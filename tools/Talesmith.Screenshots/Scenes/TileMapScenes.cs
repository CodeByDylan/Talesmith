using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.TileMaps.Dialogs;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.Screenshots.Capture;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>A sample's map in the Tile Mapping layout: the Tile Map panel with layers and palette, and the brush previewing its tile under the pointer.</summary>
internal class TileMapScene : EditorWindowScene
{
    private static readonly Pointer Mouse = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);

    public override string Name => "tilemap-hex";

    protected override string Project => EditorFixture.HexQuest;

    protected TileMapEditor Map => Editor.Get<TileMapEditor>();

    protected ToolManager Tools => Editor.Get<ToolManager>();

    protected ViewportToolContext Context => Editor.Get<ViewportToolContext>();

    protected override void Customize(Window window)
    {
        Editor.Shell.SelectedPreset = LayoutService.TileMappingPreset;
        RenderLoop.Settle(200);
        Editor.Get<LayoutService>().ShowPanel(PanelIds.TileMap);
        var documents = Editor.Get<ISceneDocumentService>();
        var entity = documents.Active!.Entities.Last(e => e.FindComponent("TileMapRenderer") is not null && (MapEntity is null || e.Name == MapEntity));
        Editor.Get<ISelectionService>().SelectEntity(entity.Id);
        RenderLoop.Wait(() => Map.Map is not null, 30000);
        RenderLoop.Settle(300);
        Editor.Get<ViewportService>().FrameSelection(animate: false);
        RenderLoop.Settle(200);
        Editor.Get<ViewportCamera>().ZoomTo(Zoom, animate: false);
        RenderLoop.Settle(200);
        Paint(window);
        RenderLoop.Settle(400);
    }

    protected virtual float Zoom => 0.55f;

    /// <summary>The name of the entity whose map is edited, or null for the last map of the scene.</summary>
    protected virtual string? MapEntity => null;

    /// <summary>Uses the tools; by default hovers the brush over the middle of the view.</summary>
    protected virtual void Paint(Window window)
    {
        var map = Map.Map!;
        Map.ActiveLayer = map.TileLayers[^1];
        var tileset = map.Tilesets.First(t => t.TileCount > 2);
        Map.Brush.Pick(new TileCell(tileset.Id, Math.Min(3, tileset.TileCount - 1)));
        Map.Brush.Size = 2;
        Tools.Select("tile.brush");
        Hover(CenterCell());
    }

    protected GridCoord CenterCell() => Map.CellAt(Editor.Get<ViewportCamera>().Position);

    protected void Hover(GridCoord cell) => Tools.ActiveTool.PointerMoved(Context, Args(cell, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other)));

    protected void Press(GridCoord cell) =>
        Tools.ActiveTool.PointerPressed(Context, Args(cell, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), 1));

    protected void Drag(GridCoord cell) =>
        Tools.ActiveTool.PointerMoved(Context, Args(cell, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other)));

    protected void Release(GridCoord cell) =>
        Tools.ActiveTool.PointerReleased(Context, Args(cell, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased)));

    private ViewportPointerEventArgs Args(GridCoord cell, PointerPointProperties properties, int clicks = 0)
    {
        var world = Map.CellCenter(cell);
        var screen = Context.ToScreen(world);
        var source = new PointerEventArgs(InputElement.PointerMovedEvent, null, Mouse, null, screen, 0, properties, KeyModifiers.None);
        return new ViewportPointerEventArgs(source, screen, world, properties, clicks);
    }
}

/// <summary>Isle Hopper's rectangular level in the Tile Mapping layout, with the brush over the ground.</summary>
internal sealed class TileMapSquareScene : TileMapScene
{
    public override string Name => "tilemap-square";

    protected override string Project => EditorFixture.IsleHopper;

    protected override string? MapEntity => "emerald-coast";

    protected override float Zoom => 0.9f;
}

/// <summary>A rectangle being dragged out with the stroke's tiles previewed with their artwork, and a selection on the map.</summary>
internal sealed class TileMapStrokeScene : TileMapScene
{
    public override string Name => "tilemap-stroke";

    protected override void Paint(Window window)
    {
        var map = Map.Map!;
        Map.ActiveLayer = map.TileLayers[^1];
        var tileset = map.Tilesets.First(t => t.TileCount > 2);
        Map.Brush.Pick(new TileCell(tileset.Id, Math.Min(3, tileset.TileCount - 1)));
        var center = CenterCell();
        var topology = map.Layout.Topology;
        var selection = new CellSet();
        GridShapes.Hexagon(topology, center + new GridCoord(-6, 1), 2, true, selection);
        Map.SetSelection(selection);
        Tools.Select("tile.rectangle");
        var start = topology.FromOffset(topology.ToOffset(center) + new GridCoord(1, -3));
        Press(start);
        Drag(topology.FromOffset(topology.ToOffset(start) + new GridCoord(3, 2)));
        Drag(topology.FromOffset(topology.ToOffset(start) + new GridCoord(5, 4)));
    }
}

/// <summary>The import dialog slicing a sample's tileset image, with Hexy Forge's settings pasted.</summary>
internal sealed class TilesetImportScene : TileMapScene
{
    public override string Name => "tilemap-import";

    protected override void Paint(Window window)
    {
        var map = Map.Map!;
        var source = map.Tilesets.First(t => t.ImageFile is not null);
        var file = Path.Combine(EditorFixture.Scratch, "terrain-sheet.png");
        using (var input = source.ImageFile!.Open())
        using (var output = File.Create(file))
            input.CopyTo(output);
        var viewModel = new TilesetImportDialogViewModel(Editor.Get<IProjectService>(), Editor.Get<IFileDialogService>(), map);
        _ = Editor.Get<TileMapDialogs>().ShowAsync(new TilesetImportDialogView(), viewModel);
        _ = viewModel.LoadFileAsync(file);
        RenderLoop.Wait(() => viewModel.HasImage, 10000);
        viewModel.Name = "Coast";
        viewModel.ApplyForgeSettings($"Tile size: {source.TileWidth} × {source.TileHeight}\nMargin: {source.Margin}\nSpacing: {source.Spacing}\nColumns: {source.Columns}");
    }
}

/// <summary>The terrain editor with a rule's neighbor pattern and weighted outputs.</summary>
internal sealed class TerrainEditorScene : TileMapScene
{
    public override string Name => "tilemap-terrain";

    protected override void Paint(Window window)
    {
        var map = Map.Map!;
        var tileset = map.Tilesets.First(t => t.TileCount > 4);
        var neighbors = map.Layout.Topology.NeighborCount;
        var terrain = map.Terrains.FirstOrDefault(t => t.TilesetId == tileset.Id) ?? new Terrain("Shore", tileset.Id, 0,
        [
            new AutoTileRule(new string('-', neighbors), [new WeightedTile(1)]),
            new AutoTileRule("+-" + new string('*', neighbors - 3) + "-", [new WeightedTile(2, 3), new WeightedTile(3)], matchRotations: true),
            new AutoTileRule("++" + new string('-', neighbors - 2), [new WeightedTile(4)], matchRotations: true)
        ]);
        var viewModel = new TerrainEditorDialogViewModel(tileset, terrain, map.Layout);
        viewModel.SelectedRule = viewModel.Rules.Count > 1 ? viewModel.Rules[1] : viewModel.Rules.FirstOrDefault();
        viewModel.PickModeIndex = 1;
        _ = Editor.Get<TileMapDialogs>().ShowAsync(new TerrainEditorDialogView(), viewModel);
    }
}
