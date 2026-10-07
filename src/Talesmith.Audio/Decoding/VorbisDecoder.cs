using NVorbis;

namespace Talesmith.Audio.Decoding;

/// <summary>Decodes Ogg Vorbis streams in mono or stereo.</summary>
/// <remarks><see cref="Rewind"/> needs a seekable stream.</remarks>
internal sealed class VorbisDecoder : IAudioDecoder
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly long _start;
    private VorbisReader _reader;
    private float[] _scratch = [];

    /// <exception cref="InvalidDataException">The stream is not a supported Ogg Vorbis stream.</exception>
    public VorbisDecoder(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _leaveOpen = leaveOpen;
        _start = stream.CanSeek ? stream.Position : 0;
        _reader = OpenReader();
        if (_reader.Channels is not (1 or 2))
        {
            var channels = _reader.Channels;
            Dispose();
            throw new InvalidDataException($"Only mono and stereo are supported, not {channels} channels.");
        }
    }

    public int SampleRate => _reader.SampleRate;

    public int Channels => _reader.Channels;

    public int Read(Span<short> buffer)
    {
        var count = buffer.Length - buffer.Length % Channels;
        if (_scratch.Length < count)
            _scratch = new float[count];

        var read = _reader.ReadSamples(_scratch.AsSpan(0, count));
        for (var i = 0; i < read; i++)
            buffer[i] = SampleConversion.ToInt16(_scratch[i]);
        return read;
    }

    public void Rewind()
    {
        if (!_stream.CanSeek)
            throw new NotSupportedException("Rewinding needs a seekable stream.");

        // Seeking NVorbis 0.10.5 back to the start drops and silences samples, so the reader is reopened instead.
        _reader.Dispose();
        _stream.Position = _start;
        _reader = OpenReader();
    }

    public void Dispose()
    {
        _reader.Dispose();
        if (!_leaveOpen)
            _stream.Dispose();
    }

    private VorbisReader OpenReader()
    {
        try
        {
            return new VorbisReader(_stream, closeOnDispose: false);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("The file is not an Ogg Vorbis stream.", ex);
        }
    }
}
