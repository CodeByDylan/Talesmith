using Talesmith.Audio;

namespace Talesmith.Scripting;

/// <summary>Plays sounds and music for scripts; see <see cref="Script.Audio"/>.</summary>
/// <remarks>Sounds and tracks given by path load on first use and stay cached, so play them by path freely.</remarks>
public readonly struct ScriptAudio
{
    private readonly ScriptRuntime _runtime;

    internal ScriptAudio(ScriptRuntime runtime) => _runtime = runtime;

    /// <summary>The audio service, for buses, master volume and changing playing sounds.</summary>
    public IAudioService Service => _runtime.Audio;

    /// <summary>Plays a sound effect by asset path, such as "audio/coin.wav".</summary>
    public SoundHandle Play(string path, SoundOptions? options = null) => Service.Play(_runtime.Assets.Load<SoundClip>(path), options);

    public SoundHandle Play(SoundClip clip, SoundOptions? options = null) => Service.Play(clip, options);

    public void Stop(SoundHandle sound) => Service.Stop(sound);

    public bool IsPlaying(SoundHandle sound) => Service.IsPlaying(sound);

    /// <summary>Plays music by asset path, such as "audio/theme.ogg", cross-fading from the current track over <paramref name="fade"/>.</summary>
    public void PlayMusic(string path, bool loop = true, TimeSpan fade = default, float volume = 1) =>
        Service.PlayMusic(_runtime.Assets.Load<MusicTrack>(path), loop, fade, volume);

    public void PlayMusic(MusicTrack track, bool loop = true, TimeSpan fade = default, float volume = 1) => Service.PlayMusic(track, loop, fade, volume);

    public void StopMusic(TimeSpan fade = default) => Service.StopMusic(fade);
}
