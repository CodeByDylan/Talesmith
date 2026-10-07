using System.IO.Compression;
using System.Text;

namespace Talesmith.Assets.Packs;

/// <summary>Writes a content pack: add files, then <see cref="FinishAsync"/> writes the table of contents.</summary>
/// <remarks>Compressed entries that would not get at least <see cref="MinimumSaving"/> smaller are stored as they are.</remarks>
public sealed class ContentPackWriter : IAsyncDisposable
{
    /// <summary>The fraction of its size compression must save for an entry to be stored compressed.</summary>
    public const double MinimumSaving = 0.03;

    private readonly Stream _output;
    private readonly bool _ownsOutput;
    private readonly List<PackEntry> _entries = [];
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private bool _finished;

    /// <param name="output">A writable, seekable stream positioned where the pack starts.</param>
    public ContentPackWriter(Stream output, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite || !output.CanSeek)
            throw new ArgumentException("A content pack is written to a writable, seekable stream.", nameof(output));
        _output = output;
        _ownsOutput = !leaveOpen;
        output.SetLength(0);
        output.Write(new byte[ContentPack.HeaderSize]);
    }

    /// <summary>Creates the pack file, replacing an existing one.</summary>
    public static ContentPackWriter Create(string path) =>
        new(new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true));

    public IReadOnlyList<PackEntry> Entries => _entries;

    /// <summary>Adds a file read from a seekable stream.</summary>
    /// <exception cref="ArgumentException">The path is empty or already in the pack, which ignores case.</exception>
    public async Task<PackEntry> AddAsync(string path, Stream content, PackCompression compression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ObjectDisposedException.ThrowIf(_finished, this);
        var normalized = AssetPath.Normalize(path);
        if (normalized.Length == 0)
            throw new ArgumentException("A pack entry needs a path.", nameof(path));
        if (!_paths.Add(normalized))
            throw new ArgumentException($"The pack already has an entry '{normalized}'; paths in a pack ignore case.", nameof(path));

        var offset = _output.Position;
        var start = content.CanSeek ? content.Position : 0;
        var length = content.CanSeek ? content.Length - start : -1;
        if (compression == PackCompression.Brotli)
        {
            var counted = new CountingStream(content);
            await using (var brotli = new BrotliStream(_output, CompressionLevel.Optimal, leaveOpen: true))
                await counted.CopyToAsync(brotli, cancellationToken).ConfigureAwait(false);
            length = counted.BytesRead;
            var stored = _output.Position - offset;
            if (stored <= length * (1 - MinimumSaving) || !content.CanSeek)
                return Add(new PackEntry(normalized, offset, stored, length, PackCompression.Brotli));

            _output.Position = offset;
            _output.SetLength(offset);
            content.Position = start;
        }

        await content.CopyToAsync(_output, cancellationToken).ConfigureAwait(false);
        var written = _output.Position - offset;
        return Add(new PackEntry(normalized, offset, written, written, PackCompression.None));
    }

    /// <summary>Adds a file from disk.</summary>
    public async Task<PackEntry> AddFileAsync(string path, string file, PackCompression compression, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        return await AddAsync(path, stream, compression, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PackEntry> AddBytesAsync(string path, ReadOnlyMemory<byte> content, PackCompression compression, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        return await AddAsync(path, stream, compression, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the table of contents and the header; the pack is complete afterwards.</summary>
    public async Task FinishAsync(CancellationToken cancellationToken = default)
    {
        if (_finished)
            return;
        _finished = true;
        var tableOffset = _output.Position;
        using (var table = new MemoryStream())
        {
            using (var writer = new BinaryWriter(table, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var entry in _entries)
                {
                    writer.Write(entry.Path);
                    writer.Write(entry.Offset);
                    writer.Write(entry.StoredLength);
                    writer.Write(entry.Length);
                    writer.Write((byte)entry.Compression);
                }
            }

            table.Position = 0;
            await table.CopyToAsync(_output, cancellationToken).ConfigureAwait(false);
        }

        var tableLength = _output.Position - tableOffset;
        var header = new byte[ContentPack.HeaderSize];
        ContentPack.Magic.CopyTo(header);
        BitConverter.TryWriteBytes(header.AsSpan(8), ContentPack.CurrentVersion);
        BitConverter.TryWriteBytes(header.AsSpan(12), _entries.Count);
        BitConverter.TryWriteBytes(header.AsSpan(16), tableOffset);
        BitConverter.TryWriteBytes(header.AsSpan(24), tableLength);
        _output.Position = 0;
        await _output.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsOutput)
            await _output.DisposeAsync().ConfigureAwait(false);
    }

    private PackEntry Add(PackEntry entry)
    {
        _entries.Add(entry);
        return entry;
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            BytesRead += read;
            return read;
        }
    }
}
