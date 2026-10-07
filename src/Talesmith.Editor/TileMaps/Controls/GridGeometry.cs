using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>Fits grid cells into small previews drawn with Avalonia, such as the new map preview and the neighbor pattern editor.</summary>
public static class GridGeometry
{
    /// <summary>A layout of the grid's kind and proportions; flat sizes fall back to a minimum.</summary>
    public static IGridLayout Layout(GridKind kind, double width, double height)
    {
        var w = (float)Math.Max(1, width);
        var h = (float)Math.Max(1, height);
        return kind == GridKind.Square ? new SquareLayout(w, h) : new HexLayout(kind == GridKind.HexPointyTop, w, h);
    }

    /// <summary>The scale and the control position of the world origin that fit <paramref name="cells"/> into <paramref name="fill"/> of
    /// <paramref name="size"/>, centered.</summary>
    public static (double Scale, Point Origin) Fit(IGridLayout layout, IEnumerable<GridCoord> cells, Size size, double fill)
    {
        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;
        foreach (var cell in cells)
        {
            var bounds = layout.CellBounds(cell);
            minX = Math.Min(minX, bounds.Left);
            minY = Math.Min(minY, bounds.Top);
            maxX = Math.Max(maxX, bounds.Right);
            maxY = Math.Max(maxY, bounds.Bottom);
        }

        var scale = Math.Max(0.001, Math.Min(size.Width / (maxX - minX), size.Height / (maxY - minY)) * fill);
        return (scale, new Point(size.Width / 2 - (minX + maxX) / 2 * scale, size.Height / 2 - (minY + maxY) / 2 * scale));
    }

    /// <summary>A cell's outline, scaled about its center by <paramref name="inset"/>.</summary>
    public static StreamGeometry Cell(IGridLayout layout, GridCoord cell, double scale, Point origin, double inset = 1)
    {
        var center = layout.CellToWorld(cell);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        for (var i = 0; i < layout.CornerCount; i++)
        {
            var corner = layout.CornerOffset(i) * (float)inset;
            var point = ToPoint(center + corner, scale, origin);
            if (i == 0)
                context.BeginFigure(point, true);
            else
                context.LineTo(point);
        }

        context.EndFigure(true);
        return geometry;
    }

    public static Point ToPoint(Vector2 world, double scale, Point origin) => new(origin.X + world.X * scale, origin.Y + world.Y * scale);
}
