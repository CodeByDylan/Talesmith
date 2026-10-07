using Talesmith.Imaging;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Textures;

/// <summary>The sprites and animations of a texture, and problems found while building them.</summary>
public sealed record SpriteSheet(IReadOnlyList<SpriteSlice> Sprites, IReadOnlyList<SpriteAnimationInfo> Animations, IReadOnlyList<string> Warnings);

/// <summary>Turns the sprite settings of a texture into named sprites and animations.</summary>
public static class SpriteSlicer
{
    /// <summary>Builds the sprites and animations of an image from its import settings.</summary>
    /// <param name="baseName">The name grid cells are prefixed with when the grid has no prefix, normally the file name.</param>
    /// <param name="scale">How much the image was scaled after the settings were authored, such as 0.5 when it was halved by MaxSize.</param>
    public static SpriteSheet Slice(ImageData image, TextureImportSettings settings, string baseName, float scale = 1)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        var warnings = new List<string>();
        var sprites = new List<SpriteSlice>();
        if (settings.SpriteMode == SpriteMode.Multiple)
        {
            if (settings.Grid is { } grid)
                sprites.AddRange(SliceGrid(image, grid, baseName, scale, warnings));
            foreach (var slice in settings.Slices)
                AddSlice(sprites, image, slice, scale, warnings);
        }

        var names = sprites.Select(sprite => sprite.Name).ToHashSet(StringComparer.Ordinal);
        var animations = new List<SpriteAnimationInfo>();
        foreach (var animation in settings.Animations)
        {
            if (string.IsNullOrWhiteSpace(animation.Name))
            {
                warnings.Add("An animation has no name, so it was left out.");
                continue;
            }

            var frames = animation.Frames.Where(names.Contains).ToArray();
            foreach (var missing in animation.Frames.Where(frame => !names.Contains(frame)).Distinct())
                warnings.Add($"Animation \"{animation.Name}\" uses sprite \"{missing}\", which does not exist.");
            if (frames.Length == 0)
            {
                warnings.Add($"Animation \"{animation.Name}\" has no frames, so it was left out.");
                continue;
            }

            animations.Add(animation with { Frames = frames, FramesPerSecond = animation.FramesPerSecond > 0 ? animation.FramesPerSecond : 12 });
        }

        return new SpriteSheet(sprites, animations, warnings);
    }

    private static IEnumerable<SpriteSlice> SliceGrid(ImageData image, SpriteGrid grid, string baseName, float scale, List<string> warnings)
    {
        if (grid.CellWidth <= 0 || grid.CellHeight <= 0)
        {
            warnings.Add($"The sprite grid cell size {grid.CellWidth}×{grid.CellHeight} is invalid.");
            yield break;
        }

        int Scaled(int value) => (int)MathF.Round(value * scale);
        var cellWidth = Math.Max(1, Scaled(grid.CellWidth));
        var cellHeight = Math.Max(1, Scaled(grid.CellHeight));
        var offsetX = Scaled(grid.OffsetX);
        var offsetY = Scaled(grid.OffsetY);
        var spacingX = Scaled(grid.SpacingX);
        var spacingY = Scaled(grid.SpacingY);
        var columns = grid.Columns > 0 ? grid.Columns : Math.Max(0, (image.Width - offsetX + spacingX) / (cellWidth + spacingX));
        var rows = grid.Rows > 0 ? grid.Rows : Math.Max(0, (image.Height - offsetY + spacingY) / (cellHeight + spacingY));
        var prefix = grid.NamePrefix ?? baseName + "_";
        var bounds = new PixelRect(0, 0, image.Width, image.Height);
        var index = 0;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var cell = new PixelRect(offsetX + column * (cellWidth + spacingX), offsetY + row * (cellHeight + spacingY), cellWidth, cellHeight);
                var name = prefix + index++.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!bounds.Contains(cell))
                {
                    warnings.Add($"Grid cell \"{name}\" extends past the texture, so it was left out.");
                    continue;
                }

                if (grid.SkipEmpty && PixelRegion.IsTransparent(image, cell))
                    continue;
                yield return new SpriteSlice(name, new Rect2(cell.X, cell.Y, cell.Width, cell.Height), grid.Pivot);
            }
        }
    }

    private static void AddSlice(List<SpriteSlice> sprites, ImageData image, SpriteSlice slice, float scale, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(slice.Name))
        {
            warnings.Add("A sprite slice has no name, so it was left out.");
            return;
        }

        var rect = new Rect2(slice.Rect.X * scale, slice.Rect.Y * scale, slice.Rect.Width * scale, slice.Rect.Height * scale);
        var clipped = Rect2.FromEdges(
            Math.Max(rect.Left, 0), Math.Max(rect.Top, 0), Math.Min(rect.Right, image.Width), Math.Min(rect.Bottom, image.Height));
        if (clipped.IsEmpty)
        {
            warnings.Add($"Sprite \"{slice.Name}\" lies outside the texture, so it was left out.");
            return;
        }

        if (clipped != rect)
            warnings.Add($"Sprite \"{slice.Name}\" extends past the texture and was cut to fit.");

        sprites.RemoveAll(sprite => sprite.Name == slice.Name);
        sprites.Add(slice with { Rect = clipped });
    }
}
