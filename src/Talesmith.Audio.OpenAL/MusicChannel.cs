using Silk.NET.OpenAL;

namespace Talesmith.Audio.OpenAL;

/// <summary>Streams one music track through an OpenAL source with a small queue of buffers, and fades its gain.</summary>
/// <remarks>Not thread-safe; the audio service calls it under its lock.</remarks>
internal sealed unsafe class MusicChannel : IDisposable
{
    private const int BufferCount = 4;
    private const int BuffersPerSecond = 8;

    private readonly AL _al;
    private readonly uint _source;
    private readonly uint[] _buffers;
    private IAudioDecoder? _decoder;
    private short[] _samples = [];
    private int _samplesPerBuffer;
    private BufferFormat _format;
    private int _sampleRate;
    private bool _loop;
    private bool _endOfStream;
    private bool _readSinceRewind;
    private float _fadeRate;

    public MusicChannel(AL al, bool directChannels)
    {
        _al = al;
        _source = al.GenSource();
        _buffers = al.GenBuffers(BufferCount);
        al.SetSourceProperty(_source, SourceBoolean.SourceRelative, true);
        al.SetSourceProperty(_source, SourceVector3.Position, 0, 0, 0);
        if (directChannels)
            al.SetSourceProperty(_source, OpenALAudioService.DirectChannels, 1);
    }

    /// <summary>The track being streamed, or null when the channel is idle.</summary>
    public MusicTrack? Track { get; private set; }

    public string? TrackPath => Track?.Path;

    public bool IsActive => _decoder is not null;

    /// <summary>The fade level from 0 to 1, multiplied by the track volume and the music bus volume.</summary>
    public float Gain { get; private set; }

    public float TargetGain { get; private set; }

    /// <summary>The volume the track was started with, including its own volume.</summary>
    public float Volume { get; private set; } = 1;

    public bool IsFadedOut => IsActive && TargetGain == 0 && Gain == 0;

    public void Start(MusicTrack track, IAudioDecoder decoder, bool loop, float fadeSeconds, float volume, float busVolume, bool paused)
    {
        Stop();
        Track = track;
        Volume = volume;
        _decoder = decoder;
        _loop = loop;
        _endOfStream = false;
        _readSinceRewind = false;
        _format = decoder.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        _sampleRate = decoder.SampleRate;
        _samplesPerBuffer = Math.Max(decoder.SampleRate / BuffersPerSecond, 1) * decoder.Channels;
        if (_samples.Length < _samplesPerBuffer)
            _samples = new short[_samplesPerBuffer];

        foreach (var buffer in _buffers)
        {
            if (!Fill(buffer))
                break;
            Queue(buffer);
        }

        FadeTo(1, fadeSeconds);
        ApplyGain(busVolume);
        if (!paused)
            _al.SourcePlay(_source);
    }

    public void FadeTo(float target, float seconds)
    {
        TargetGain = target;
        if (seconds <= 0)
        {
            Gain = target;
            _fadeRate = 0;
        }
        else
        {
            _fadeRate = Math.Abs(target - Gain) / seconds;
        }
    }

    /// <summary>Moves the gain toward its target and returns whether it changed.</summary>
    public bool AdvanceFade(float deltaSeconds)
    {
        if (Gain == TargetGain)
            return false;

        var step = _fadeRate * deltaSeconds;
        Gain = Gain < TargetGain ? Math.Min(Gain + step, TargetGain) : Math.Max(Gain - step, TargetGain);
        return true;
    }

    public void ApplyGain(float busVolume) => _al.SetSourceProperty(_source, SourceFloat.Gain, Gain * Volume * busVolume);

    /// <summary>Refills played buffers from the decoder and returns true once the track has finished playing.</summary>
    public bool Refill(bool paused)
    {
        if (!IsActive)
            return false;

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out var processed);
        for (var i = 0; i < processed; i++)
        {
            uint buffer;
            _al.SourceUnqueueBuffers(_source, 1, &buffer);
            if (!_endOfStream && Fill(buffer))
                Queue(buffer);
        }

        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
        if ((SourceState)state != SourceState.Stopped)
            return false;

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out var queued);
        if (queued == 0)
            return true;

        // The queue ran dry before it was refilled, which stops the source.
        if (!paused)
            _al.SourcePlay(_source);
        return false;
    }

    public void Pause()
    {
        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
        if ((SourceState)state == SourceState.Playing)
            _al.SourcePause(_source);
    }

    public void Resume()
    {
        if (!IsActive)
            return;

        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out var state);
        if ((SourceState)state is SourceState.Paused or SourceState.Initial)
            _al.SourcePlay(_source);
    }

    public void Stop()
    {
        _al.SourceStop(_source);
        _al.SetSourceProperty(_source, SourceInteger.Buffer, 0);
        _decoder?.Dispose();
        _decoder = null;
        Track = null;
        Gain = TargetGain = 0;
    }

    public void Dispose()
    {
        Stop();
        _al.DeleteSource(_source);
        _al.DeleteBuffers(_buffers);
    }

    private bool Fill(uint buffer)
    {
        var filled = 0;
        while (filled < _samplesPerBuffer)
        {
            var read = _decoder!.Read(_samples.AsSpan(filled, _samplesPerBuffer - filled));
            if (read > 0)
            {
                filled += read;
                _readSinceRewind = true;
                continue;
            }

            if (!_loop || !_readSinceRewind)
            {
                _endOfStream = true;
                break;
            }

            _decoder.Rewind();
            _readSinceRewind = false;
        }

        if (filled == 0)
            return false;

        fixed (short* samples = _samples)
            _al.BufferData(buffer, _format, samples, filled * sizeof(short), _sampleRate);
        return true;
    }

    private void Queue(uint buffer) => _al.SourceQueueBuffers(_source, 1, &buffer);
}
