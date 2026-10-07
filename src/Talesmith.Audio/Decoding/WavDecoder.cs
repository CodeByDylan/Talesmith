using System.Buffers.Binary;

namespace Talesmith.Audio.Decoding;

/// <summary>Decodes RIFF/WAVE files: integer PCM with 8, 16, 24 or 32 bits and 32-bit float, in mono or stereo.</summary>
/// <remarks>Samples are converted to 16 bits while reading. <see cref="Rewind"/> needs a seekable stream.</remarks>
public sealed class WavDecoder : ISeekableAudioDecoder
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;
    private const int FramesPerRead = 4096;

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly SampleFormat _format;
    private readonly int _blockAlign;
    private readonly long _dataStart;
    private readonly long _dataLength;
    private readonly byte[] _scratch;
    private long _remaining;

    /// <exception cref="InvalidDataException">The stream is not a supported WAVE file.</exception>
    public WavDecoder(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _leaveOpen = leaveOpen;

        Span<byte> header = stackalloc byte[12];
        ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WAVE"u8))
            throw new InvalidDataException("The file is not a RIFF/WAVE file.");

        var hasFormat = false;
        Span<byte> chunk = stackalloc byte[8];
        while (true)
        {
            if (stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false) < chunk.Length)
                throw new InvalidDataException(hasFormat ? "The file has no data chunk." : "The file has no fmt chunk.");

            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                (_format, Channels, SampleRate, _blockAlign) = ReadFormat(size);
                hasFormat = true;
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (!hasFormat)
                    throw new InvalidDataException("The data chunk comes before the fmt chunk.");
                break;
            }
            else
            {
                Skip(size + (size & 1));
            }
        }

        var length = (long)BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
        if (stream.CanSeek)
        {
            _dataStart = stream.Position;
            length = Math.Min(length, stream.Length - _dataStart);
        }

        _dataLength = length - length % _blockAlign;
        _remaining = _dataLength;
        _scratch = new byte[FramesPerRead * _blockAlign];
    }

    private enum SampleFormat
    {
        UInt8,
        Int16,
        Int24,
        Int32,
        Float32
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int Read(Span<short> buffer)
    {
        var written = 0;
        while (_remaining > 0)
        {
            var frames = (int)Math.Min(Math.Min((buffer.Length - written) / Channels, FramesPerRead), _remaining / _blockAlign);
            if (frames == 0)
                break;

            var bytes = frames * _blockAlign;
            var read = _stream.ReadAtLeast(_scratch.AsSpan(0, bytes), bytes, throwOnEndOfStream: false);
            read -= read % _blockAlign;
            _remaining = read < bytes ? 0 : _remaining - read;
            written += Convert(_scratch.AsSpan(0, read), buffer[written..]);
        }

        return written;
    }

    /// <summary>The length of the audio in sample frames.</summary>
    public long Frames => _dataLength / _blockAlign;

    public void Seek(long frame)
    {
        if (!_stream.CanSeek)
            throw new NotSupportedException("Seeking needs a seekable stream.");
        var offset = Math.Clamp(frame * _blockAlign, 0, _dataLength);
        _stream.Position = _dataStart + offset;
        _remaining = _dataLength - offset;
    }

    public void Rewind()
    {
        if (!_stream.CanSeek)
            throw new NotSupportedException("Rewinding needs a seekable stream.");
        _stream.Position = _dataStart;
        _remaining = _dataLength;
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }

    private (SampleFormat Format, int Channels, int SampleRate, int BlockAlign) ReadFormat(uint size)
    {
        if (size < 16)
            throw new InvalidDataException("The fmt chunk is too short.");

        Span<byte> fmt = stackalloc byte[40];
        var used = (int)Math.Min(size, (uint)fmt.Length);
        ReadExactly(fmt[..used]);
        Skip(size - (uint)used + (size & 1));

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
        if (tag == FormatExtensible)
        {
            if (used < 26)
                throw new InvalidDataException("The extensible fmt chunk is too short.");
            tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]);
        }

        if (channels is not (1 or 2))
            throw new InvalidDataException($"Only mono and stereo are supported, not {channels} channels.");
        if (sampleRate <= 0)
            throw new InvalidDataException($"The sample rate {sampleRate} is invalid.");

        var format = (tag, bits) switch
        {
            (FormatPcm, 8) => SampleFormat.UInt8,
            (FormatPcm, 16) => SampleFormat.Int16,
            (FormatPcm, 24) => SampleFormat.Int24,
            (FormatPcm, 32) => SampleFormat.Int32,
            (FormatFloat, 32) => SampleFormat.Float32,
            _ => throw new InvalidDataException($"The sample format {tag} with {bits} bits is not supported.")
        };
        return (format, channels, sampleRate, channels * bits / 8);
    }

    private int Convert(ReadOnlySpan<byte> source, Span<short> target)
    {
        var count = source.Length / (_blockAlign / Channels);
        switch (_format)
        {
            case SampleFormat.UInt8:
                for (var i = 0; i < count; i++)
                    target[i] = (short)((source[i] - 128) << 8);
                break;
            case SampleFormat.Int16:
                for (var i = 0; i < count; i++)
                    target[i] = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 2)..]);
                break;
            case SampleFormat.Int24:
                for (var i = 0; i < count; i++)
                    target[i] = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 3 + 1)..]);
                break;
            case SampleFormat.Int32:
                for (var i = 0; i < count; i++)
                    target[i] = BinaryPrimitives.ReadInt16LittleEndian(source[(i * 4 + 2)..]);
                break;
            case SampleFormat.Float32:
                for (var i = 0; i < count; i++)
                    target[i] = SampleConversion.ToInt16(BinaryPrimitives.ReadSingleLittleEndian(source[(i * 4)..]));
                break;
        }

        return count;
    }

    private void ReadExactly(Span<byte> buffer)
    {
        if (_stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length)
            throw new InvalidDataException("The file ends in the middle of its header.");
    }

    private void Skip(long count)
    {
        if (_stream.CanSeek)
        {
            _stream.Seek(count, SeekOrigin.Current);
            return;
        }

        Span<byte> discard = stackalloc byte[256];
        while (count > 0)
        {
            var read = _stream.Read(discard[..(int)Math.Min(count, discard.Length)]);
            if (read == 0)
                return;
            count -= read;
        }
    }
}
