using System.Numerics;
using Talesmith.Grids;
using Talesmith.Imaging;

namespace Talesmith.Runtime.Maps;

/// <summary>Creates a white image in the shape of a grid cell, used to draw color tiles and tiles whose artwork is missing.</summary>
public static class CellMask
{
    private const int Supersampling = 4;

    /// <summary>Rasterizes the cell outline of <paramref name="layout"/> with anti-aliased edges, filling the image's bounding box.</summary>
    public static ImageData Create(IGridLayout layout)
    {
        var width = Math.Max(2, (int)MathF.Ceiling(layout.CellSize.X));
        var height = Math.Max(2, (int)MathF.Ceiling(layout.CellSize.Y));
        Span<Vector2> corners = stackalloc Vector2[layout.CornerCount];
        var scale = new Vector2(width / layout.CellSize.X, height / layout.CellSize.Y);
        var center = new Vector2(width / 2f, height / 2f);
        for (var i = 0; i < corners.Length; i++)
            corners[i] = center + layout.CornerOffset(i) * scale;

        var pixels = new byte[width * height * 4];
        const float step = 1f / Supersampling;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var covered = 0;
                for (var sy = 0; sy < Supersampling; sy++)
                {
                    for (var sx = 0; sx < Supersampling; sx++)
                    {
                        if (Contains(corners, new Vector2(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step)))
                            covered++;
                    }
                }

                var alpha = (byte)(covered * 255 / (Supersampling * Supersampling));
                var i = (y * width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = alpha;
            }
        }

        return new ImageData(width, height, pixels);
    }

    private static bool Contains(ReadOnlySpan<Vector2> polygon, Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}
