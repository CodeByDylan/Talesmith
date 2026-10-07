using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Runtime.Components;

/// <summary>One frame of an <see cref="AnimationClip"/>.</summary>
public readonly record struct AnimationFrame(Texture Texture, Rect2 Source, float Duration);

/// <summary>A sequence of sprite frames, such as a walk cycle.</summary>
public sealed class AnimationClip(string name, IReadOnlyList<AnimationFrame> frames, bool loop = true)
{
    public string Name { get; } = name;

    public IReadOnlyList<AnimationFrame> Frames { get; } = frames.Count > 0 ? frames : throw new ArgumentException("A clip needs at least one frame.", nameof(frames));

    public bool Loop { get; } = loop;

    public float Length { get; } = frames.Sum(f => f.Duration);
}

/// <summary>Plays an <see cref="AnimationClip"/> on the entity's <see cref="Sprite"/>.</summary>
public struct SpriteAnimation(AnimationClip clip)
{
    public AnimationClip Clip = clip;

    /// <summary>Seconds into the clip.</summary>
    public float Time;

    /// <summary>Playback speed; 1 is normal.</summary>
    public float Speed = 1;

    public bool Playing = true;

    /// <summary>Switches to another clip from its start, unless it is already playing.</summary>
    public void Play(AnimationClip clip)
    {
        if (ReferenceEquals(Clip, clip) && Playing)
            return;
        Clip = clip;
        Time = 0;
        Playing = true;
    }
}
