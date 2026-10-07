using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;
using GridSelectionMode = Talesmith.Grids.SelectionMode;

namespace Talesmith.Editor.TileMaps.Tools;

/// <summary>Selects cells by rectangle, lasso or matching tiles, and moves the selected tiles by dragging them.</summary>
/// <remarks>Shift adds to the selection, Ctrl subtracts, Shift+Ctrl intersects; a click without dragging clears it. Ctrl+C, Ctrl+X, Ctrl+V and
/// Delete copy, cut, paste and clear the selected tiles, Ctrl+A selects every tile of the layer.</remarks>
public sealed class TileSelectTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    private const double DragThreshold = 4;

    private readonly CellSet _cells = new();
    private readonly List<Vector2> _lasso = [];
    private readonly List<PlacedTile> _ghosts = [];
    private Point? _pressPoint;
    private GridCoord _start;
    private GridCoord _current;
    private bool _dragging;
    private bool _moving;
    private GridSelectionMode _mode;

    public override string Id => "tile.select";

    public override string Name => "Select Tiles";

    public override string Description => "Drag to select cells; drag the selection to move its tiles. Shift adds, Ctrl subtracts, Ctrl+C copies.";

    public override Geometry Icon => Icons.SquareDashed;

    public override string? Shortcut => "M";

    public override int Order => 11;

    public override TileToolOptions Options => TileToolOptions.Selection;

    public override bool IsOperationInProgress => _pressPoint is not null;

    public override Cursor? Cursor => new(StandardCursorType.Cross);

    public override void Cancel(ViewportToolContext context) => Reset();

    protected override void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (!e.Properties.IsLeftButtonPressed || _pressPoint is not null)
            return;
        _pressPoint = e.Position;
        _start = _current = cell;
        _mode = ModeOf(e.Modifiers);
        _moving = _mode == GridSelectionMode.Replace && Editor.Selection.Contains(cell) && Editor.Brush.SelectionShape != CellSelectionShape.Wand;
        _lasso.Clear();
        _lasso.Add(e.World - Editor.Origin);
        if (!_moving && Editor.Brush.SelectionShape == CellSelectionShape.Wand)
        {
            SelectMatching(cell);
            Reset();
        }
    }

    protected override bool OnMoved(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell, bool cellChanged)
    {
        if (_pressPoint is not { } press)
            return cellChanged;
        if (!_dragging && Point.Distance(press, e.Position) < DragThreshold)
            return false;
        _dragging = true;
        _current = cell;
        if (_moving)
        {
            var delta = cell - _start;
            context.Hint = $"Move {Editor.Selection.Count:N0} cells by {delta.X}, {delta.Y}";
            return true;
        }

        _cells.Clear();
        if (Editor.Brush.SelectionShape == CellSelectionShape.Lasso)
        {
            var point = e.World - Editor.Origin;
            if (Vector2.Distance(point, _lasso[^1]) * Overlay.Camera.Zoom >= 3)
                _lasso.Add(point);
            GridShapes.Polygon(Editor.Map!.Layout, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_lasso), _cells);
        }
        else
        {
            GridShapes.Rectangle(Editor.Map!.Layout.Topology, _start, cell, true, _cells);
        }

        context.Hint = $"Selecting {_cells.Count:N0} {(_cells.Count == 1 ? "cell" : "cells")}";
        return true;
    }

    protected override void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_pressPoint is null || e.Properties.IsLeftButtonPressed)
            return;
        context.Hint = Description;
        if (_moving && _dragging)
            MoveSelection(cell - _start);
        else if (_dragging)
            Editor.Select(_cells, _mode);
        else if (_mode == GridSelectionMode.Replace)
            Editor.ClearSelection();
        Reset();
    }

    protected override bool OnKeyDown(ViewportToolContext context, KeyEventArgs e)
    {
        if (e.Key == Key.A && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            Editor.SelectAll();
            return true;
        }

        return false;
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        var colors = TileOverlay.Palette;
        if (_moving && _dragging && Editor.ActiveTileLayer is { } layer)
        {
            var delta = _current - _start;
            _ghosts.Clear();
            _cells.Clear();
            foreach (var cell in Editor.Selection)
            {
                _cells.Add(cell + delta);
                if (_ghosts.Count <= TileOverlay.MaxGhostTiles && layer.GetCell(cell) is { IsEmpty: false } tile)
                    _ghosts.Add(new PlacedTile(cell + delta, tile));
            }

            Overlay.DrawGhosts(drawing, _ghosts, 0.75);
            Overlay.DrawCells(drawing, _cells, null, colors.AccentPen);
            return;
        }

        if (_dragging)
        {
            Overlay.DrawCells(drawing, _cells, _mode == GridSelectionMode.Subtract ? colors.EraseFill : colors.AccentFill,
                _mode == GridSelectionMode.Subtract ? colors.ErasePen : colors.AccentPen);
            if (Editor.Brush.SelectionShape == CellSelectionShape.Lasso && _lasso.Count > 1)
                DrawLasso(drawing, colors);
            return;
        }

        base.RenderPreview(context, drawing);
    }

    private static GridSelectionMode ModeOf(KeyModifiers modifiers)
    {
        var add = (modifiers & KeyModifiers.Shift) != 0;
        var subtract = (modifiers & KeyModifiers.Control) != 0;
        return add && subtract ? GridSelectionMode.Intersect : add ? GridSelectionMode.Add : subtract ? GridSelectionMode.Subtract : GridSelectionMode.Replace;
    }

    private void SelectMatching(GridCoord cell)
    {
        if (Editor.Map is not { } map || Editor.ActiveTileLayer is not { } layer)
            return;
        var region = new CellSet();
        var result = TileFill.Contiguous(map, layer, cell, Editor.Brush.FillMatch, Editor.Brush.FillLimit, region);
        if (result.ReachedLimit)
        {
            Editor.Warn("Selection too large", $"The region has more than {Editor.Brush.FillLimit:N0} cells. Empty space is unbounded.");
            return;
        }

        Editor.Select(region, _mode);
    }

    private void MoveSelection(GridCoord delta)
    {
        if (delta == GridCoord.Zero || !Editor.TryGetEditableTileLayer(out var layer))
            return;
        var edit = Editor.BeginEdit();
        var moved = TileClipboard.Move(edit, layer, Editor.Selection, delta);
        Editor.Commit(edit, "Move {0}");
        Editor.SetSelection(moved);
    }

    private void DrawLasso(DrawingContext drawing, TileOverlay.Colors colors)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Overlay.ToScreen(_lasso[0]), false);
            for (var i = 1; i < _lasso.Count; i++)
                context.LineTo(Overlay.ToScreen(_lasso[i]));
            context.EndFigure(false);
        }

        drawing.DrawGeometry(null, colors.SelectionPen, geometry);
    }

    private void Reset()
    {
        _pressPoint = null;
        _dragging = false;
        _moving = false;
        _cells.Clear();
        _lasso.Clear();
    }
}
