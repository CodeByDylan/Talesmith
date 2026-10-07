using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.OpenAL;
using Talesmith.Audio.Decoding;

namespace Talesmith.Audio.OpenAL;

/// <summary>Plays audio on the default output device through OpenAL Soft.</summary>
/// <remarks>
/// Sound effects play on a pool of 64 voices; when all are busy the oldest non-looping sound is cut off, and when all are looping
/// <see cref="Play"/> returns an empty handle. Clips are uploaded to the device on first play and cached within a memory budget.
/// Music streams from its decoder on a background thread, so it keeps playing smoothly when frames are slow; fades advance in
/// <see cref="Update"/>. Panning applies to mono clips only; stereo clips play unpanned with their channels sent straight to the
/// speakers.
/// </remarks>
public sealed unsafe partial class OpenALAudioService : IAudioService
{
    internal const SourceInteger DirectChannels = (SourceInteger)0x1033;

    private const int VoiceCount = 64;
    private const int IndexBits = 8;
    private const int IndexMask = (1 << IndexBits) - 1;
    private const int MaxGeneration = int.MaxValue >> IndexBits;
    private const long ClipCacheBytes = 64L * 1024 * 1024;
    private const float MinimumPitch = 0.01f;
    private static readonly TimeSpan StreamInterval = TimeSpan.FromMilliseconds(10);

    private readonly Lock _lock = new();
    private readonly ILogger _logger;
    private readonly ALContext _alc;
    private readonly AL _al;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly bool _directChannels;
    private readonly Voice[] _voices;
    private readonly MusicChannel[] _music;
    private readonly ClipBufferCache _buffers;
    private readonly float[] _busVolumes = [1, 1, 1, 1];
    private readonly ManualResetEventSlim _stopStreaming = new();
    private readonly Thread _streamThread;
    private MusicChannel? _currentMusic;
    private float _masterVolume = 1;
    private long _playCount;
    private bool _paused;
    private bool _disposed;

    /// <exception cref="AudioDeviceException">OpenAL Soft could not be loaded or no output device could be opened.</exception>
    public OpenALAudioService(ILogger<OpenALAudioService>? logger = null)
    {
        _logger = logger ?? NullLogger<OpenALAudioService>.Instance;
        (_alc, _al) = OpenALLibrary.Load();

        _device = _alc.OpenDevice(null);
        if (_device == null)
        {
            ReleaseApis();
            throw new AudioDeviceException("No audio output device could be opened.");
        }

        _context = _alc.CreateContext(_device, null);
        if (_context == null || !_alc.MakeContextCurrent(_context))
        {
            ReleaseDevice();
            throw new AudioDeviceException($"OpenAL could not create a context on the audio device: {_alc.GetError(_device)}.");
        }

        DeviceName = _alc.GetContextProperty(_device, GetContextString.DeviceSpecifier);
        _directChannels = _al.IsExtensionPresent("AL_SOFT_direct_channels");
        _al.DistanceModel(DistanceModel.None);
        _music = [new MusicChannel(_al, _directChannels), new MusicChannel(_al, _directChannels)];
        _voices = CreateVoices();
        _buffers = new ClipBufferCache(_al, ClipCacheBytes, _al.IsExtensionPresent("AL_SOFT_loop_points"));

        _streamThread = new Thread(StreamMusic) { Name = "Talesmith music streaming", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _streamThread.Start();
    }

    public string DeviceName { get; }

    public float MasterVolume
    {
        get
        {
            lock (_lock)
                return _masterVolume;
        }
        set
        {
            lock (_lock)
            {
                _masterVolume = Math.Clamp(value, 0, 1);
                if (!_disposed)
                    _al.SetListenerProperty(ListenerFloat.Gain, _masterVolume);
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_lock)
                return _paused;
        }
        set
        {
            lock (_lock)
            {
                if (_paused == value || _disposed)
                    return;

                _paused = value;
                if (value)
                    PauseAll();
                else
                    ResumeAll();
            }
        }
    }

    public int ActiveSounds
    {
        get
        {
            lock (_lock)
            {
                var count = 0;
                if (_disposed)
                    return count;

                foreach (var voice in _voices)
                {
                    if (voice.IsActive && !IsFinished(voice))
                        count++;
                }

                return count;
            }
        }
    }

    public float GetBusVolume(AudioBus bus)
    {
        lock (_lock)
            return _busVolumes[(int)bus];
    }

    public void SetBusVolume(AudioBus bus, float volume)
    {
        lock (_lock)
        {
            var busVolume = _busVolumes[(int)bus] = Math.Clamp(volume, 0, 1);
            if (_disposed)
                return;

            foreach (var voice in _voices)
            {
                if (voice.IsActive && voice.Bus == bus)
                    _al.SetSourceProperty(voice.Source, SourceFloat.Gain, voice.Volume * busVolume);
            }

            if (bus != AudioBus.Music)
                return;

            foreach (var channel in _music)
                channel.ApplyGain(busVolume);
        }
    }

    /// <exception cref="ArgumentException">The clip has more than two channels.</exception>
    public SoundHandle Play(SoundClip clip, SoundOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.Channels is not (1 or 2))
            throw new ArgumentException($"Only mono and stereo clips can play, and '{clip.Path}' has {clip.Channels} channels.", nameof(clip));

        var settings = options ?? SoundOptions.For(clip);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (clip.Samples.Length < clip.Channels || AcquireVoice() is not { } voice)
                return default;

            var buffer = _buffers.Acquire(clip);
            voice.Buffer = buffer;
            voice.Bus = settings.Bus;
            voice.Volume = Math.Max(settings.Volume, 0) * clip.Volume;
            voice.Loop = settings.Loop;
            voice.StartOrder = ++_playCount;
            voice.Generation = voice.Generation % MaxGeneration + 1;

            var source = voice.Source;
            var pan = clip.Channels == 1 ? Math.Clamp(settings.Pan, -1, 1) : 0;
            _al.SetSourceProperty(source, SourceInteger.Buffer, buffer.Name);
            _al.SetSourceProperty(source, SourceBoolean.Looping, settings.Loop);
            _al.SetSourceProperty(source, SourceFloat.Pitch, Math.Max(settings.Pitch, MinimumPitch));
            _al.SetSourceProperty(source, SourceFloat.Gain, voice.Volume * _busVolumes[(int)settings.Bus]);
            SetPan(source, pan);
            if (_directChannels)
                _al.SetSourceProperty(source, DirectChannels, clip.Channels == 2 ? 1 : 0);
            if (!_paused)
                _al.SourcePlay(source);

            return new SoundHandle(voice.Generation << IndexBits | (voice.Index + 1));
        }
    }

    public void Stop(SoundHandle sound)
    {
        lock (_lock)
        {
            if (FindVoice(sound) is { } voice)
                Release(voice);
        }
    }

    public bool IsPlaying(SoundHandle sound)
    {
        lock (_lock)
            return FindVoice(sound) is { } voice && !IsFinished(voice);
    }

    public void SetVolume(SoundHandle sound, float volume)
    {
        lock (_lock)
        {
            if (FindVoice(sound) is not { } voice)
                return;
            voice.Volume = Math.Max(volume, 0) * voice.Buffer!.Clip.Volume;
            _al.SetSourceProperty(voice.Source, SourceFloat.Gain, voice.Volume * _busVolumes[(int)voice.Bus]);
        }
    }

    public void SetPan(SoundHandle sound, float pan)
    {
        lock (_lock)
        {
            if (FindVoice(sound) is { Buffer.Clip.Channels: 1 } voice)
                SetPan(voice.Source, Math.Clamp(pan, -1, 1));
        }
    }

    public void SetPitch(SoundHandle sound, float pitch)
    {
        lock (_lock)
        {
            if (FindVoice(sound) is { } voice)
                _al.SetSourceProperty(voice.Source, SourceFloat.Pitch, Math.Max(pitch, MinimumPitch));
        }
    }

    public MusicTrack? CurrentMusic
    {
        get
        {
            lock (_lock)
                return _currentMusic is { IsActive: true } current ? current.Track : null;
        }
    }

    /// <exception cref="ArgumentException">The track has more than two channels.</exception>
    public void PlayMusic(MusicTrack track, bool loop = true, TimeSpan fade = default, float volume = 1)
    {
        ArgumentNullException.ThrowIfNull(track);
        var decoder = track.OpenDecoder();
        if (decoder.Channels is not (1 or 2))
        {
            decoder.Dispose();
            throw new ArgumentException($"Only mono and stereo music can play, and '{track.Path}' has {decoder.Channels} channels.", nameof(track));
        }

        if (loop && track.HasLoopRegion)
            decoder = new LoopRegionDecoder(decoder, track.LoopStart, track.LoopEnd);

        var fadeSeconds = (float)fade.TotalSeconds;
        lock (_lock)
        {
            if (_disposed)
            {
                decoder.Dispose();
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            var outgoing = _currentMusic is { IsActive: true } ? _currentMusic : null;
            var next = PickFreeMusicChannel(outgoing);
            if (outgoing is not null)
                FadeOut(outgoing, fadeSeconds);

            try
            {
                next.Start(track, decoder, loop, fadeSeconds, Math.Clamp(volume, 0, 1) * track.Volume, _busVolumes[(int)AudioBus.Music], _paused);
            }
            catch
            {
                next.Stop();
                throw;
            }

            _currentMusic = next;
        }
    }

    public void StopMusic(TimeSpan fade = default)
    {
        lock (_lock)
        {
            if (_disposed || _currentMusic is not { IsActive: true } current)
                return;

            FadeOut(current, (float)fade.TotalSeconds);
            _currentMusic = null;
        }
    }

    public void Update(float deltaSeconds)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            foreach (var voice in _voices)
            {
                if (voice.IsActive && IsFinished(voice))
                    Release(voice);
            }

            if (_paused)
                return;

            foreach (var channel in _music)
            {
                if (!channel.IsActive || !channel.AdvanceFade(deltaSeconds))
                    continue;

                if (channel.IsFadedOut)
                    channel.Stop();
                else
                    channel.ApplyGain(_busVolumes[(int)AudioBus.Music]);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _stopStreaming.Set();
        _streamThread.Join();
        lock (_lock)
        {
            foreach (var voice in _voices)
            {
                if (voice.IsActive)
                    Release(voice);
                _al.DeleteSource(voice.Source);
            }

            foreach (var channel in _music)
                channel.Dispose();
            _buffers.Dispose();
            ReleaseDevice();
        }

        _stopStreaming.Dispose();
    }

    private void SetPan(uint source, float pan) => _al.SetSourceProperty(source, SourceVector3.Position, pan, 0, -MathF.Sqrt(1 - pan * pan));

    private Voice[] CreateVoices()
    {
        var voices = new List<Voice>(VoiceCount);
        _al.GetError();
        while (voices.Count < VoiceCount)
        {
            var source = _al.GenSource();
            if (_al.GetError() != AudioError.NoError)
                break;

            _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
            voices.Add(new Voice(voices.Count, source));
        }

        return [.. voices];
    }

    private Voice? AcquireVoice()
    {
        Voice? oldest = null;
        foreach (var voice in _voices)
        {
            if (!voice.IsActive)
                return voice;

            if (IsFinished(voice))
            {
                Release(voice);
                return voice;
            }

            if (!voice.Loop && (oldest is null || voice.StartOrder < oldest.StartOrder))
                oldest = voice;
        }

        if (oldest is not null)
            Release(oldest);
        return oldest;
    }

    private Voice? FindVoice(SoundHandle sound)
    {
        var index = (sound.Id & IndexMask) - 1;
        if (_disposed || index < 0 || index >= _voices.Length)
            return null;

        var voice = _voices[index];
        return voice.IsActive && voice.Generation == sound.Id >> IndexBits ? voice : null;
    }

    private bool IsFinished(Voice voice)
    {
        _al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out var state);
        return (SourceState)state == SourceState.Stopped;
    }

    private void Release(Voice voice)
    {
        _al.SourceStop(voice.Source);
        _al.SetSourceProperty(voice.Source, SourceInteger.Buffer, 0);
        _buffers.Release(voice.Buffer!);
        voice.Buffer = null;
    }

    private MusicChannel PickFreeMusicChannel(MusicChannel? outgoing)
    {
        var free = Array.Find(_music, channel => !channel.IsActive);
        if (free is not null)
            return free;

        var other = _music[0] == outgoing ? _music[1] : _music[0];
        other.Stop();
        return other;
    }

    private static void FadeOut(MusicChannel channel, float seconds)
    {
        if (seconds > 0)
            channel.FadeTo(0, seconds);
        else
            channel.Stop();
    }

    private void PauseAll()
    {
        foreach (var voice in _voices)
        {
            if (!voice.IsActive)
                continue;

            _al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out var state);
            if ((SourceState)state == SourceState.Playing)
                _al.SourcePause(voice.Source);
        }

        foreach (var channel in _music)
            channel.Pause();
    }

    private void ResumeAll()
    {
        foreach (var voice in _voices)
        {
            if (!voice.IsActive)
                continue;

            _al.GetSourceProperty(voice.Source, GetSourceInteger.SourceState, out var state);
            if ((SourceState)state is SourceState.Paused or SourceState.Initial)
                _al.SourcePlay(voice.Source);
        }

        foreach (var channel in _music)
            channel.Resume();
    }

    private void StreamMusic()
    {
        while (!_stopStreaming.Wait(StreamInterval))
        {
            lock (_lock)
            {
                if (_disposed)
                    return;

                foreach (var channel in _music)
                    RefillSafely(channel);
            }
        }
    }

    private void RefillSafely(MusicChannel channel)
    {
        try
        {
            if (channel.Refill(_paused))
                channel.Stop();
        }
        catch (Exception ex)
        {
            LogStreamingFailed(channel.TrackPath, ex);
            channel.Stop();
        }
    }

    private void ReleaseDevice()
    {
        _alc.MakeContextCurrent(null);
        if (_context != null)
            _alc.DestroyContext(_context);
        _alc.CloseDevice(_device);
        ReleaseApis();
    }

    private void ReleaseApis()
    {
        _al.Dispose();
        _alc.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Streaming the music track {Path} failed, so it was stopped")]
    private partial void LogStreamingFailed(string? path, Exception exception);
}
