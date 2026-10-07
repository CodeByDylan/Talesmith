using System.Numerics;
using Talesmith.Assets;
using Talesmith.Audio;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;

namespace Talesmith.Samples.IsleHopper;

/// <summary>One run through the level, shared by the gameplay systems of a scene: the level, the hero and the score.</summary>
/// <remarks>Registered per scene, so reloading the scene starts a fresh run.</remarks>
public sealed class Course(IAssetManager assets, IAudioService audio, IsleHud hud)
{
    public const float RespawnInvulnerability = 1.2f;

    private readonly Dictionary<string, SoundClip> _sounds = new(StringComparer.Ordinal);

    /// <summary>The playable map, once the hero has been placed on it.</summary>
    public Level? Level { get; set; }

    public Entity Hero { get; set; }

    /// <summary>The entity the camera follows, kept above and ahead of the hero.</summary>
    public Entity CameraFocus { get; set; }

    public int Gems { get; private set; }

    public int GemsTotal { get; set; }

    /// <summary>Whether the hero reached the lighthouse; the hero stops listening to input.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Game seconds since the run started.</summary>
    public double Elapsed { get; set; }

    public bool IsRunning => Level is not null && !IsComplete;

    public void CollectGem()
    {
        Gems++;
        hud.Gems = Gems;
        Play("gem", 0.6f);
    }

    public void Complete()
    {
        if (IsComplete)
            return;
        IsComplete = true;
        hud.IsComplete = true;
        Play("goal", 0.7f);
    }

    /// <summary>Sends the hero back to the last checkpoint, briefly unable to be hurt again.</summary>
    public void Hurt(ref Hero hero, ref Transform transform)
    {
        if (hero.Invulnerable > 0)
            return;
        Play("hurt", 0.55f);
        transform.Position = hero.Respawn;
        hero.Velocity = Vector2.Zero;
        hero.Invulnerable = RespawnInvulnerability;
        hud.Falls++;
    }

    /// <summary>Plays <c>audio/{name}.wav</c>.</summary>
    public void Play(string name, float volume = 1, float pitch = 1)
    {
        if (!_sounds.TryGetValue(name, out var clip))
            _sounds[name] = clip = assets.Load<SoundClip>($"audio/{name}.wav");
        audio.Play(clip, new SoundOptions(Volume: volume, Pitch: pitch));
    }
}
