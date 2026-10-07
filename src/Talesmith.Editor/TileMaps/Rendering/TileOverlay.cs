using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Assets.Maps;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Color = Avalonia.Media.Color;

namespace Talesmith.Editor.TileMaps.Rendering;

/// <summary>Draws tile tool feedback over the viewport in screen coordinates: cell regions with their outline, ghost tiles with the real artwork,
/// and outlines of single cells, limited to the visible part of the map so huge selections and brushes stay fast.</summary>
public sealed class TileOverlay(TileMapEditor editor, ViewportCamera camera)
{
    /// <summary>The most cells drawn one by one; larger regions show their bounds.</summary>
    public const int MaxDrawnCells = 40_000;

    /// <summary>The most ghost tiles drawn with artwork; larger previews show cell regions only.</summary>
    public const int MaxGhostTiles = 2_500;

    private static readonly ConditionalWeakTable<IGridLayout, GridCoord[]> EdgeNeighbors = [];
    private static (Color Accent, Colors Value)? _colors;
    private readonly List<GridCoord> _visible = [];
    private TileGrid? _grid;

    public TileMapEditor Editor { get; } = editor;

    public ViewportCamera Camera { get; } = camera;

    /// <summary>The edited map's cell outlines, which the viewport shows as its grid while a tile tool is active.</summary>
    public TileGrid Grid => _grid ??= new TileGrid(this);

    /// <summary>The colors of tile feedback for the current accent color.</summary>
    public static Colors Palette
    {
        get
        {
            var accent = SelectTool.AccentColor();
            if (_colors is { } cached && cached.Accent == accent)
                return cached.Value;
            var colors = new Colors(accent);
            _colors = (accent, colors);
            return colors;
        }
    }

    /// <summary>The transform from world coordinates to the viewport's.</summary>
    public Matrix WorldToScreen
    {
        get
        {
            var zoom = Camera.Zoom;
            var size = Camera.ViewSize;
            return new Matrix(zoom, 0, 0, zoom, size.Width / 2 - Camera.Position.X * zoom, size.Height / 2 - Camera.Position.Y * zoom);
        }
    }

    /// <summary>The cells of the edited map that may be visible.</summary>
    public GridBounds VisibleCells
    {
        get
        {
            if (Editor.Map is not { } map)
                return GridBounds.Empty;
            var visible = Camera.VisibleWorld;
            var origin = Editor.Origin;
            var margin = Math.Max(map.Layout.CellSize.X, map.Layout.CellSize.Y);
            return map.Layout.CoveringBounds(new Rect2(visible.X - origin.X - margin, visible.Y - origin.Y - margin, visible.Width + margin * 2,
                visible.Height + margin * 2));
        }
    }

    /// <summary>A cell's outline in viewport coordinates.</summary>
    public StreamGeometry CellGeometry(GridCoord cell)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
            AddCell(context, cell, true);
        return geometry;
    }

    /// <summary>Draws one cell's outline.</summary>
    public void DrawCell(DrawingContext drawing, GridCoord cell, IBrush? fill, IPen? pen)
    {
        if (Editor.Map is not null)
            drawing.DrawGeometry(fill, pen, CellGeometry(cell));
    }

    /// <summary>Draws a region of cells as a fill and the outline of its border, culled to the view.</summary>
    public void DrawCells(DrawingContext drawing, CellSet cells, IBrush? fill, IPen? pen)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (Editor.Map is not { } map || cells.IsEmpty || !CollectVisible(cells))
        {
            DrawBounds(drawing, cells, pen);
            return;
        }

        var neighbors = EdgeNeighbors.GetValue(map.Layout, ComputeEdgeNeighbors);
        var cellPixels = Math.Min(map.Layout.CellSize.X, map.Layout.CellSize.Y) * Camera.Zoom;
        var fillGeometry = fill is null ? null : new StreamGeometry();
        var outline = pen is null ? null : new StreamGeometry();
        using (var fills = fillGeometry?.Open())
        using (var edges = outline?.Open())
        {
            foreach (var cell in _visible)
            {
                if (fills is not null)
                    AddCell(fills, cell, true);
                if (edges is null || cellPixels < 2)
                    continue;
                for (var e = 0; e < neighbors.Length; e++)
                {
                    if (!cells.Contains(cell + neighbors[e]))
                        AddEdge(edges, cell, e);
                }
            }
        }

        if (fillGeometry is not null)
            drawing.DrawGeometry(fill, null, fillGeometry);
        if (outline is not null && cellPixels >= 2)
            drawing.DrawGeometry(null, pen, outline);
    }

    /// <summary>Draws tiles translucently in their cells with their real artwork, as a preview of what a tool will paint.</summary>
    /// <returns>False when there were too many to draw.</returns>
    public bool DrawGhosts(DrawingContext drawing, IReadOnlyCollection<PlacedTile> tiles, double opacity = 0.62)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (Editor.Map is not { } map || tiles.Count > MaxGhostTiles)
            return false;
        var visible = VisibleCells;
        var transform = WorldToScreen;
        var origin = Editor.Origin;
        using (drawing.PushOpacity(opacity))
        {
            foreach (var placed in tiles)
            {
                if (placed.Tile.IsEmpty || !visible.Contains(placed.Cell))
                    continue;
                var center = origin + map.Layout.CellToWorld(placed.Cell);
                TileArt.DrawCellTile(drawing, transform, map, center, placed.Tile, CellGeometry(placed.Cell));
            }
        }

        return true;
    }

    /// <summary>The viewport position of a point in map coordinates.</summary>
    public Point ToScreen(Vector2 mapPosition) => Camera.WorldToScreen(Editor.Origin + mapPosition);

    private bool CollectVisible(CellSet cells)
    {
        _visible.Clear();
        var bounds = VisibleCells;
        var area = (long)bounds.Width * bounds.Height;
        if (cells.Count <= area)
        {
            foreach (var cell in cells)
            {
                if (!bounds.Contains(cell))
                    continue;
                if (_visible.Count >= MaxDrawnCells)
                    return false;
                _visible.Add(cell);
            }

            return true;
        }

        var occupied = cells.Bounds;
        var region = new GridBounds(Math.Max(bounds.MinX, occupied.MinX), Math.Max(bounds.MinY, occupied.MinY), Math.Min(bounds.MaxX, occupied.MaxX),
            Math.Min(bounds.MaxY, occupied.MaxY));
        if ((long)region.Width * region.Height > MaxDrawnCells * 8L)
            return false;
        for (var y = region.MinY; y <= region.MaxY; y++)
        {
            for (var x = region.MinX; x <= region.MaxX; x++)
            {
                var cell = new GridCoord(x, y);
                if (!cells.Contains(cell))
                    continue;
                if (_visible.Count >= MaxDrawnCells)
                    return false;
                _visible.Add(cell);
            }
        }

        return true;
    }

    private void DrawBounds(DrawingContext drawing, CellSet cells, IPen? pen)
    {
        if (Editor.Map is not { } map || cells.IsEmpty || pen is null)
            return;
        var bounds = cells.Bounds;
        var world = map.Layout.CellBounds(new GridCoord(bounds.MinX, bounds.MinY));
        foreach (var corner in (ReadOnlySpan<GridCoord>)[new(bounds.MaxX, bounds.MinY), new(bounds.MinX, bounds.MaxY), new(bounds.MaxX, bounds.MaxY)])
            world = world.Union(map.Layout.CellBounds(corner));
        var topLeft = ToScreen(new Vector2(world.X, world.Y));
        drawing.DrawRectangle(null, pen, new Rect(topLeft, new Size(world.Width * Camera.Zoom, world.Height * Camera.Zoom)));
    }

    private void AddCell(StreamGeometryContext context, GridCoord cell, bool closed)
    {
        var layout = Editor.Map!.Layout;
        var center = layout.CellToWorld(cell);
        var origin = Editor.Origin;
        for (var i = 0; i < layout.CornerCount; i++)
        {
            var point = Camera.WorldToScreen(origin + center + layout.CornerOffset(i));
            if (i == 0)
                context.BeginFigure(point, closed);
            else
                context.LineTo(point);
        }

        context.EndFigure(closed);
    }

    private void AddEdge(StreamGeometryContext context, GridCoord cell, int edge)
    {
        var layout = Editor.Map!.Layout;
        var center = Editor.Origin + layout.CellToWorld(cell);
        context.BeginFigure(Camera.WorldToScreen(center + layout.CornerOffset(edge)), false);
        context.LineTo(Camera.WorldToScreen(center + layout.CornerOffset((edge + 1) % layout.CornerCount)));
        context.EndFigure(false);
    }

    /// <summary>The offset of the neighbor across a cell's edge, where edge <c>i</c> runs from corner <c>i</c> to the next.</summary>
    internal static GridCoord EdgeNeighbor(IGridLayout layout, int edge) => EdgeNeighbors.GetValue(layout, ComputeEdgeNeighbors)[edge];

    /// <summary>The neighbor across each edge of a cell, where edge <c>i</c> runs from corner <c>i</c> to the next.</summary>
    private static GridCoord[] ComputeEdgeNeighbors(IGridLayout layout)
    {
        var neighbors = new GridCoord[layout.CornerCount];
        for (var e = 0; e < neighbors.Length; e++)
        {
            var middle = (layout.CornerOffset(e) + layout.CornerOffset((e + 1) % neighbors.Length)) / 2;
            neighbors[e] = layout.WorldToCell(middle * 2);
        }

        return neighbors;
    }

    /// <summary>The brushes and pens of tile feedback.</summary>
    public sealed class Colors
    {
        public Colors(Color accent)
        {
            var accentBrush = new ImmutableSolidColorBrush(accent);
            var erase = Color.Parse("#EF4444");
            Accent = accentBrush;
            AccentFill = new ImmutableSolidColorBrush(accent, 0.16);
            AccentPen = new ImmutablePen(accentBrush, 1.5, lineJoin: PenLineJoin.Round);
            HoverPen = new ImmutablePen(new ImmutableSolidColorBrush(accent, 0.9), 1.25, lineJoin: PenLineJoin.Round);
            SelectionFill = new ImmutableSolidColorBrush(accent, 0.12);
            SelectionPen = new ImmutablePen(new ImmutableSolidColorBrush(global::Avalonia.Media.Colors.White, 0.95), 1.25, new ImmutableDashStyle([4, 3], 0), lineJoin: PenLineJoin.Round);
            SelectionShadowPen = new ImmutablePen(accentBrush, 2.5, lineJoin: PenLineJoin.Round);
            EraseFill = new ImmutableSolidColorBrush(erase, 0.22);
            ErasePen = new ImmutablePen(new ImmutableSolidColorBrush(erase), 1.5, lineJoin: PenLineJoin.Round);
            CollisionFill = new ImmutableSolidColorBrush(erase, 0.28);
            CollisionPen = new ImmutablePen(new ImmutableSolidColorBrush(erase, 0.85), 1, lineJoin: PenLineJoin.Round);
            ObjectPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.Parse("#F59E0B")), 1.5, lineJoin: PenLineJoin.Round);
            White = new ImmutableSolidColorBrush(global::Avalonia.Media.Colors.White);
        }

        public IImmutableSolidColorBrush Accent { get; }

        public IImmutableSolidColorBrush AccentFill { get; }

        public IPen AccentPen { get; }

        public IPen HoverPen { get; }

        public IImmutableSolidColorBrush SelectionFill { get; }

        public IPen SelectionPen { get; }

        public IPen SelectionShadowPen { get; }

        public IImmutableSolidColorBrush EraseFill { get; }

        public IPen ErasePen { get; }

        public IImmutableSolidColorBrush CollisionFill { get; }

        public IPen CollisionPen { get; }

        public IPen ObjectPen { get; }

        public IImmutableSolidColorBrush White { get; }
    }
}
