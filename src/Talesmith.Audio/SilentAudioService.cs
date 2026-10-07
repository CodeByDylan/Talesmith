namespace Talesmith.Audio;

/// <summary>An <see cref="IAudioService"/> without a device: it plays nothing but keeps track of sounds as if it did.</summary>
/// <remarks>A sound counts as playing until its clip's duration, adjusted for pitch, has passed in <see cref="Update"/>; looping sounds play until stopped.</remarks>
public sealed class SilentAudioService : IAudioService
{
    private readonly Lock _lock = new();
    private readonly Dictionary<int, double> _endTimes = new();
    private readonly float[] _busVolumes = [1, 1, 1, 1];
    private double _clock;
    private int _lastId;
    private float _masterVolume = 1;
    private bool _paused;
    private MusicTrack? _music;

    public string DeviceName => "Silent";

    public float MasterVolume
    {
        get => Volatile.Read(ref _masterVolume);
        set => Volatile.Write(ref _masterVolume, Math.Clamp(value, 0, 1));
    }

    public bool IsPaused
    {
        get => Volatile.Read(ref _paused);
        set => Volatile.Write(ref _paused, value);
    }

    public int ActiveSounds
    {
        get
        {
            lock (_lock)
                return _endTimes.Count;
        }
    }

    public float GetBusVolume(AudioBus bus) => Volatile.Read(ref _busVolumes[(int)bus]);

    public void SetBusVolume(AudioBus bus, float volume) => Volatile.Write(ref _busVolumes[(int)bus], Math.Clamp(volume, 0, 1));

    public SoundHandle Play(SoundClip clip, SoundOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var settings = options ?? SoundOptions.For(clip);
        lock (_lock)
        {
            var id = _lastId = _lastId == int.MaxValue ? 1 : _lastId + 1;
            _endTimes[id] = settings.Loop ? double.PositiveInfinity : _clock + clip.Duration.TotalSeconds / Math.Max(settings.Pitch, 0.01f);
            return new SoundHandle(id);
        }
    }

    public void Stop(SoundHandle sound)
    {
        lock (_lock)
            _endTimes.Remove(sound.Id);
    }

    public bool IsPlaying(SoundHandle sound)
    {
        lock (_lock)
            return _endTimes.ContainsKey(sound.Id);
    }

    public void SetVolume(SoundHandle sound, float volume)
    {
    }

    public void SetPan(SoundHandle sound, float pan)
    {
    }

    public void SetPitch(SoundHandle sound, float pitch)
    {
    }

    public MusicTrack? CurrentMusic
    {
        get
        {
            lock (_lock)
                return _music;
        }
    }

    public void PlayMusic(MusicTrack track, bool loop = true, TimeSpan fade = default, float volume = 1)
    {
        ArgumentNullException.ThrowIfNull(track);
        lock (_lock)
            _music = track;
    }

    public void StopMusic(TimeSpan fade = default)
    {
        lock (_lock)
            _music = null;
    }

    public void Update(float deltaSeconds)
    {
        if (IsPaused)
            return;

        lock (_lock)
        {
            _clock += deltaSeconds;
            foreach (var (id, endTime) in _endTimes)
            {
                if (endTime <= _clock)
                    _endTimes.Remove(id);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
            _endTimes.Clear();
    }
}
