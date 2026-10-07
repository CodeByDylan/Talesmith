using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Rendering;

/// <summary>The outlines of the edited map's cells across the viewport, which the viewport shows as its grid while a tile tool is active.</summary>
/// <remarks>
/// Square cells are drawn as whole rows and columns of lines on whole pixels. Hex cells are drawn edge by edge, each shared edge once, in map
/// coordinates for an area somewhat larger than the view, so panning reuses the lines until the view leaves that area. The lines fade in from
/// <see cref="HiddenCellPixels"/>, below which cells are too small to show a grid, until <see cref="SolidCellPixels"/>.
/// </remarks>
public sealed class TileGrid(TileOverlay overlay)
{
    /// <summary>The cell size on screen below which no grid shows.</summary>
    public const double HiddenCellPixels = 10;

    /// <summary>The cell size on screen from which the grid shows at full strength.</summary>
    public const double SolidCellPixels = 28;

    private const double LineOpacity = 0.26;

    /// <summary>The share of the view's size added on each side of the area whose hex lines are built.</summary>
    private const double Margin = 0.25;

    private const int MaxBuiltCells = 80_000;

    private HexLines? _hex;

    /// <summary>Draws the cells of the edited map in lines of <paramref name="color"/>.</summary>
    public void Render(DrawingContext drawing, Color color)
    {
        if (overlay.Editor.Map is not { } map)
            return;
        var layout = map.Layout;
        var cellPixels = Math.Min(layout.CellSize.X, layout.CellSize.Y) * overlay.Camera.Zoom;
        var strength = Math.Clamp((cellPixels - HiddenCellPixels) / (SolidCellPixels - HiddenCellPixels), 0, 1);
        if (strength <= 0)
            return;
        var brush = new ImmutableSolidColorBrush(color, LineOpacity * strength);
        if (layout.Kind == GridKind.Square)
            DrawSquare(drawing, layout, new ImmutablePen(brush, 1));
        else
            DrawHex(drawing, layout, brush);
    }

    private void DrawSquare(DrawingContext drawing, IGridLayout layout, IPen pen)
    {
        var cells = overlay.VisibleCells;
        if (cells.IsEmpty)
            return;
        var size = overlay.Camera.ViewSize;
        var geometry = new StreamGeometry();
        using (var lines = geometry.Open())
        {
            for (var x = cells.MinX; x <= cells.MaxX + 1; x++)
            {
                var screen = Snap(overlay.ToScreen(new Vector2(layout.CellBounds(new GridCoord(x, 0)).X, 0)).X);
                if (screen < 0 || screen > size.Width)
                    continue;
                lines.BeginFigure(new Point(screen, 0), false);
                lines.LineTo(new Point(screen, size.Height));
                lines.EndFigure(false);
            }

            for (var y = cells.MinY; y <= cells.MaxY + 1; y++)
            {
                var screen = Snap(overlay.ToScreen(new Vector2(0, layout.CellBounds(new GridCoord(0, y)).Y)).Y);
                if (screen < 0 || screen > size.Height)
                    continue;
                lines.BeginFigure(new Point(0, screen), false);
                lines.LineTo(new Point(size.Width, screen));
                lines.EndFigure(false);
            }
        }

        drawing.DrawGeometry(null, pen, geometry);
    }

    private void DrawHex(DrawingContext drawing, IGridLayout layout, IImmutableBrush brush)
    {
        var visible = overlay.VisibleCells;
        if (visible.IsEmpty)
            return;
        if (_hex is not { } lines || !ReferenceEquals(lines.Layout, layout) || !Covers(lines.Cells, visible) || Area(lines.Cells) > 16 * Area(visible))
        {
            var cells = Grow(visible, Margin);
            if (Area(cells) > MaxBuiltCells)
                cells = visible;
            if (Area(cells) > MaxBuiltCells)
                return;
            _hex = lines = new HexLines(layout, cells, Build(layout, cells));
        }

        var origin = overlay.Editor.Origin;
        using (drawing.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y) * overlay.WorldToScreen))
            drawing.DrawGeometry(null, new ImmutablePen(brush, 1 / overlay.Camera.Zoom), lines.Geometry);
    }

    /// <summary>Each cell's outline toward its neighbors that come later in reading order, so every edge between two cells is drawn once.</summary>
    private static StreamGeometry Build(IGridLayout layout, GridBounds cells)
    {
        var runs = OwnEdgeRuns(layout);
        var geometry = new StreamGeometry();
        using var lines = geometry.Open();
        for (var y = cells.MinY; y <= cells.MaxY; y++)
        {
            for (var x = cells.MinX; x <= cells.MaxX; x++)
            {
                var center = layout.CellToWorld(new GridCoord(x, y));
                foreach (var (first, count) in runs)
                {
                    lines.BeginFigure(ToPoint(center + layout.CornerOffset(first)), false);
                    for (var i = 1; i <= count; i++)
                        lines.LineTo(ToPoint(center + layout.CornerOffset((first + i) % layout.CornerCount)));
                    lines.EndFigure(false);
                }
            }
        }

        return geometry;
    }

    /// <summary>The runs of consecutive edges, as their first corner and number of edges, that face a neighbor further down, or further right
    /// on the same row.</summary>
    private static List<(int First, int Count)> OwnEdgeRuns(IGridLayout layout)
    {
        var count = layout.CornerCount;
        var own = new bool[count];
        for (var e = 0; e < count; e++)
        {
            var neighbor = TileOverlay.EdgeNeighbor(layout, e);
            own[e] = neighbor.Y > 0 || (neighbor.Y == 0 && neighbor.X > 0);
        }

        var runs = new List<(int, int)>();
        var start = Array.IndexOf(own, false);
        for (var i = 1; i <= count; i++)
        {
            var e = (start + i) % count;
            if (!own[e])
                continue;
            if (runs.Count > 0 && runs[^1] is var (first, length) && (first + length) % count == e)
                runs[^1] = (first, length + 1);
            else
                runs.Add((e, 1));
        }

        return runs;
    }

    private static bool Covers(GridBounds outer, GridBounds inner) =>
        inner.MinX >= outer.MinX && inner.MaxX <= outer.MaxX && inner.MinY >= outer.MinY && inner.MaxY <= outer.MaxY;

    private static long Area(GridBounds cells) => (long)cells.Width * cells.Height;

    private static GridBounds Grow(GridBounds cells, double share)
    {
        var x = (int)Math.Ceiling(cells.Width * share);
        var y = (int)Math.Ceiling(cells.Height * share);
        return new GridBounds(cells.MinX - x, cells.MinY - y, cells.MaxX + x, cells.MaxY + y);
    }

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);

    private static double Snap(double value) => Math.Round(value) + 0.5;

    private sealed record HexLines(IGridLayout Layout, GridBounds Cells, StreamGeometry Geometry);
}
