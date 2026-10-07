using System.IO.Compression;
using System.IO.Enumeration;

namespace Talesmith.Assets.Packs;

/// <summary>Reads assets from a content pack written by a build, and files missing from it from an optional fallback, such as loose config files.</summary>
/// <remarks>
/// Lookups ignore case, so games behave the same on every platform. Stored entries are read straight from the pack file through a
/// seekable view; compressed entries are decompressed into memory when opened. Safe to use from several threads.
/// </remarks>
public sealed class PackAssetSource : IAssetSource
{
    private readonly Dictionary<string, PackEntry> _entries;
    private readonly IAssetSource? _fallback;

    /// <exception cref="FileNotFoundException">The pack does not exist.</exception>
    /// <exception cref="AssetException">The file is not a valid content pack.</exception>
    public PackAssetSource(string packPath, IAssetSource? fallback = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(packPath);
        PackPath = Path.GetFullPath(packPath);
        _fallback = fallback;
        using var stream = File.OpenRead(PackPath);
        Entries = ContentPack.ReadTable(stream);
        _entries = new Dictionary<string, PackEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
            _entries[AssetPath.Normalize(entry.Path)] = entry;
    }

    public string PackPath { get; }

    public IReadOnlyList<PackEntry> Entries { get; }

    public string Description => _fallback is null ? PackPath : $"{PackPath} and {_fallback.Description}";

    /// <summary>Uses the pack in an asset folder when there is one, with the folder as fallback; otherwise the folder alone.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static IAssetSource OpenFolder(string assetRoot)
    {
        var folder = new FileSystemAssetSource(assetRoot);
        return ContentPack.Find(folder.RootFolder) is { } pack ? new PackAssetSource(pack, folder) : folder;
    }

    public bool Exists(string path) => _entries.ContainsKey(AssetPath.Normalize(path)) || _fallback?.Exists(path) == true;

    public Stream OpenRead(string path)
    {
        if (!_entries.TryGetValue(AssetPath.Normalize(path), out var entry))
            return _fallback?.OpenRead(path) ?? throw new FileNotFoundException($"The asset '{path}' is not in {PackPath}.", path);

        var file = new FileStream(PackPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 14);
        if (entry.Compression == PackCompression.None)
            return new EntryStream(file, entry.Offset, entry.StoredLength);

        using (file)
        {
            file.Position = entry.Offset;
            using var stored = new EntryStream(file, entry.Offset, entry.StoredLength, ownsFile: false);
            using var brotli = new BrotliStream(stored, CompressionMode.Decompress);
            var content = new byte[entry.Length];
            brotli.ReadExactly(content);
            return new MemoryStream(content, writable: false);
        }
    }

    public IEnumerable<string> List(string folder, string pattern = "*", bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        if (pattern.AsSpan().ContainsAny('/', '\\'))
            throw new ArgumentException("The pattern must match file names only, without folders.", nameof(pattern));

        var normalized = AssetPath.Normalize(folder);
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _entries.Keys)
        {
            var directory = AssetPath.GetDirectory(path);
            var inFolder = recursive
                ? normalized.Length == 0 || string.Equals(directory, normalized, StringComparison.OrdinalIgnoreCase)
                  || directory.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase)
                : string.Equals(directory, normalized, StringComparison.OrdinalIgnoreCase);
            if (inFolder && FileSystemName.MatchesSimpleExpression(pattern, AssetPath.GetFileName(path)))
                found.Add(path);
        }

        if (_fallback is not null)
            found.UnionWith(_fallback.List(folder, pattern, recursive));
        return found;
    }

    public string? GetFullPath(string path) => _entries.ContainsKey(AssetPath.Normalize(path)) ? null : _fallback?.GetFullPath(path);

    /// <summary>A read-only, seekable view of one entry's bytes in the pack file.</summary>
    private sealed class EntryStream(FileStream file, long offset, long length, bool ownsFile = true) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, length);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, length - _position);
            if (count <= 0)
                return 0;
            file.Position = offset + _position;
            var read = file.Read(buffer[..count]);
            _position += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = (int)Math.Min(buffer.Length, length - _position);
            if (count <= 0)
                return 0;
            file.Position = offset + _position;
            var read = await file.ReadAsync(buffer[..count], cancellationToken).ConfigureAwait(false);
            _position += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && ownsFile)
                file.Dispose();
            base.Dispose(disposing);
        }
    }
}
