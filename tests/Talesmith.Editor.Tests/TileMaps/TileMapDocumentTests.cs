using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.TileMaps.Panel;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Grids;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Tests.TileMaps;

public sealed class TileMapDocumentTests
{
    [Fact]
    public void LayerOperationsUndoAndRedo() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("hex-adventure");
        var map = harness.Map;
        var layers = new LayersViewModel(harness.Editor, harness.Fixture.Get<IDialogService>());
        var count = map.Layers.Count;

        layers.AddTileLayerCommand.Execute(null);
        var added = Assert.IsType<TileLayer>(harness.Editor.ActiveLayer);
        Assert.Equal(count + 1, map.Layers.Count);
        Assert.Same(added, layers.Items[0].Layer);

        layers.Items[0].Name = "Rocks";
        layers.Items[0].IsVisible = false;
        layers.Items[0].SetRoleCommand.Execute(LayerRoleOption.Of(LayerRole.Decoration));
        Assert.Equal(("Rocks", false, LayerRole.Decoration), (added.Name, added.IsVisible, added.Role));

        layers.MoveToRow(layers.Items[0], layers.Items.Count - 1);
        Assert.Equal(0, map.IndexOf(added));

        layers.AddObjectLayerCommand.Execute(null);
        Assert.IsType<ObjectLayer>(map.Layers[1]);

        for (var i = 0; i < 6; i++)
            harness.Undo.Undo();
        Assert.Equal(count, map.Layers.Count);
        Assert.DoesNotContain(added, map.Layers);
        for (var i = 0; i < 6; i++)
            harness.Undo.Redo();
        Assert.Equal(count + 2, map.Layers.Count);
        Assert.Equal(0, map.IndexOf(added));
        Assert.Equal("Rocks", added.Name);
        Assert.Equal(LayerRole.Decoration, layers.Items[^1].Role.Role);
    });

    [Theory]
    [InlineData("platformer")]
    [InlineData("hex-adventure")]
    public void SavingTheSceneWritesTheMapAsAHexyFileThatLoadsEqual(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        harness.Editor.Brush.Pick(harness.Tile(1).WithTransform(1, true, map.Layout.RotationSteps));
        harness.Drag(harness.Tool<BrushTool>(), [new GridCoord(-8, -8), new GridCoord(-2, -8)]);
        harness.Editor.Execute("Add layer", Talesmith.Assets.Maps.Editing.MapEdits.InsertLayer(new ObjectLayer("Spawns"), map.Layers.Count));
        Assert.True(harness.Editor.Maps.IsDirty(map));

        Assert.True(await harness.Fixture.Get<ISceneDocumentService>().SaveAsync());
        await harness.WaitAsync(() => !harness.Editor.Maps.IsDirty(map));
        Assert.False(harness.Fixture.Document.IsDirty);

        var project = harness.Fixture.Get<IProjectService>().Project;
        var assets = new AssetManager(new FileSystemAssetSource(project.AssetRoot), [new HexyMapImporter()], NullLogger<AssetManager>.Instance);
        var reloaded = await assets.LoadAsync<TileMap>(map.Path);
        Assert.Equal(map.Layout.Kind, reloaded.Layout.Kind);
        Assert.Equal(map.Layers.Select(l => (l.Name, l.Role, l.GetType())), reloaded.Layers.Select(l => (l.Name, l.Role, l.GetType())));
        for (var i = 0; i < map.TileLayers.Count; i++)
        {
            var expected = new Dictionary<GridCoord, TileCell>();
            foreach (var placed in map.TileLayers[i].Cells)
                expected[placed.Cell] = placed.Tile;
            var actual = new Dictionary<GridCoord, TileCell>();
            foreach (var placed in reloaded.TileLayers[i].Cells)
                actual[placed.Cell] = placed.Tile;
            Assert.Equal(expected, actual);
        }
    });

    [Fact]
    public void HotReloadKeepsTheEditedMapAfterTheEditorWroteIt() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var map = harness.Map;
        var project = harness.Fixture.Get<IProjectService>();
        harness.Editor.Brush.Pick(harness.Tile(1));
        harness.Click(harness.Tool<BrushTool>(), new GridCoord(-3, -3));

        Assert.True(await harness.Editor.Maps.SaveAsync(map));
        await project.Database!.RefreshAsync([map.Path]);
        await project.HotReload!.Idle;

        var assets = project.EditSession!.Game.Services.GetRequiredService<IAssetManager>();
        Assert.True(assets.TryGet<TileMap>(map.Path, out var loaded));
        Assert.Same(map, loaded);
        Assert.Same(map, harness.Editor.Map);
    });

    [Fact]
    public void MapsWithUnsavedEditsAreNotReplacedByChangesOnDisk() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var map = harness.Map;
        var project = harness.Fixture.Get<IProjectService>();
        harness.Editor.Brush.Pick(harness.Tile(1));
        harness.Click(harness.Tool<BrushTool>(), new GridCoord(-3, -3));

        var file = project.Project.ToAbsolutePath(map.Path);
        var copy = TileMap.Create(GridKind.Square, 32, 32);
        await HexyMapWriter.SaveAsync(copy, file);
        await project.Database!.RefreshAsync([map.Path]);
        await project.HotReload!.Idle;

        var assets = project.EditSession!.Game.Services.GetRequiredService<IAssetManager>();
        Assert.True(assets.TryGet<TileMap>(map.Path, out var loaded));
        Assert.Same(map, loaded);
        Assert.True(harness.Editor.Maps.IsDirty(map));
    });

    [Fact]
    public void ANewMapIsWrittenAndShownByANewEntity() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var dialogs = harness.Fixture.Get<Editor.TileMaps.Dialogs.TileMapDialogs>();
        var options = new Editor.TileMaps.Dialogs.NewMapOptions("maps/caves.hexy", GridKind.HexFlatTop, 96, 83, 5, IncludePalette: true);

        var entity = await dialogs.CreateMapAsync(options);

        Assert.NotNull(entity);
        Assert.True(File.Exists(harness.Fixture.Get<IProjectService>().Project.ToAbsolutePath("maps/caves.hexy")));
        await harness.WaitAsync(() => harness.Editor.Target?.EntityId == entity);
        Assert.Equal(GridKind.HexFlatTop, harness.Editor.Map!.Layout.Kind);
        Assert.Equal(16, harness.Editor.Map.Tilesets[0].TileCount);
        Assert.Equal("Ground", harness.Editor.ActiveLayer!.Name);
    });
}
