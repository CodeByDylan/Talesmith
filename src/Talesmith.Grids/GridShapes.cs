using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Grids;

/// <summary>The footprint of a painting brush.</summary>
public enum BrushShape
{
    /// <summary>Every cell within <c>Size - 1</c> steps: a hexagon on hex grids and a square on square grids.</summary>
    Range,

    /// <summary>Every cell whose center lies within a circle of <c>Size - 0.5</c> cell steps, which looks round on any grid.</summary>
    Circle
}

/// <summary>A brush footprint: its shape and its size in cells, where 1 paints a single cell.</summary>
public readonly record struct GridBrush(BrushShape Shape, int Size)
{
    public const int MaxSize = 64;

    public static GridBrush Single => new(BrushShape.Range, 1);

    /// <summary>Adds the cells the brush covers when centered on <paramref name="center"/>.</summary>
    public void Footprint(IGridLayout layout, GridCoord center, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var size = Math.Clamp(Size, 1, MaxSize);
        if (Shape == BrushShape.Circle && size > 1)
            GridShapes.Circle(layout, center, (size - 0.5f) * GridShapes.StepLength(layout), output);
        else
            layout.Topology.Range(center, size - 1, output);
    }

    /// <summary>Adds the cells covered by dragging the brush in a straight line, as painting does between two pointer samples.</summary>
    public void Stroke(IGridLayout layout, GridCoord from, GridCoord to, CellSet output)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(output);
        var line = new List<GridCoord>(layout.Topology.Distance(from, to) + 1);
        layout.Topology.Line(from, to, line);
        foreach (var cell in line)
            Footprint(layout, cell, output);
    }
}

/// <summary>Builds the cell sets of shape tools: rectangles, circles, hexagons, polygons and outlines, on any grid.</summary>
/// <remarks>Methods add to the output without clearing it, so callers can reuse buffers and combine shapes.</remarks>
public static class GridShapes
{
    /// <summary>The distance between the centers of the closest neighbors, in world units.</summary>
    public static float StepLength(IGridLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var step = float.MaxValue;
        foreach (var direction in layout.Topology.Directions)
            step = MathF.Min(step, layout.CellToWorld(direction).Length());
        return step;
    }

    /// <summary>Adds a screen-aligned rectangle spanned by two corner cells; hex grids use offset rows or columns so it looks rectangular.</summary>
    public static void Rectangle(GridTopology topology, GridCoord corner, GridCoord oppositeCorner, bool filled, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(output);
        var a = topology.ToOffset(corner);
        var b = topology.ToOffset(oppositeCorner);
        var (minX, maxX) = (Math.Min(a.X, b.X), Math.Max(a.X, b.X));
        var (minY, maxY) = (Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (filled || y == minY || y == maxY || x == minX || x == maxX)
                    output.Add(topology.FromOffset(new GridCoord(x, y)));
            }
        }
    }

    /// <summary>Adds every cell whose center lies within <paramref name="radius"/> world units of the center of <paramref name="center"/>.</summary>
    public static void Circle(IGridLayout layout, GridCoord center, float radius, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(output);
        var topology = layout.Topology;
        var reach = (int)MathF.Ceiling(radius / StepLength(layout)) + 1;
        var origin = layout.CellToWorld(center);
        var radiusSquared = radius * radius;
        for (var dx = -reach; dx <= reach; dx++)
        {
            var minY = topology.IsHex ? Math.Max(-reach, -dx - reach) : -reach;
            var maxY = topology.IsHex ? Math.Min(reach, -dx + reach) : reach;
            for (var dy = minY; dy <= maxY; dy++)
            {
                var cell = new GridCoord(center.X + dx, center.Y + dy);
                if (Vector2.DistanceSquared(layout.CellToWorld(cell), origin) <= radiusSquared)
                    output.Add(cell);
            }
        }
    }

    /// <summary>Adds the circle a circle tool draws when dragged from <paramref name="center"/> to <paramref name="edge"/>, filled or as its outline.</summary>
    public static void Circle(IGridLayout layout, GridCoord center, GridCoord edge, bool filled, CellSet output)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(output);
        var radius = Vector2.Distance(layout.CellToWorld(edge), layout.CellToWorld(center)) + MathF.Min(layout.CellSize.X, layout.CellSize.Y) * 0.25f;
        if (filled)
        {
            Circle(layout, center, radius, output);
            return;
        }

        var disc = new CellSet();
        Circle(layout, center, radius, disc);
        Outline(layout.Topology, disc, output);
    }

    /// <summary>Adds a hexagon of cells within <paramref name="radius"/> steps of <paramref name="center"/>, filled or as its ring.</summary>
    public static void Hexagon(GridTopology topology, GridCoord center, int radius, bool filled, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (filled)
            topology.Range(center, radius, output);
        else
            topology.Ring(center, radius, output);
    }

    /// <summary>Adds the cells of <paramref name="shape"/> that have an edge neighbor outside it.</summary>
    public static void Outline(GridTopology topology, CellSet shape, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(output);
        var directions = topology.Directions;
        foreach (var cell in shape)
        {
            foreach (var direction in directions)
            {
                if (!shape.Contains(cell + direction))
                {
                    output.Add(cell);
                    break;
                }
            }
        }
    }

    /// <summary>Adds every cell whose center lies inside a polygon in world coordinates, as a lasso selects.</summary>
    public static void Polygon(IGridLayout layout, ReadOnlySpan<Vector2> polygon, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(output);
        if (polygon.Length < 3)
            return;

        var bounds = layout.CoveringBounds(Rect2.Bounding(polygon));
        for (var y = bounds.MinY; y <= bounds.MaxY; y++)
        {
            for (var x = bounds.MinX; x <= bounds.MaxX; x++)
            {
                var cell = new GridCoord(x, y);
                if (Contains(polygon, layout.CellToWorld(cell)))
                    output.Add(cell);
            }
        }
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
