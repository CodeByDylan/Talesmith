namespace Talesmith.Audio;

/// <summary>The mixer channel a sound plays on; each bus has its own volume.</summary>
public enum AudioBus
{
    Effects,
    Music,
    Voice,
    Interface
}

/// <summary>A sound effect fully decoded into memory, for short, frequently played sounds.</summary>
/// <param name="Samples">Interleaved 16-bit PCM samples.</param>
public sealed record SoundClip(string Path, int SampleRate, int Channels, short[] Samples)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)(SampleRate * Channels));

    /// <summary>The number of sample frames, one sample per channel each.</summary>
    public int Frames => Samples.Length / Channels;

    /// <summary>Whether the clip loops when played without options.</summary>
    public bool Loop { get; init; }

    /// <summary>The frame looping restarts at.</summary>
    public int LoopStart { get; init; }

    /// <summary>The frame looping jumps back at, exclusive; 0 loops at the end.</summary>
    public int LoopEnd { get; init; }

    /// <summary>The bus the clip plays on when played without options.</summary>
    public AudioBus Bus { get; init; } = AudioBus.Effects;

    /// <summary>A volume multiplier applied every time the clip plays.</summary>
    public float Volume { get; init; } = 1;

    /// <summary>Whether looping uses a region other than the whole clip.</summary>
    public bool HasLoopRegion => LoopStart > 0 || (LoopEnd > 0 && LoopEnd < Frames);
}

/// <summary>A music track that is decoded while it plays instead of all at once.</summary>
/// <remarks>Each <see cref="OpenDecoder"/> call returns an independent decoder, so a track can be playing and preloaded at the same time.</remarks>
public sealed class MusicTrack(string path, Func<IAudioDecoder> openDecoder)
{
    public string Path { get; } = path;

    /// <summary>Whether the track loops when played by components that do not say otherwise.</summary>
    public bool Loop { get; init; }

    /// <summary>The frame looping restarts at; the part before it plays once, as an intro.</summary>
    public long LoopStart { get; init; }

    /// <summary>The frame looping jumps back at, exclusive; 0 loops at the end.</summary>
    public long LoopEnd { get; init; }

    /// <summary>A volume multiplier applied every time the track plays.</summary>
    public float Volume { get; init; } = 1;

    public bool HasLoopRegion => LoopStart > 0 || LoopEnd > 0;

    public IAudioDecoder OpenDecoder() => openDecoder();
}

/// <summary>Reads PCM samples from a compressed or uncompressed stream.</summary>
public interface IAudioDecoder : IDisposable
{
    int SampleRate { get; }

    int Channels { get; }

    /// <summary>Fills a buffer with interleaved 16-bit samples and returns how many were written; 0 at the end.</summary>
    int Read(Span<short> buffer);

    /// <summary>Restarts from the beginning, for looping.</summary>
    void Rewind();
}

/// <summary>Options for playing a sound effect.</summary>
/// <param name="Pitch">Playback speed; 1 is normal, 2 is an octave higher.</param>
/// <param name="Pan">Stereo position from -1, left, to 1, right.</param>
public readonly record struct SoundOptions(float Volume = 1, float Pitch = 1, float Pan = 0, bool Loop = false, AudioBus Bus = AudioBus.Effects)
{
    /// <summary>Full volume, normal pitch, centered, played once on the effects bus.</summary>
    public SoundOptions() : this(Volume: 1)
    {
    }

    /// <summary>The options a clip plays with when none are given: its own loop setting and bus, full volume, normal pitch, centered.</summary>
    public static SoundOptions For(SoundClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return new SoundOptions(Loop: clip.Loop, Bus: clip.Bus);
    }
}

/// <summary>Identifies a playing sound so it can be changed or stopped.</summary>
public readonly record struct SoundHandle(int Id)
{
    public bool IsNone => Id == 0;
}

/// <summary>Plays sound effects and music.</summary>
/// <remarks>
/// Methods are thread-safe. When no audio device is available the engine uses a silent implementation, so games never need to check.
/// Every clip and track is also scaled by its own <see cref="SoundClip.Volume"/> or <see cref="MusicTrack.Volume"/> from its import
/// settings, and loops between its loop points when it has them.
/// </remarks>
public interface IAudioService : IDisposable
{
    /// <summary>The device in use, such as an output name, or "Silent".</summary>
    string DeviceName { get; }

    /// <summary>The overall volume from 0 to 1.</summary>
    float MasterVolume { get; set; }

    float GetBusVolume(AudioBus bus);

    void SetBusVolume(AudioBus bus, float volume);

    /// <summary>Plays a clip; without options it plays with <see cref="SoundOptions.For"/> the clip.</summary>
    SoundHandle Play(SoundClip clip, SoundOptions? options = null);

    void Stop(SoundHandle sound);

    bool IsPlaying(SoundHandle sound);

    /// <summary>Changes the volume of a playing sound, as given in <see cref="SoundOptions.Volume"/>.</summary>
    void SetVolume(SoundHandle sound, float volume);

    /// <summary>Changes the stereo position of a playing mono sound, from -1, left, to 1, right.</summary>
    void SetPan(SoundHandle sound, float pan);

    /// <summary>Changes the playback speed of a playing sound.</summary>
    void SetPitch(SoundHandle sound, float pitch);

    /// <summary>Plays music on the music bus, cross-fading from the current track over <paramref name="fade"/>.</summary>
    /// <param name="volume">A volume from 0 to 1 for this track, multiplied with the music bus volume.</param>
    void PlayMusic(MusicTrack track, bool loop = true, TimeSpan fade = default, float volume = 1);

    void StopMusic(TimeSpan fade = default);

    /// <summary>The track that is playing or fading in, or null.</summary>
    MusicTrack? CurrentMusic { get; }

    /// <summary>Pauses or resumes everything, for example while the game window is minimized.</summary>
    bool IsPaused { get; set; }

    /// <summary>The number of sounds currently playing, for diagnostics.</summary>
    int ActiveSounds { get; }

    /// <summary>Advances fades and recycles finished voices; the game loop calls it once per frame.</summary>
    void Update(float deltaSeconds);
}
