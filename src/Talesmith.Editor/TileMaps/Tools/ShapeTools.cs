using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;
using TileBrush = Talesmith.Assets.Maps.Editing.TileBrush;

namespace Talesmith.Editor.TileMaps.Tools;

/// <summary>Base of tools that drag out a shape and paint it on release, with a live preview of the tiles; right-drag erases the shape.</summary>
public abstract class ShapeTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    private readonly CellSet _cells = new();
    private readonly List<PlacedTile> _ghosts = [];
    private GridCoord? _start;
    private GridCoord _end;
    private bool _erasing;
    private bool _constrain;

    public override bool IsOperationInProgress => _start is not null;

    /// <summary>Adds the cells of the shape dragged from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <param name="constrain">Whether Shift is held, which keeps proportions.</param>
    protected abstract void BuildShape(IGridLayout layout, GridCoord start, GridCoord end, bool constrain, CellSet output);

    public override void Cancel(ViewportToolContext context)
    {
        _start = null;
        _cells.Clear();
    }

    protected override void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_start is not null || !(e.Properties.IsLeftButtonPressed || e.Properties.IsRightButtonPressed))
            return;
        _erasing = e.Properties.IsRightButtonPressed;
        if (!_erasing && Editor.Brush.Tiles.Count == 0)
        {
            Editor.Warn("Choose a tile", "Pick a tile in the Tile Map panel's palette, or Alt-click a tile in the map.");
            return;
        }

        _start = cell;
        Update(context, cell, (e.Modifiers & KeyModifiers.Shift) != 0);
    }

    protected override bool OnMoved(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell, bool cellChanged)
    {
        var constrain = (e.Modifiers & KeyModifiers.Shift) != 0;
        if (_start is null || (cell == _end && constrain == _constrain))
            return cellChanged;
        Update(context, cell, constrain);
        return true;
    }

    protected override void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_start is null || e.Properties.IsLeftButtonPressed || e.Properties.IsRightButtonPressed)
            return;
        _start = null;
        context.Hint = Description;
        if (_cells.IsEmpty || !Editor.TryGetEditableTileLayer(out var layer))
            return;
        var edit = Editor.BeginEdit();
        edit.Paint(layer, _cells, _erasing ? TileBrush.Eraser : Editor.Brush.CreateBrush(Editor.RotationSteps));
        Editor.Commit(edit, (_erasing ? "Erase " : "Draw ") + Name.ToLowerInvariant() + " of {0}");
        _cells.Clear();
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        var colors = TileOverlay.Palette;
        if (_start is null)
        {
            if (Editor.HoveredCell is { } hovered)
                Overlay.DrawCell(drawing, hovered, colors.AccentFill, colors.AccentPen);
            return;
        }

        if (_erasing)
        {
            Overlay.DrawCells(drawing, _cells, colors.EraseFill, colors.ErasePen);
            return;
        }

        _ghosts.Clear();
        if (_cells.Count <= TileOverlay.MaxGhostTiles)
        {
            var brush = Editor.Brush.CreateBrush(Editor.RotationSteps);
            foreach (var cell in _cells)
                _ghosts.Add(new PlacedTile(cell, brush.TileFor(cell)));
        }

        var drawn = _ghosts.Count > 0 && Overlay.DrawGhosts(drawing, _ghosts);
        Overlay.DrawCells(drawing, _cells, drawn ? null : colors.AccentFill, colors.AccentPen);
    }

    private void Update(ViewportToolContext context, GridCoord end, bool constrain)
    {
        _end = end;
        _constrain = constrain;
        _cells.Clear();
        BuildShape(Editor.Map!.Layout, _start!.Value, end, constrain, _cells);
        context.Hint = $"{Name}: {_cells.Count:N0} {(_cells.Count == 1 ? "cell" : "cells")}";
    }
}

/// <summary>Draws screen-aligned rectangles, filled or as outlines; Shift keeps them square.</summary>
public sealed class RectangleTool(TileMapEditor editor, TileOverlay overlay) : ShapeTool(editor, overlay)
{
    public override string Id => "tile.rectangle";

    public override string Name => "Rectangle";

    public override string Description => "Drag to draw a filled or outlined rectangle. Shift keeps it square, right-drag erases.";

    public override Geometry Icon => Icons.Square;

    public override string? Shortcut => "R";

    public override int Order => 4;

    public override TileToolOptions Options => TileToolOptions.Tile | TileToolOptions.Orientation | TileToolOptions.ShapeFill;

    protected override void BuildShape(IGridLayout layout, GridCoord start, GridCoord end, bool constrain, CellSet output)
    {
        var topology = layout.Topology;
        if (constrain)
        {
            var a = topology.ToOffset(start);
            var b = topology.ToOffset(end);
            var side = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            end = topology.FromOffset(new GridCoord(a.X + side * Math.Sign(b.X - a.X == 0 ? 1 : b.X - a.X), a.Y + side * Math.Sign(b.Y - a.Y == 0 ? 1 : b.Y - a.Y)));
        }

        GridShapes.Rectangle(topology, start, end, Editor.Brush.IsShapeFilled, output);
    }
}

/// <summary>Draws straight lines as thick as the brush.</summary>
public sealed class LineTool(TileMapEditor editor, TileOverlay overlay) : ShapeTool(editor, overlay)
{
    public override string Id => "tile.line";

    public override string Name => "Line";

    public override string Description => "Drag to draw a straight line as thick as the brush. Right-drag erases.";

    public override Geometry Icon => Icons.PenLine;

    public override string? Shortcut => "L";

    public override int Order => 5;

    public override TileToolOptions Options => TileToolOptions.Tile | TileToolOptions.Orientation | TileToolOptions.Size;

    protected override void BuildShape(IGridLayout layout, GridCoord start, GridCoord end, bool constrain, CellSet output) =>
        new GridBrush(Editor.Brush.Shape, Editor.Brush.Size).Stroke(layout, start, end, output);
}

/// <summary>Draws circles from the center, or ranges of grid steps, which are hexagons on hex grids and squares on square grids.</summary>
public sealed class CircleTool(TileMapEditor editor, TileOverlay overlay) : ShapeTool(editor, overlay)
{
    public override string Id => "tile.circle";

    public override string Name => "Circle";

    public override string Description => "Drag from the center to draw a circle, or a hexagon or square range, filled or outlined. Right-drag erases.";

    public override Geometry Icon => Icons.Circle;

    public override string? Shortcut => "C";

    public override int Order => 6;

    public override TileToolOptions Options => TileToolOptions.Tile | TileToolOptions.Orientation | TileToolOptions.ShapeFill | TileToolOptions.Range;

    protected override void BuildShape(IGridLayout layout, GridCoord start, GridCoord end, bool constrain, CellSet output)
    {
        if (Editor.Brush.IsRangeShape)
            GridShapes.Hexagon(layout.Topology, start, layout.Topology.Distance(start, end), Editor.Brush.IsShapeFilled, output);
        else
            GridShapes.Circle(layout, start, end, Editor.Brush.IsShapeFilled, output);
    }
}

/// <summary>Fills connected matching tiles, or every matching tile of the layer, within the selection when there is one; right-click fills with
/// empty. Previews the region under the pointer.</summary>
public sealed class FillTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    private const int PreviewLimit = 20_000;

    private readonly CellSet _preview = new();
    private readonly List<PlacedTile> _ghosts = [];
    private (GridCoord Cell, long Version, bool Contiguous, FillMatch Match, int Selection, TileLayer? Layer)? _previewKey;
    private bool _previewComplete;

    public override string Id => "tile.fill";

    public override string Name => "Fill";

    public override string Description => "Fill connected matching tiles, within the selection when there is one. Right-click fills with empty, Alt-click picks.";

    public override Geometry Icon => Icons.PaintBucket;

    public override string? Shortcut => "G";

    public override int Order => 3;

    public override TileToolOptions Options => TileToolOptions.Tile | TileToolOptions.Orientation | TileToolOptions.Fill;

    protected override async void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (!(e.Properties.IsLeftButtonPressed || e.Properties.IsRightButtonPressed) || Editor.Map is not { } map)
            return;
        var erasing = e.Properties.IsRightButtonPressed;
        if (!erasing && Editor.Brush.Tiles.Count == 0)
        {
            Editor.Warn("Choose a tile", "Pick a tile in the Tile Map panel's palette, or Alt-click a tile in the map.");
            return;
        }

        if (!Editor.TryGetEditableTileLayer(out var layer))
            return;
        var selection = Editor.Selection.IsEmpty ? null : Editor.Selection;
        if (selection is not null && !selection.Contains(cell))
            return;
        var brush = Editor.Brush;
        var target = layer.GetCell(cell);
        if (!brush.IsContiguousFill && target.IsEmpty && selection is null)
        {
            Editor.Warn("Cannot fill every empty cell", "Empty space is unbounded. Use a contiguous fill or select an area first.");
            return;
        }

        var region = new CellSet();
        var (contiguous, match, limit) = (brush.IsContiguousFill, brush.FillMatch, brush.FillLimit);
        Editor.IsBusy = true;
        FloodFillResult result;
        try
        {
            result = await Task.Run(() => contiguous
                ? TileFill.Contiguous(map, layer, cell, match, limit, region, selection)
                : TileFill.Global(map, layer, target, match, limit, region, selection));
        }
        finally
        {
            Editor.IsBusy = false;
        }

        if (result.ReachedLimit)
        {
            Editor.Warn("Fill area too large", $"The region has more than {limit:N0} cells. Close it with a border, select an area or raise the limit.");
            return;
        }

        var edit = Editor.BeginEdit();
        edit.Paint(layer, region, erasing ? TileBrush.Eraser : brush.CreateBrush(Editor.RotationSteps));
        Editor.Commit(edit, erasing ? "Clear {0}" : "Fill {0}");
        context.Invalidate();
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.HoveredCell is not { } cell || Editor.Map is not { } map || Editor.ActiveTileLayer is not { } layer)
            return;
        var colors = TileOverlay.Palette;
        var brush = Editor.Brush;
        var selection = Editor.Selection.IsEmpty ? null : Editor.Selection;
        var key = (cell, map.Version, brush.IsContiguousFill, brush.FillMatch, Editor.SelectionVersion, layer);
        if (_previewKey != key)
        {
            _previewKey = key;
            _preview.Clear();
            var limit = Math.Min(PreviewLimit, brush.FillLimit);
            var target = layer.GetCell(cell);
            var result = selection is not null && !selection.Contains(cell) ? new FloodFillResult(0, true)
                : brush.IsContiguousFill ? TileFill.Contiguous(map, layer, cell, brush.FillMatch, limit, _preview, selection)
                : target.IsEmpty && selection is null ? new FloodFillResult(0, true)
                : TileFill.Global(map, layer, target, brush.FillMatch, limit, _preview, selection);
            _previewComplete = !result.ReachedLimit;
        }

        if (!_previewComplete)
        {
            Overlay.DrawCell(drawing, cell, colors.AccentFill, colors.AccentPen);
            return;
        }

        _ghosts.Clear();
        if (brush.Tiles.Count > 0 && _preview.Count <= TileOverlay.MaxGhostTiles)
        {
            var tiles = brush.CreateBrush(Editor.RotationSteps);
            foreach (var filled in _preview)
                _ghosts.Add(new PlacedTile(filled, tiles.TileFor(filled)));
        }

        var drawn = _ghosts.Count > 0 && Overlay.DrawGhosts(drawing, _ghosts, 0.5);
        Overlay.DrawCells(drawing, _preview, drawn ? null : colors.AccentFill, colors.AccentPen);
    }
}

/// <summary>Picks the tile under the pointer into the brush with its orientation, then returns to the previous tool.</summary>
public sealed class PickerTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    public override string Id => "tile.picker";

    public override string Name => "Tile Picker";

    public override string Description => "Click a tile to paint with it. Other tile tools pick with Alt-click.";

    public override Geometry Icon => Icons.Pipette;

    public override string? Shortcut => "I";

    public override int Order => 7;

    public override TileToolOptions Options => TileToolOptions.Tile;

    protected override bool PicksWithAlt => false;

    protected override void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (e.Properties.IsLeftButtonPressed && Pick(cell))
            ReturnToPreviousTool(context);
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.HoveredCell is not { } cell || Editor.Map is not { } map)
            return;
        var picked = TilePicker.Pick(map, cell, Editor.ActiveTileLayer);
        Overlay.DrawCell(drawing, cell, picked is null ? null : TileOverlay.Palette.AccentFill, TileOverlay.Palette.AccentPen);
    }
}

/// <summary>Places the stamp: copied tiles or a rectangle picked in the palette, previewed under the pointer. Dragging repeats it a stamp apart;
/// Z and Shift+Z rotate it, X and Y flip it and Escape returns to the previous tool.</summary>
public sealed class StampTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    public const string ToolId = "tile.stamp";

    private readonly List<PlacedTile> _ghosts = [];
    private readonly CellSet _footprint = new();
    private TileEdit? _edit;
    private TileLayer? _layer;
    private GridCoord _lastPlaced;

    public override string Id => ToolId;

    public override string Name => "Stamp";

    public override string Description => "Place copied tiles. Drag to repeat, Z rotates, X and Y flip, Escape returns.";

    public override Geometry Icon => Icons.Paste;

    public override string? Shortcut => "S";

    public override int Order => 8;

    public override TileToolOptions Options => TileToolOptions.Stamp;

    public override bool IsOperationInProgress => _edit is not null;

    protected override bool TurnsBrush => false;

    public override void Cancel(ViewportToolContext context)
    {
        _edit?.Cancel();
        _edit = null;
    }

    protected override void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_edit is not null || !e.Properties.IsLeftButtonPressed)
            return;
        if (Editor.Brush.Stamp is not { IsEmpty: false } stamp)
        {
            Editor.Warn("Nothing to place", "Copy tiles with Ctrl+C, or drag across several tiles in the palette.");
            return;
        }

        if (!Editor.TryGetEditableTileLayer(out var layer))
            return;
        _layer = layer;
        _edit = Editor.BeginEdit();
        TileClipboard.Paste(_edit, layer, stamp, cell);
        _lastPlaced = cell;
    }

    protected override bool OnMoved(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell, bool cellChanged)
    {
        if (_edit is null || Editor.Brush.Stamp is not { } stamp || Editor.Map is not { } map)
            return cellChanged;
        var (width, height) = Extent(stamp, map.Layout.Topology);
        var a = map.Layout.Topology.ToOffset(_lastPlaced);
        var b = map.Layout.Topology.ToOffset(cell);
        if (Math.Abs(b.X - a.X) < width && Math.Abs(b.Y - a.Y) < height)
            return cellChanged;
        TileClipboard.Paste(_edit, _layer!, stamp, cell);
        _lastPlaced = cell;
        return true;
    }

    protected override void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_edit is not { } edit || e.Properties.IsLeftButtonPressed)
            return;
        _edit = null;
        Editor.Commit(edit, "Stamp {0}");
    }

    protected override bool OnKeyDown(ViewportToolContext context, KeyEventArgs e)
    {
        if (Editor.Map is not { } map || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None)
            return false;
        var topology = map.Layout.Topology;
        if (e.Key == Key.Escape && _edit is null)
        {
            ReturnToPreviousTool(context);
            return true;
        }

        if (Editor.Brush.Stamp is not { } stamp)
            return false;
        Editor.Brush.Stamp = e.Key switch
        {
            Key.Z when (e.KeyModifiers & KeyModifiers.Shift) != 0 => stamp.Rotate(-1, topology),
            Key.Z => stamp.Rotate(1, topology),
            Key.X => stamp.FlipHorizontal(topology),
            Key.Y => stamp.FlipVertical(topology),
            _ => stamp
        };
        return !ReferenceEquals(stamp, Editor.Brush.Stamp);
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.HoveredCell is not { } cell)
            return;
        var colors = TileOverlay.Palette;
        if (Editor.Brush.Stamp is not { IsEmpty: false } stamp)
        {
            Overlay.DrawCell(drawing, cell, null, colors.HoverPen);
            return;
        }

        _ghosts.Clear();
        _footprint.Clear();
        foreach (var placed in stamp.Cells)
        {
            _ghosts.Add(placed with { Cell = placed.Cell + cell });
            _footprint.Add(placed.Cell + cell);
        }

        var drawn = Overlay.DrawGhosts(drawing, _ghosts, 0.7);
        Overlay.DrawCells(drawing, _footprint, drawn ? null : colors.AccentFill, colors.AccentPen);
    }

    /// <summary>The stamp's width and height in screen-aligned columns and rows.</summary>
    private static (int Width, int Height) Extent(TileStamp stamp, GridTopology topology)
    {
        var bounds = GridBounds.Empty;
        foreach (var placed in stamp.Cells)
            bounds = bounds.Include(topology.ToOffset(placed.Cell));
        return (Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
    }
}
