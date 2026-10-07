using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;
using TileBrush = Talesmith.Assets.Maps.Editing.TileBrush;

namespace Talesmith.Editor.TileMaps.Tools;

/// <summary>Base of tools that paint while dragging: interpolates between pointer samples so fast moves leave no gaps, and makes the whole
/// stroke one undo step. Right-drag erases; Shift-click continues with a straight line from the end of the last stroke.</summary>
public abstract class StrokeTool(TileMapEditor editor, TileOverlay overlay) : TileTool(editor, overlay)
{
    private readonly CellSet _cells = new();
    private readonly CellSet _footprint = new();
    private readonly List<PlacedTile> _ghosts = [];
    private TileEdit? _edit;
    private TileLayer? _layer;
    private bool _erasing;
    private GridCoord _last;
    private GridCoord? _strokeEnd;

    public override bool IsOperationInProgress => _edit is not null;

    /// <summary>The cells the brush covers around a cell.</summary>
    protected GridBrush Footprint => new(Editor.Brush.Shape, Editor.Brush.Size);

    /// <summary>The verb of the undo step, such as "Paint".</summary>
    protected abstract string Verb(bool erasing);

    /// <summary>Writes the stroke's effect into cells covered since the last pointer sample.</summary>
    protected abstract void Paint(TileEdit edit, TileLayer layer, CellSet cells, bool erasing);

    /// <summary>The layer a stroke paints, or null when it cannot start; tells the user why.</summary>
    protected virtual TileLayer? ResolveLayer() => Editor.TryGetEditableTileLayer(out var layer) ? layer : null;

    /// <summary>Whether a stroke can start; tools explain why not.</summary>
    protected virtual bool CanStart(bool erasing) => true;

    /// <summary>Adds the tiles the preview shows for <paramref name="cells"/>; none shows outlines only.</summary>
    protected virtual void Ghosts(TileLayer? layer, CellSet cells, List<PlacedTile> output)
    {
    }

    public override void Cancel(ViewportToolContext context)
    {
        _edit?.Cancel();
        _edit = null;
    }

    protected override void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_edit is not null || !(e.Properties.IsLeftButtonPressed || e.Properties.IsRightButtonPressed))
            return;
        var erasing = e.Properties.IsRightButtonPressed;
        if (!CanStart(erasing) || ResolveLayer() is not { } layer)
            return;
        _layer = layer;
        _erasing = erasing;
        _edit = Editor.BeginEdit();
        var start = (e.Modifiers & KeyModifiers.Shift) != 0 && _strokeEnd is { } end ? end : cell;
        PaintSegment(start, cell);
    }

    protected override bool OnMoved(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell, bool cellChanged)
    {
        if (_edit is null || cell == _last)
            return cellChanged;
        PaintSegment(_last, cell);
        return true;
    }

    protected override void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        if (_edit is not { } edit || e.Properties.IsLeftButtonPressed || e.Properties.IsRightButtonPressed)
            return;
        _edit = null;
        _strokeEnd = _last;
        Editor.Commit(edit, Verb(_erasing) + " {0}");
    }

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.HoveredCell is not { } cell || Editor.Map is not { } map)
            return;
        var colors = TileOverlay.Palette;
        _footprint.Clear();
        Footprint.Footprint(map.Layout, cell, _footprint);
        if (_edit is null)
        {
            _ghosts.Clear();
            Ghosts(Editor.ActiveTileLayer, _footprint, _ghosts);
            if (_ghosts.Count > 0)
                Overlay.DrawGhosts(drawing, _ghosts);
        }

        var erasing = _edit is not null && _erasing;
        Overlay.DrawCells(drawing, _footprint, erasing ? colors.EraseFill : _ghosts.Count > 0 ? null : colors.AccentFill, erasing ? colors.ErasePen : colors.AccentPen);
    }

    private void PaintSegment(GridCoord from, GridCoord to)
    {
        _cells.Clear();
        Footprint.Stroke(Editor.Map!.Layout, from, to, _cells);
        Paint(_edit!, _layer!, _cells, _erasing);
        _last = to;
    }
}

/// <summary>Paints the brush tiles, picking among several by weight; right-drag erases, Alt-click picks, Shift-click draws a line.</summary>
public sealed class BrushTool(TileMapEditor editor, TileOverlay overlay) : StrokeTool(editor, overlay)
{
    public override string Id => "tile.brush";

    public override string Name => "Brush";

    public override string Description => "Paint tiles. Right-drag erases, Alt-click picks, Shift-click draws a line, Z rotates and X flips.";

    public override Geometry Icon => Icons.Brush;

    public override string? Shortcut => "B";

    public override int Order => 0;

    public override TileToolOptions Options => TileToolOptions.Tile | TileToolOptions.Size | TileToolOptions.Orientation;

    protected override string Verb(bool erasing) => erasing ? "Erase" : "Paint";

    protected override bool CanStart(bool erasing)
    {
        if (erasing || Editor.Brush.Tiles.Count > 0)
            return true;
        Editor.Warn("Choose a tile", "Pick a tile in the Tile Map panel's palette, or Alt-click a tile in the map.");
        return false;
    }

    protected override void Paint(TileEdit edit, TileLayer layer, CellSet cells, bool erasing) =>
        edit.Paint(layer, cells, erasing ? TileBrush.Eraser : Editor.Brush.CreateBrush(Editor.RotationSteps));

    protected override void Ghosts(TileLayer? layer, CellSet cells, List<PlacedTile> output)
    {
        if (Editor.Brush.Tiles.Count == 0 || cells.Count > TileOverlay.MaxGhostTiles)
            return;
        var brush = Editor.Brush.CreateBrush(Editor.RotationSteps);
        foreach (var cell in cells)
            output.Add(new PlacedTile(cell, brush.TileFor(cell)));
    }
}

/// <summary>Paints tiles picked at random by weight from the palette selection, deterministically per cell so previews match.</summary>
public sealed class RandomBrushTool(TileMapEditor editor, TileOverlay overlay) : StrokeTool(editor, overlay)
{
    public override string Id => "tile.random";

    public override string Name => "Random Brush";

    public override string Description => "Paint tiles picked at random by weight. Ctrl-click tiles in the palette to add them; R reshuffles.";

    public override Geometry Icon => Icons.Dice;

    public override string? Shortcut => "N";

    public override int Order => 1;

    public override TileToolOptions Options => TileToolOptions.Weights | TileToolOptions.Size | TileToolOptions.Orientation;

    protected override string Verb(bool erasing) => erasing ? "Erase" : "Scatter";

    protected override bool CanStart(bool erasing)
    {
        if (erasing || Editor.Brush.Tiles.Count > 0)
            return true;
        Editor.Warn("Choose tiles", "Ctrl-click several tiles in the Tile Map panel's palette to paint them at random.");
        return false;
    }

    protected override bool OnKeyDown(ViewportToolContext context, KeyEventArgs e)
    {
        if (e.Key != Key.R || e.KeyModifiers != KeyModifiers.None)
            return false;
        Editor.Brush.Seed++;
        context.Invalidate();
        return true;
    }

    protected override void Paint(TileEdit edit, TileLayer layer, CellSet cells, bool erasing) =>
        edit.Paint(layer, cells, erasing ? TileBrush.Eraser : Editor.Brush.CreateBrush(Editor.RotationSteps));

    protected override void Ghosts(TileLayer? layer, CellSet cells, List<PlacedTile> output)
    {
        if (Editor.Brush.Tiles.Count == 0 || cells.Count > TileOverlay.MaxGhostTiles)
            return;
        var brush = Editor.Brush.CreateBrush(Editor.RotationSteps);
        foreach (var cell in cells)
            output.Add(new PlacedTile(cell, brush.TileFor(cell)));
    }
}

/// <summary>Clears the tiles under the brush.</summary>
public sealed class EraserTool(TileMapEditor editor, TileOverlay overlay) : StrokeTool(editor, overlay)
{
    private readonly CellSet _footprint = new();

    public override string Id => "tile.eraser";

    public override string Name => "Eraser";

    public override string Description => "Erase tiles under the brush. Shift-click erases a line.";

    public override Geometry Icon => Icons.Eraser;

    public override string? Shortcut => "E";

    public override int Order => 2;

    public override TileToolOptions Options => TileToolOptions.Size;

    protected override string Verb(bool erasing) => "Erase";

    protected override void Paint(TileEdit edit, TileLayer layer, CellSet cells, bool erasing) => edit.Fill(layer, cells, TileCell.Empty);

    protected override void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.HoveredCell is not { } cell || Editor.Map is not { } map)
            return;
        _footprint.Clear();
        Footprint.Footprint(map.Layout, cell, _footprint);
        Overlay.DrawCells(drawing, _footprint, TileOverlay.Palette.EraseFill, TileOverlay.Palette.ErasePen);
    }

}

/// <summary>Paints the selected terrain, resolving transitions with its auto-tile rules as Hexy does; right-drag erases terrain.</summary>
public sealed class TerrainTool(TileMapEditor editor, TileOverlay overlay) : StrokeTool(editor, overlay)
{
    private (GridCoord Cell, long Version, Terrain? Terrain, int Size, TileLayer? Layer)? _previewKey;
    private Dictionary<GridCoord, TileCell>? _preview;

    public override string Id => "tile.terrain";

    public override string Name => "Terrain";

    public override string Description => "Paint terrain with automatic transitions. Right-drag erases terrain.";

    public override Geometry Icon => Icons.Wand;

    public override string? Shortcut => "T";

    public override int Order => 9;

    public override TileToolOptions Options => TileToolOptions.Terrain | TileToolOptions.Size;

    protected override string Verb(bool erasing) => erasing ? "Erase terrain on" : "Paint terrain on";

    protected override bool CanStart(bool erasing)
    {
        if (erasing || Editor.Brush.Terrain is not null)
            return true;
        Editor.Warn("Choose a terrain", "Select a terrain in the Tile Map panel to paint with automatic transitions.");
        return false;
    }

    protected override void Paint(TileEdit edit, TileLayer layer, CellSet cells, bool erasing) =>
        AutoTiler.Paint(edit, layer, cells, erasing ? null : Editor.Brush.Terrain);

    protected override void Ghosts(TileLayer? layer, CellSet cells, List<PlacedTile> output)
    {
        if (layer is null || Editor.Map is not { } map || Editor.Brush.Terrain is not { } terrain || Editor.HoveredCell is not { } cell
            || cells.Count > TileOverlay.MaxGhostTiles)
            return;
        var key = (cell, map.Version, terrain, Editor.Brush.Size, layer);
        if (_previewKey != key)
        {
            _previewKey = key;
            _preview = AutoTiler.ComputeChanges(layer, map.Terrains, map.Layout.Topology, cells, terrain);
        }

        foreach (var (changed, tile) in _preview!)
            output.Add(new PlacedTile(changed, tile));
    }
}

/// <summary>Paints solid cells on a collision layer, creating the layer and a "Collision" tileset when the map has none; right-drag clears.</summary>
/// <remarks>Collision layers make non-empty cells solid unless their tile defines its own shapes, which the tile properties edit.</remarks>
public sealed class CollisionTool(TileMapEditor editor, TileOverlay overlay) : StrokeTool(editor, overlay)
{
    public const string TilesetName = "Collision";
    public const string SolidTileName = "Solid";

    private readonly CellSet _solidCells = new();
    private UndoTransaction? _setup;
    private TileCell _solid;

    public override string Id => "tile.collision";

    public override string Name => "Collision";

    public override string Description => "Paint solid cells on the collision layer, which is created when needed. Right-drag clears.";

    public override Geometry Icon => Icons.Shield;

    public override string? Shortcut => "K";

    public override int Order => 10;

    public override TileToolOptions Options => TileToolOptions.Size | TileToolOptions.Collision;

    protected override string Verb(bool erasing) => erasing ? "Clear collision on" : "Make solid";

    protected override TileLayer? ResolveLayer()
    {
        if (Editor.Map is not { } map)
            return null;
        var layer = Editor.ActiveTileLayer is { Role: LayerRole.Collision } active ? active : map.TileLayers.LastOrDefault(l => l.Role == LayerRole.Collision);
        if (layer is null)
        {
            _setup ??= Editor.BeginTransaction("Paint collision");
            layer = map.CreateTileLayer(map.UniqueLayerName("Collision"), LayerRole.Collision);
            layer.Opacity = 0.55f;
            Editor.Execute($"Add layer \"{layer.Name}\"", MapEdits.InsertLayer(layer, map.Layers.Count));
            Editor.ActiveLayer = layer;
        }

        if (!Editor.CanEdit(layer))
            return null;
        if (FindSolidTile(map) is not { } solid)
        {
            _setup ??= Editor.BeginTransaction("Paint collision");
            solid = AddSolidTileset(map);
        }

        _solid = solid;
        return layer;
    }

    public override void Cancel(ViewportToolContext context)
    {
        base.Cancel(context);
        _setup?.Cancel();
        _setup = null;
    }

    protected override void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
        base.OnReleased(context, e, cell);
        if (IsOperationInProgress)
            return;
        _setup?.Dispose();
        _setup = null;
    }

    protected override void Paint(TileEdit edit, TileLayer layer, CellSet cells, bool erasing) => edit.Fill(layer, cells, erasing ? TileCell.Empty : _solid);

    protected override void RenderBackground(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.Map is not { } map)
            return;
        var bounds = Overlay.VisibleCells;
        if ((long)bounds.Width * bounds.Height > TileOverlay.MaxDrawnCells * 4L)
            return;
        var solid = _solidCells;
        solid.Clear();
        foreach (var layer in map.TileLayers)
        {
            if (layer.Role != LayerRole.Collision || !layer.IsVisible)
                continue;
            for (var y = bounds.MinY; y <= bounds.MaxY; y++)
            {
                for (var x = bounds.MinX; x <= bounds.MaxX; x++)
                {
                    if (!layer.GetCell(new GridCoord(x, y)).IsEmpty)
                        solid.Add(new GridCoord(x, y));
                }
            }
        }

        Overlay.DrawCells(drawing, solid, TileOverlay.Palette.CollisionFill, TileOverlay.Palette.CollisionPen);
    }

    /// <summary>The tile collision layers are painted with, from the map's "Collision" color tileset.</summary>
    private static TileCell? FindSolidTile(TileMap map)
    {
        foreach (var tileset in map.Tilesets)
        {
            if (!tileset.IsColorTileset || tileset.Name != TilesetName)
                continue;
            for (var id = 0; id < tileset.TileCount; id++)
            {
                if (tileset.Find(id)?.Name == SolidTileName)
                    return new TileCell(tileset.Id, id);
            }
        }

        return null;
    }

    private TileCell AddSolidTileset(TileMap map)
    {
        var tileset = Tileset.FromColors(TilesetName, [(SolidTileName, new Mathematics.Color(0xEF, 0x44, 0x44))]);
        Editor.Execute($"Add tileset \"{TilesetName}\"", MapEdits.InsertTileset(tileset, map.Tilesets.Count));
        return new TileCell(tileset.Id, 0);
    }
}
