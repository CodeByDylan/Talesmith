using System.Globalization;
using System.Numerics;
using Talesmith.Assets.Textures;
using Talesmith.Imaging;
using Talesmith.Mathematics;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>A sprite as the sprite editor edits it: a name, a rectangle of whole pixels and a pivot from (0, 0) top-left to (1, 1) bottom-right.</summary>
public sealed record SliceState(string Name, PixelRect Rect, Vector2 Pivot);

/// <summary>An animation as the sprite editor edits it.</summary>
public sealed record AnimationState(string Name, IReadOnlyList<string> Frames, float FramesPerSecond = 12, bool Loop = true)
{
    public bool Equals(AnimationState? other) =>
        other is not null && Name == other.Name && Frames.SequenceEqual(other.Frames) && FramesPerSecond.Equals(other.FramesPerSecond) && Loop == other.Loop;

    public override int GetHashCode() => HashCode.Combine(Name, Frames.Count, FramesPerSecond, Loop);
}

/// <summary>The sprites and animations of a texture at one moment, which the sprite editor's undo steps swap between.</summary>
public sealed record SpriteSheetState(IReadOnlyList<SliceState> Slices, IReadOnlyList<AnimationState> Animations)
{
    public static SpriteSheetState Empty { get; } = new([], []);

    public bool Equals(SpriteSheetState? other) =>
        other is not null && Slices.SequenceEqual(other.Slices) && Animations.SequenceEqual(other.Animations);

    public override int GetHashCode() => HashCode.Combine(Slices.Count, Animations.Count);

    /// <summary>The sprites and animations a texture's settings produce, with grid cells turned into slices.</summary>
    public static SpriteSheetState From(ImageData image, TextureImportSettings settings, string baseName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SpriteMode != SpriteMode.Multiple)
            return new SpriteSheetState([], [.. settings.Animations.Select(ToState)]);
        var sheet = SpriteSlicer.Slice(image, settings, baseName);
        var slices = sheet.Sprites.Select(s => new SliceState(s.Name, new PixelRect((int)s.Rect.X, (int)s.Rect.Y, (int)MathF.Round(s.Rect.Width), (int)MathF.Round(s.Rect.Height)), s.Pivot));
        return new SpriteSheetState([.. slices], [.. settings.Animations.Select(ToState)]);
    }

    /// <summary>Settings with these sprites as slices and these animations, keeping the other settings; sprites mean multiple sprite mode.</summary>
    public TextureImportSettings ApplyTo(TextureImportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with
        {
            SpriteMode = Slices.Count > 0 ? SpriteMode.Multiple : settings.SpriteMode,
            Grid = null,
            Slices = [.. Slices.Select(s => new SpriteSlice(s.Name, new Rect2(s.Rect.X, s.Rect.Y, s.Rect.Width, s.Rect.Height), s.Pivot))],
            Animations = [.. Animations.Select(a => new SpriteAnimationInfo(a.Name, [.. a.Frames], a.FramesPerSecond, a.Loop))]
        };
    }

    /// <summary>A name not used by any slice: <paramref name="prefix"/> followed by the lowest free number.</summary>
    public string NextSliceName(string prefix)
    {
        var used = Slices.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; ; i++)
        {
            var name = prefix + i.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(name))
                return name;
        }
    }

    /// <summary>Slices for rectangles, numbered from 0 after a prefix, all with one pivot.</summary>
    public static IReadOnlyList<SliceState> Numbered(IEnumerable<PixelRect> rects, string prefix, Vector2 pivot) =>
        [.. rects.Select((r, i) => new SliceState(prefix + i.ToString(CultureInfo.InvariantCulture), r, pivot))];

    private static AnimationState ToState(SpriteAnimationInfo animation) => new(animation.Name, [.. animation.Frames], animation.FramesPerSecond, animation.Loop);
}
