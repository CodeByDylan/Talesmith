namespace Talesmith.Audio.Decoding;

/// <summary>A decoder that can jump to any sample frame.</summary>
public interface ISeekableAudioDecoder : IAudioDecoder
{
    /// <summary>Continues reading at a sample frame from the start; frames past the end read nothing.</summary>
    void Seek(long frame);
}

/// <summary>Plays a decoder from the start, then loops between a start and an end frame.</summary>
/// <remarks>
/// Reading stops at the loop end, and <see cref="Rewind"/> continues at the loop start, so a player that rewinds when a looping stream
/// ends plays the intro once and then the loop. Decoders that cannot seek are rewound and read up to the loop start.
/// </remarks>
public sealed class LoopRegionDecoder : IAudioDecoder
{
    private readonly IAudioDecoder _inner;
    private readonly long _loopStart;
    private readonly long _loopEnd;
    private short[] _discard = [];
    private long _position;

    /// <param name="loopEnd">The frame to jump back at, exclusive; 0 or less loops at the end of the stream.</param>
    public LoopRegionDecoder(IAudioDecoder inner, long loopStart, long loopEnd)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegative(loopStart);
        if (loopEnd > 0 && loopEnd <= loopStart)
            throw new ArgumentOutOfRangeException(nameof(loopEnd), "The loop must end after it starts.");
        _inner = inner;
        _loopStart = loopStart;
        _loopEnd = loopEnd;
    }

    public int SampleRate => _inner.SampleRate;

    public int Channels => _inner.Channels;

    public int Read(Span<short> buffer)
    {
        if (_loopEnd > 0)
        {
            var framesLeft = _loopEnd - _position;
            if (framesLeft <= 0)
                return 0;
            buffer = buffer[..(int)Math.Min(buffer.Length, framesLeft * Channels)];
        }

        var read = _inner.Read(buffer);
        _position += read / Channels;
        return read;
    }

    public void Rewind()
    {
        if (_inner is ISeekableAudioDecoder seekable)
        {
            seekable.Seek(_loopStart);
            _position = _loopStart;
            return;
        }

        _inner.Rewind();
        _position = 0;
        if (_discard.Length == 0)
            _discard = new short[4096 * Channels];
        while (_position < _loopStart)
        {
            var read = _inner.Read(_discard.AsSpan(0, (int)Math.Min(_discard.Length, (_loopStart - _position) * Channels)));
            if (read == 0)
                break;
            _position += read / Channels;
        }
    }

    public void Dispose() => _inner.Dispose();
}
