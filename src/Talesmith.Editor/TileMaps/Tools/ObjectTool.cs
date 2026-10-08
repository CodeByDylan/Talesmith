using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;

namespace Talesmith.Editor.TileMaps.Tools;

/// <summary>Places, selects, moves and deletes objects of object layers: points, polygons and tile images. Placing snaps to cell centers; Alt
/// places freely, and Ctrl snaps polygon vertices to cell corners.</summary>
/// <remarks>Activating it with a tile in the brush switches from selecting to placing that tile. The selected object's name, type, position and properties are edited in the Tile Map panel; Delete removes it.</remarks>
public sealed class ObjectTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    private const double PointRadius = 6;
    private const double HitTolerance = 10;

    private readonly List<Vector2> _vertices = [];
    private MapObjectRef? _dragging;
    private MapObject? _original;
    private Vector2 _pressPosition;
    private bool _moved;
    private Vector2? _cursor;

    public override string Id => "tile.objects";

    public override string Name => "Objects";

    public override string Description => "Place and edit objects on object layers. Click to select, drag to move, Delete removes; Alt places freely.";

    public override Geometry Icon => Icons.MapPin;

    public override string? Shortcut => "O";

    public override int Order => 12;

    public override TileToolOptions Options => TileToolOptions.Objects;

    public override Cursor? Cursor => Editor.Brush.ObjectMode == ObjectToolMode.Select ? null : new Cursor(StandardCursorType.Cross);

    public override bool IsOperationInProgress => _dragging is not null || _vertices.Count > 0;

    protected override bool PicksWithAlt => false;

    public override void Activate(ViewportToolContext context)
    {
        base.Activate(context);
        if (Editor.Brush.ObjectMode == ObjectToolMode.Select && !Editor.Brush.PrimaryTile.IsEmpty)
            Editor.Brush.ObjectMode = ObjectToolMode.Tile;
    }

    public override void Cancel(ViewportToolContext context)
    {
        if (_dragging is { } dragging && _original is not null && dragging.Layer.Find(dragging.Id) is not null)
            dragging.Layer.Replace(_original);
        _dragging = null;
        _vertices.Clear();
    }

    protected override void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (!e.Properties.IsLeftButtonPressed || Editor.Map is not { } map)
            return;
        var position = e.World - Editor.Origin;
        switch (Editor.Brush.ObjectMode)
        {
            case ObjectToolMode.Select:
                BeginDrag(position);
                break;
            case ObjectToolMode.Point when Editor.TryGetEditableObjectLayer(out var layer):
                Place(layer, "Point", MapObjectShape.Point, Snap(map, position, e.Modifiers), [], TileCell.Empty);
                break;
            case ObjectToolMode.Tile when HitTest(map, position) is not null:
                BeginDrag(position);
                break;
            case ObjectToolMode.Tile when Editor.TryGetEditableObjectLayer(out var layer):
                var tile = Editor.Brush.PrimaryTileFor(Editor.RotationSteps);
                if (tile.IsEmpty)
                {
                    Editor.Warn("Choose a tile", "Pick a tile in the Tile Map panel's palette to place it as an object.");
                    break;
                }

                var name = map.FindTileset(tile.TilesetId)?.Find(tile.TileId)?.Name;
                Place(layer, string.IsNullOrEmpty(name) ? "Image" : name, MapObjectShape.Tile, Snap(map, position, e.Modifiers), [], tile);
                break;
            case ObjectToolMode.Polygon:
                if (e.ClickCount >= 2)
                {
                    FinishPolygon();
                    break;
                }

                if (_vertices.Count == 0 && !Editor.TryGetEditableObjectLayer(out _))
                    break;
                var vertex = Snap(map, position, e.Modifiers);
                if (_vertices.Count == 0 || Vector2.Distance(_vertices[^1], vertex) > 1e-3f)
                    _vertices.Add(vertex);
                break;
        }
    }

    protected override bool OnMoved(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell, bool cellChanged)
    {
        if (Editor.Map is not { } map)
            return false;
        var position = e.World - Editor.Origin;
        _cursor = Editor.Brush.ObjectMode == ObjectToolMode.Select ? position : Snap(map, position, e.Modifiers);
        if (_dragging is not { } dragging || _original is not { } original)
            return true;
        var moved = original.Position + position - _pressPosition;
        if (!_moved && Vector2.Distance(position, _pressPosition) * Overlay.Camera.Zoom < 4)
            return false;
        _moved = true;
        var target = (e.Modifiers & KeyModifiers.Alt) != 0 ? moved : map.Layout.CellToWorld(map.Layout.WorldToCell(moved));
        if (dragging.Object is { } current && current.Position != target)
            dragging.Layer.Replace(current.MoveTo(target, map.Layout));
        return true;
    }

    protected override void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_dragging is not { } dragging || _original is not { } original || e.Properties.IsLeftButtonPressed)
            return;
        _dragging = null;
        if (dragging.Object is { } moved && moved != original)
            Editor.Record($"Move \"{original.Name}\"", MapEdits.ReplaceObject(dragging.Layer, original));
    }

    protected override bool OnKeyDown(ViewportToolContext context, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when _vertices.Count > 0:
                FinishPolygon();
                return true;
            case Key.Back when _vertices.Count > 0:
                _vertices.RemoveAt(_vertices.Count - 1);
                return true;
            case Key.Delete or Key.Back when Editor.SelectedObject is not null && _dragging is null:
                Editor.DeleteSelectedObject();
                return true;
            case Key.Escape when Editor.SelectedObject is not null && !IsOperationInProgress:
                Editor.SelectedObject = null;
                return true;
            default:
                return false;
        }
    }

    protected override void RenderBackground(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.Map is not { } map)
            return;
        var transform = Overlay.WorldToScreen;
        foreach (var layer in map.ObjectLayers)
        {
            if (!layer.IsVisible)
                continue;
            var color = TileArt.ToAvalonia(layer.Color ?? ObjectLayer.DefaultColor);
            var pen = new ImmutablePen(new ImmutableSolidColorBrush(color), ReferenceEquals(layer, Editor.ActiveLayer) ? 1.75 : 1.25, lineJoin: PenLineJoin.Round);
            var fill = new ImmutableSolidColorBrush(color, 0.18);
            foreach (var mapObject in layer.Objects)
            {
                var selected = Editor.SelectedObject is { } s && ReferenceEquals(s.Layer, layer) && s.Id == mapObject.Id;
                DrawObject(drawing, map, transform, mapObject, selected ? TileOverlay.Palette.AccentFill : fill, selected ? TileOverlay.Palette.AccentPen : pen, selected);
            }
        }
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        var colors = TileOverlay.Palette;
        if (Editor.Map is not { } map || _cursor is not { } cursor)
            return;
        switch (Editor.Brush.ObjectMode)
        {
            case ObjectToolMode.Polygon:
                DrawPolyline(drawing, colors, cursor);
                break;
            case ObjectToolMode.Point:
                var center = Overlay.ToScreen(cursor);
                drawing.DrawEllipse(colors.AccentFill, colors.AccentPen, center, PointRadius, PointRadius);
                break;
            case ObjectToolMode.Tile when Editor.Brush.PrimaryTileFor(Editor.RotationSteps) is { IsEmpty: false } tile:
                using (drawing.PushOpacity(0.6))
                    TileArt.DrawCentered(drawing, Overlay.WorldToScreen, map, Editor.Origin + cursor, tile);
                break;
        }
    }

    /// <summary>Finds the topmost visible object under a point in map coordinates, preferring the active layer.</summary>
    internal MapObjectRef? HitTest(TileMap map, Vector2 position)
    {
        var tolerance = HitTolerance / Overlay.Camera.Zoom;
        var layers = map.ObjectLayers.Where(l => l.IsVisible).Reverse().ToList();
        if (Editor.ActiveObjectLayer is { } active && layers.Remove(active))
            layers.Insert(0, active);
        foreach (var layer in layers)
        {
            for (var i = layer.Objects.Count - 1; i >= 0; i--)
            {
                var mapObject = layer.Objects[i];
                var hit = mapObject.Shape switch
                {
                    MapObjectShape.Polygon when mapObject.Polygon.Count >= 3 => mapObject.Contains(position),
                    MapObjectShape.Tile => TileBounds(map, mapObject).Contains(position),
                    _ => Vector2.Distance(mapObject.Position, position) <= tolerance
                };
                if (hit)
                    return new MapObjectRef(layer, mapObject.Id);
            }
        }

        return null;
    }

    private void BeginDrag(Vector2 position)
    {
        if (Editor.Map is not { } map)
            return;
        var hit = HitTest(map, position);
        Editor.SelectedObject = hit;
        if (hit is null)
            return;
        Editor.ActiveLayer = hit.Layer;
        if (hit.Layer.IsLocked)
            return;
        _dragging = hit;
        _original = hit.Object;
        _pressPosition = position;
        _moved = false;
    }

    private void Place(ObjectLayer layer, string baseName, MapObjectShape shape, Vector2 position, IReadOnlyList<Vector2> polygon, TileCell tile)
    {
        var map = Editor.Map!;
        var id = map.NextObjectId;
        var mapObject = new MapObject(id, string.Create(CultureInfo.InvariantCulture, $"{baseName} {id}"), "", shape, position, map.Layout.WorldToCell(position),
            polygon, tile, PropertySet.Empty);
        Editor.Execute($"Add {shape.ToString().ToLowerInvariant()} \"{mapObject.Name}\"", MapEdits.InsertObject(layer, mapObject, layer.Objects.Count));
        Editor.SelectedObject = new MapObjectRef(layer, id);
    }

    private void FinishPolygon()
    {
        var vertices = _vertices.ToList();
        _vertices.Clear();
        if (vertices.Count < 3 || !Editor.TryGetEditableObjectLayer(out var layer))
            return;
        var centroid = vertices.Aggregate(Vector2.Zero, (sum, v) => sum + v) / vertices.Count;
        Place(layer, "Area", MapObjectShape.Polygon, centroid, [.. vertices.Select(v => v - centroid)], TileCell.Empty);
    }

    /// <summary>Snaps to the nearest cell center, or with Ctrl the nearest cell corner; Alt places freely.</summary>
    private static Vector2 Snap(TileMap map, Vector2 position, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Alt) != 0)
            return position;
        var layout = map.Layout;
        var center = layout.CellToWorld(layout.WorldToCell(position));
        if ((modifiers & KeyModifiers.Control) == 0)
            return center;
        var best = center + layout.CornerOffset(0);
        for (var i = 1; i < layout.CornerCount; i++)
        {
            var corner = center + layout.CornerOffset(i);
            if (Vector2.DistanceSquared(corner, position) < Vector2.DistanceSquared(best, position))
                best = corner;
        }

        return best;
    }

    private static Mathematics.Rect2 TileBounds(TileMap map, MapObject mapObject)
    {
        var size = map.FindTileset(mapObject.Tile.TilesetId) is { IsColorTileset: false } tileset ? new Vector2(tileset.TileWidth, tileset.TileHeight) : map.Layout.CellSize;
        return Mathematics.Rect2.FromCenter(mapObject.Position, size);
    }

    private void DrawObject(DrawingContext drawing, TileMap map, Matrix transform, MapObject mapObject, IBrush fill, IPen pen, bool selected)
    {
        var center = Overlay.ToScreen(mapObject.Position);
        switch (mapObject.Shape)
        {
            case MapObjectShape.Polygon when mapObject.Polygon.Count >= 2:
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(Overlay.ToScreen(mapObject.Position + mapObject.Polygon[0]), true);
                    for (var i = 1; i < mapObject.Polygon.Count; i++)
                        context.LineTo(Overlay.ToScreen(mapObject.Position + mapObject.Polygon[i]));
                    context.EndFigure(true);
                }

                drawing.DrawGeometry(fill, pen, geometry);
                break;
            case MapObjectShape.Tile:
                var bounds = TileBounds(map, mapObject);
                var topLeft = Overlay.ToScreen(new Vector2(bounds.X, bounds.Y));
                drawing.DrawRectangle(null, pen, new Rect(topLeft, new Size(bounds.Width * Overlay.Camera.Zoom, bounds.Height * Overlay.Camera.Zoom)), 3, 3);
                break;
            default:
                drawing.DrawEllipse(fill, pen, center, PointRadius, PointRadius);
                drawing.DrawEllipse(pen.Brush, null, center, 2, 2);
                break;
        }

        if (!selected)
            return;
        var label = new FormattedText(mapObject.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, TileOverlay.Palette.White);
        var origin = new Point(center.X - label.Width / 2, center.Y - PointRadius - label.Height - 6);
        drawing.DrawRectangle(TileOverlay.Palette.Accent, null, new Rect(origin.X - 5, origin.Y - 2, label.Width + 10, label.Height + 4), 4, 4);
        drawing.DrawText(label, origin);
    }

    private void DrawPolyline(DrawingContext drawing, TileOverlay.Colors colors, Vector2 cursor)
    {
        if (_vertices.Count == 0)
        {
            drawing.DrawEllipse(colors.White, colors.AccentPen, Overlay.ToScreen(cursor), 4, 4);
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Overlay.ToScreen(_vertices[0]), _vertices.Count >= 2);
            for (var i = 1; i < _vertices.Count; i++)
                context.LineTo(Overlay.ToScreen(_vertices[i]));
            context.LineTo(Overlay.ToScreen(cursor));
            context.EndFigure(_vertices.Count >= 2);
        }

        drawing.DrawGeometry(_vertices.Count >= 2 ? colors.AccentFill : null, colors.SelectionShadowPen, geometry);
        foreach (var vertex in _vertices)
            drawing.DrawEllipse(colors.White, colors.AccentPen, Overlay.ToScreen(vertex), 4, 4);
    }
}
