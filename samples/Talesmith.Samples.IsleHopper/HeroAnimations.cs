using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;

namespace Talesmith.Samples.IsleHopper;

/// <summary>The hero's animations, cut from the ten-frame sheet in sprites/hero.png: idle, run, jump and fall, all facing right.</summary>
public sealed class HeroAnimations(Texture sheet)
{
    public const string SheetPath = "sprites/hero.png";
    public const int FrameWidth = 64;
    public const int FrameHeight = 80;

    /// <summary>Where the feet are, as a fraction of the frame height.</summary>
    public const float FeetY = 0.95f;

    public Texture Sheet { get; } = sheet;

    public AnimationClip Idle { get; } = Clip(sheet, "idle", 0, 2, 0.45f);

    public AnimationClip Run { get; } = Clip(sheet, "run", 2, 6, 0.075f);

    public AnimationClip Jump { get; } = Clip(sheet, "jump", 8, 1, 1);

    public AnimationClip Fall { get; } = Clip(sheet, "fall", 9, 1, 1);

    private static AnimationClip Clip(Texture sheet, string name, int firstFrame, int count, float duration) =>
        new(name, Enumerable.Range(firstFrame, count).Select(i => new AnimationFrame(sheet, new Rect2(i * FrameWidth, 0, FrameWidth, FrameHeight), duration)).ToList());
}
