using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;

namespace Talesmith.Samples.HexQuest;

/// <summary>The hero's idle and walk cycles, cut from the eight-frame sheet in sprites/hero.png.</summary>
public sealed class HeroAnimations(Texture sheet)
{
    public const string SheetPath = "sprites/hero.png";
    public const int FrameWidth = 64;
    public const int FrameHeight = 80;

    public Texture Sheet { get; } = sheet;

    public AnimationClip Idle { get; } = Clip(sheet, "idle", 0, 0.3f);

    public AnimationClip Walk { get; } = Clip(sheet, "walk", 4, 0.1f);

    private static AnimationClip Clip(Texture sheet, string name, int firstFrame, float duration) =>
        new(name, Enumerable.Range(firstFrame, 4).Select(i => new AnimationFrame(sheet, new Rect2(i * FrameWidth, 0, FrameWidth, FrameHeight), duration)).ToList());
}
