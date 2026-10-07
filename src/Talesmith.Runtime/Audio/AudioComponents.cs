using Talesmith.Audio;
using Talesmith.Authoring;
using Talesmith.Ecs;

namespace Talesmith.Runtime.Audio;

/// <summary>Plays a sound clip or a music track from an entity, optionally positioned in the world relative to the listener.</summary>
/// <remarks>
/// <see cref="AudioSourceSystem"/> plays it when the entity appears in a running game if <see cref="PlayOnStart"/> is set, or when a
/// <see cref="PlayAudioSource"/> event names the entity, and stops it when the entity or the component goes away or the scene ends.
/// Music plays on the music channel, replacing the current track, and is never spatial.
/// </remarks>
[Component("Audio Source", Category = "Audio", Icon = "volume-2", Description = "Plays a sound or music, optionally positioned in the world.")]
public struct AudioSource
{
    public AudioSource()
    {
    }

    /// <summary>The sound effect to play; ignored when <see cref="Music"/> is set.</summary>
    [AssetFilter(".wav", ".ogg")]
    public SoundClip? Clip;

    [AssetFilter(".wav", ".ogg")]
    [Tooltip("A streamed music track; plays on the music channel instead of the clip.")]
    public MusicTrack? Music;

    [Range(0, 1, Step = 0.01)]
    public float Volume = 1;

    [Range(0.1, 3, Step = 0.01)]
    [Tooltip("Playback speed; 1 is normal, 2 is an octave higher. Music ignores it.")]
    public float Pitch = 1;

    [Tooltip("Loops the sound; sounds whose import settings loop also loop without it.")]
    public bool Loop;

    [Label("Play On Start")]
    public bool PlayOnStart = true;

    [Tooltip("The mixer bus; empty uses the bus from the sound's import settings.")]
    public AudioBus? Bus;

    [Header("Spatial")]
    [Tooltip("Fades the sound with distance from the listener and pans it left or right.")]
    public bool Spatial;

    [Range(0)]
    [Tooltip("Within this distance in world units the sound plays at full volume.")]
    public float MinDistance = 128;

    [Range(0)]
    [Tooltip("Beyond this distance in world units the sound is silent.")]
    public float MaxDistance = 1024;
}

/// <summary>Marks the entity whose <see cref="Components.Transform"/> spatial sounds are heard from; without one, the active camera is used.</summary>
[Component("Audio Listener", Category = "Audio", Icon = "headphones", Description = "Hears spatial sounds from this entity's position.")]
public struct AudioListener;

/// <summary>Starts the <see cref="AudioSource"/> of an entity, or restarts it when it is playing.</summary>
public readonly record struct PlayAudioSource(Entity Entity);

/// <summary>Stops the <see cref="AudioSource"/> of an entity.</summary>
public readonly record struct StopAudioSource(Entity Entity);
