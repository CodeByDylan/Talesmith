using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Mathematics;

namespace Talesmith.Runtime.Maps;

/// <summary>Computes where tile artwork is drawn, matching how the Hexy editor places it.</summary>
public static class TileGeometry
{
    /// <summary>The transform of a map tile in a cell: placed by the tileset's <see cref="TilePlacement"/>, flipped, then rotated around the cell center.</summary>
    public static Matrix3x2 CellTile(TileMap map, Tileset tileset, Vector2 cellCenter, TileCell cell)
    {
        var size = new Vector2(tileset.TileWidth, tileset.TileHeight);
        var offset = tileset.Placement == TilePlacement.BottomCenter
            ? new Vector2(-size.X / 2, map.Layout.CellSize.Y / 2 - size.Y)
            : -size / 2;
        return Oriented(size, offset, cellCenter, cell, map.RotationStepDegrees);
    }

    /// <summary>The transform of an image of <paramref name="size"/> centered on <paramref name="center"/>, as tile objects are drawn.</summary>
    public static Matrix3x2 Centered(Vector2 size, Vector2 center, TileCell cell, float rotationStepDegrees) =>
        Oriented(size, -size / 2, center, cell, rotationStepDegrees);

    /// <summary>The transform of a cell-shaped quad covering a cell's bounding box, used for flat color tiles.</summary>
    public static Matrix3x2 CellFill(Vector2 cellSize, Vector2 cellCenter) =>
        Matrix3x2.CreateScale(cellSize) * Matrix3x2.CreateTranslation(cellCenter - cellSize / 2);

    private static Matrix3x2 Oriented(Vector2 size, Vector2 offset, Vector2 center, TileCell cell, float rotationStepDegrees)
    {
        var transform = Matrix3x2.CreateScale(size) * Matrix3x2.CreateTranslation(offset);
        if (cell.FlipX)
            transform *= Matrix3x2.CreateScale(-1, 1);
        if (cell.Rotation != 0)
            transform *= Matrix3x2.CreateRotation(MathHelper.ToRadians(cell.Rotation * rotationStepDegrees));
        return transform * Matrix3x2.CreateTranslation(center);
    }

    /// <summary>A distinct, stable color for color tiles that have none assigned, matching the editor.</summary>
    public static Color FallbackColor(int tileId)
    {
        var hue = tileId * 137.508f % 360f;
        return FromHsl(hue, 0.55f, 0.55f);
    }

    private static Color FromHsl(float hue, float saturation, float lightness)
    {
        var chroma = (1 - MathF.Abs(2 * lightness - 1)) * saturation;
        var x = chroma * (1 - MathF.Abs(hue / 60 % 2 - 1));
        var m = lightness - chroma / 2;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x)
        };
        return Color.FromVector4(new Vector4(r + m, g + m, b + m, 1));
    }
}
