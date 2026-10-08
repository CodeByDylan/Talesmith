using Avalonia.Input;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.TileMaps.Controls;
using Talesmith.Editor.TileMaps.Panel;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Grids;

namespace Talesmith.Editor.Tests.TileMaps;

public sealed class TileToolTests
{
    public static TheoryData<string> Templates => ["platformer", "hex-adventure"];

    [Theory]
    [MemberData(nameof(Templates))]
    public void ABrushStrokeFillsTheCellsBetweenPointerSamplesAsOneUndoStep(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var line = new List<GridCoord>();
        harness.Map.Layout.Topology.Line(new GridCoord(-20, -30), new GridCoord(-12, -30), line);
        var before = harness.TilesAt(line);
        var steps = harness.Undo.UndoSteps.Count;
        harness.Editor.Brush.Pick(harness.Tile(1));

        harness.Drag(harness.Tool<BrushTool>(), [line[0], line[^1]]);

        Assert.All(harness.TilesAt(line), tile => Assert.Equal(harness.Tile(1), tile));
        Assert.Equal(steps + 1, harness.Undo.UndoSteps.Count);
        Assert.Equal($"Paint {line.Count} tiles", harness.Undo.UndoSteps[^1].Description);
        Assert.True(harness.Editor.Maps.IsDirty(harness.Map));
        Assert.True(harness.Fixture.Document.IsDirty);

        harness.Undo.Undo();
        Assert.Equal(before, harness.TilesAt(line));
        Assert.False(harness.Editor.Maps.IsDirty(harness.Map));
        harness.Undo.Redo();
        Assert.All(harness.TilesAt(line), tile => Assert.Equal(harness.Tile(1), tile));
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void RightDragErasesAndTheEraserClearsTheBrushFootprint(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var cell = new GridCoord(-40, -40);
        harness.Editor.Brush.Pick(harness.Tile(2));
        harness.Editor.Brush.Size = 2;
        harness.Click(harness.Tool<BrushTool>(), cell);
        var footprint = new List<GridCoord>();
        new GridBrush(BrushShape.Range, 2).Footprint(harness.Map.Layout, cell, footprint);
        Assert.All(harness.TilesAt(footprint), tile => Assert.Equal(harness.Tile(2), tile));

        harness.Editor.Brush.Size = 1;
        harness.Click(harness.Tool<BrushTool>(), cell, MouseButton.Right);
        Assert.True(harness.Layer.GetCell(cell).IsEmpty);
        Assert.Equal(harness.Tile(2), harness.Layer.GetCell(footprint[^1] == cell ? footprint[0] : footprint[^1]));

        harness.Editor.Brush.Size = 2;
        harness.Click(harness.Tool<EraserTool>(), cell);
        Assert.All(harness.TilesAt(footprint), tile => Assert.True(tile.IsEmpty));
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void ShapeToolsPaintTheirShapeOnRelease(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        var (start, end) = (new GridCoord(-60, -60), new GridCoord(-54, -55));
        harness.Editor.Brush.Pick(harness.Tile(1));

        harness.Editor.Brush.IsShapeFilled = false;
        harness.Drag(harness.Tool<RectangleTool>(), [start, new GridCoord(-58, -58), end]);
        var outline = new CellSet();
        GridShapes.Rectangle(map.Layout.Topology, start, end, false, outline);
        Assert.All(harness.TilesAt(outline), tile => Assert.Equal(harness.Tile(1), tile));
        var inside = new CellSet();
        GridShapes.Rectangle(map.Layout.Topology, start, end, true, inside);
        inside.ExceptWith(outline);
        Assert.All(harness.TilesAt(inside), tile => Assert.True(tile.IsEmpty));
        harness.Undo.Undo();
        Assert.All(harness.TilesAt(outline), tile => Assert.True(tile.IsEmpty));

        harness.Editor.Brush.IsShapeFilled = true;
        harness.Editor.Brush.IsRangeShape = true;
        var center = new GridCoord(-80, -80);
        harness.Drag(harness.Tool<CircleTool>(), [center, new GridCoord(-77, -80)]);
        var range = new List<GridCoord>();
        map.Layout.Topology.Range(center, 3, range);
        Assert.All(harness.TilesAt(range), tile => Assert.Equal(harness.Tile(1), tile));

        harness.Drag(harness.Tool<LineTool>(), [new GridCoord(-90, -70), new GridCoord(-84, -70)], MouseButton.Right);
        var erased = new List<GridCoord>();
        map.Layout.Topology.Line(new GridCoord(-90, -70), new GridCoord(-84, -70), erased);
        Assert.All(harness.TilesAt(erased), tile => Assert.True(tile.IsEmpty));
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void FillReplacesTheConnectedRegionWithinItsBorder(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        var ring = new List<GridCoord>();
        map.Layout.Topology.Ring(new GridCoord(-100, -100), 3, ring);
        var border = harness.Editor.BeginEdit();
        border.Fill(harness.Layer, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ring), harness.Tile(0));
        harness.Editor.Commit(border, "Border {0}");
        harness.Editor.Brush.Pick(harness.Tile(2));
        var steps = harness.Undo.UndoSteps.Count;

        harness.Click(harness.Tool<FillTool>(), new GridCoord(-100, -100));
        await harness.WaitAsync(() => harness.Undo.UndoSteps.Count == steps + 1);

        var inside = new List<GridCoord>();
        map.Layout.Topology.Range(new GridCoord(-100, -100), 2, inside);
        Assert.All(harness.TilesAt(inside), tile => Assert.Equal(harness.Tile(2), tile));
        Assert.All(harness.TilesAt(ring), tile => Assert.Equal(harness.Tile(0), tile));
        Assert.True(harness.Layer.GetCell(new GridCoord(-110, -110)).IsEmpty);
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void ThePickerCopiesTheTileWithItsOrientationAndReturnsToThePreviousTool(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var steps = harness.Map.Layout.RotationSteps;
        var turned = harness.Tile(2).WithTransform(1, true, steps);
        harness.Layer.SetCell(new GridCoord(-5, -50), turned);
        var brush = harness.Tool<BrushTool>();
        harness.Tools.ActiveTool = brush;

        harness.Click(harness.Tool<PickerTool>(), new GridCoord(-5, -50));

        Assert.Same(brush, harness.Tools.ActiveTool);
        Assert.Equal(turned, harness.Editor.Brush.PrimaryTileFor(steps));

        harness.Editor.Brush.Pick(harness.Tile(0));
        harness.Press(new GridCoord(-5, -50), modifiers: KeyModifiers.Alt);
        Assert.Equal(turned, harness.Editor.Brush.PrimaryTileFor(steps));
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void OrientationKeysTurnTheBrushAndTheStamp(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var steps = harness.Map.Layout.RotationSteps;
        var topology = harness.Map.Layout.Topology;
        harness.Editor.Brush.Pick(harness.Tile(1));
        harness.Tools.ActiveTool = harness.Tool<BrushTool>();

        Assert.True(harness.Key(Key.Z));
        Assert.Equal(1, harness.Editor.Brush.Rotation);
        Assert.True(harness.Key(Key.Z, KeyModifiers.Shift));
        Assert.True(harness.Key(Key.Z, KeyModifiers.Shift));
        Assert.Equal(steps - 1, harness.Editor.Brush.Rotation);
        Assert.True(harness.Key(Key.X));
        Assert.Equal(harness.Tile(1).WithTransform(steps - 1, false, steps).FlipHorizontal(steps), harness.Editor.Brush.PrimaryTileFor(steps));

        var stamp = new TileStamp([new PlacedTile(GridCoord.Zero, harness.Tile(0)), new PlacedTile(topology.Direction(0), harness.Tile(1))]);
        harness.Editor.Brush.Stamp = stamp;
        harness.Tools.ActiveTool = harness.Tool<StampTool>();
        Assert.True(harness.Key(Key.Z));
        var target = new GridCoord(-30, -70);
        harness.Click(harness.Tool<StampTool>(), target);

        var rotated = stamp.Rotate(1, topology);
        foreach (var placed in rotated.Cells)
            Assert.Equal(placed.Tile, harness.Layer.GetCell(target + placed.Cell));
        Assert.Equal(harness.Tile(1).Rotate(1, steps), harness.Layer.GetCell(target + topology.Rotate(topology.Direction(0), 1)));
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void SelectedTilesCopyPasteAndMove(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var topology = harness.Map.Layout.Topology;
        var source = new CellSet();
        GridShapes.Rectangle(topology, new GridCoord(-50, -90), new GridCoord(-47, -88), true, source);
        var edit = harness.Editor.BeginEdit();
        foreach (var cell in source)
            edit.Set(harness.Layer, cell, harness.Tile((cell.X + cell.Y) & 1));
        harness.Editor.Commit(edit, "Seed {0}");
        var select = harness.Tool<TileSelectTool>();

        harness.Drag(select, [new GridCoord(-50, -90), new GridCoord(-49, -89), new GridCoord(-47, -88)]);
        Assert.Equal(source.Count, harness.Editor.Selection.Count);

        Assert.True(harness.Key(Key.C, KeyModifiers.Control));
        Assert.Equal(source.Count, harness.Editor.Brush.Stamp!.Count);
        Assert.True(harness.Key(Key.V, KeyModifiers.Control));
        Assert.IsType<StampTool>(harness.Tools.ActiveTool);
        var target = new GridCoord(-30, -120);
        harness.Click(harness.Tools.ActiveTool, target);
        foreach (var placed in harness.Editor.Brush.Stamp.Cells)
            Assert.Equal(placed.Tile, harness.Layer.GetCell(target + placed.Cell));

        var original = source.ToDictionary(c => c, harness.Layer.GetCell);
        var delta = new GridCoord(0, 4);
        harness.Tools.ActiveTool = select;
        harness.Press(new GridCoord(-49, -89));
        harness.Move(new GridCoord(-49, -87));
        harness.Move(new GridCoord(-49, -85));
        harness.Release(new GridCoord(-49, -85));
        foreach (var (cell, tile) in original)
        {
            Assert.Equal(tile, harness.Layer.GetCell(cell + delta));
            if (!source.Contains(cell - delta))
                Assert.True(harness.Layer.GetCell(cell).IsEmpty);
        }

        Assert.Contains(new GridCoord(-49, -85), harness.Editor.Selection);
        Assert.StartsWith("Move ", harness.Undo.UndoSteps[^1].Description, StringComparison.Ordinal);
        harness.Undo.Undo();
        foreach (var (cell, tile) in original)
            Assert.Equal(tile, harness.Layer.GetCell(cell));

        Assert.True(harness.Key(Key.Delete));
        Assert.All(harness.TilesAt(harness.Editor.Selection), tile => Assert.True(tile.IsEmpty));
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void TerrainPaintingResolvesTransitionsThroughTheAutoTiler(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        var topology = map.Layout.Topology;
        var lonely = new string('-', topology.NeighborCount);
        var edge = "+" + new string('-', topology.NeighborCount - 1);
        var terrain = new Terrain("Sand", map.Tilesets[0].Id, 0, [new AutoTileRule(lonely, [new WeightedTile(1)]), new AutoTileRule(edge, [new WeightedTile(2)], true)]);
        harness.Editor.Execute("Add terrain", MapEdits.InsertTerrain(terrain, map.Terrains.Count));
        harness.Editor.Brush.Terrain = terrain;
        var a = new GridCoord(-70, -140);
        var b = a + topology.Direction(0);

        harness.Click(harness.Tool<TerrainTool>(), a);
        Assert.Equal(harness.Tile(1), harness.Layer.GetCell(a));

        harness.Click(harness.Tool<TerrainTool>(), b);
        var expected = AutoTiler.Resolve(terrain, topology, a, harness.Layer.GetCell);
        Assert.Equal(expected, harness.Layer.GetCell(a));
        Assert.Equal(2, harness.Layer.GetCell(a).TileId);
        Assert.Equal(2, harness.Layer.GetCell(b).TileId);

        harness.Undo.Undo();
        Assert.Equal(harness.Tile(1), harness.Layer.GetCell(a));
        Assert.True(harness.Layer.GetCell(b).IsEmpty);
    });

    [Fact]
    public void TheCollisionToolAddsACollisionLayerAndPaintsSolidCellsInOneUndoStep() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var layers = harness.Map.Layers.Count;
        var tilesets = harness.Map.Tilesets.Count;
        var steps = harness.Undo.UndoSteps.Count;

        harness.Click(harness.Tool<CollisionTool>(), new GridCoord(3, 3));

        Assert.Equal(steps + 1, harness.Undo.UndoSteps.Count);
        var collision = Assert.Single(harness.Map.TileLayers, l => l.Role == LayerRole.Collision);
        Assert.Equal(layers + 1, harness.Map.Layers.Count);
        Assert.Same(collision, harness.Editor.ActiveLayer);
        var solid = collision.GetCell(new GridCoord(3, 3));
        Assert.Equal(CollisionTool.SolidTileName, harness.Map.FindTileset(solid.TilesetId)!.Find(solid.TileId)!.Name);
        harness.Undo.Undo();
        Assert.True(collision.GetCell(new GridCoord(3, 3)).IsEmpty);
        Assert.Equal(layers, harness.Map.Layers.Count);
        Assert.Equal(tilesets, harness.Map.Tilesets.Count);
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void ObjectsArePlacedMovedAndDeleted(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        harness.Editor.Execute("Add objects", MapEdits.InsertLayer(new ObjectLayer("Spawns"), map.Layers.Count));
        harness.Editor.ActiveLayer = map.ObjectLayers[^1];
        var objects = map.ObjectLayers[^1];
        var tool = harness.Tool<ObjectTool>();

        harness.Editor.Brush.ObjectMode = ObjectToolMode.Point;
        harness.Click(tool, new GridCoord(2, 2));
        var point = Assert.Single(objects.Objects);
        Assert.Equal(map.Layout.CellToWorld(new GridCoord(2, 2)), point.Position);
        Assert.Equal(new GridCoord(2, 2), point.Cell);

        harness.Editor.Brush.ObjectMode = ObjectToolMode.Select;
        harness.Drag(tool, [new GridCoord(2, 2), new GridCoord(3, 2), new GridCoord(5, 4)]);
        Assert.Equal(new GridCoord(5, 4), objects.Objects[0].Cell);
        harness.Undo.Undo();
        Assert.Equal(new GridCoord(2, 2), objects.Objects[0].Cell);

        harness.Editor.Brush.ObjectMode = ObjectToolMode.Polygon;
        harness.Tools.ActiveTool = tool;
        foreach (var corner in (GridCoord[])[new(10, 10), new(14, 10), new(12, 13)])
        {
            harness.Press(corner);
            harness.Release(corner);
        }

        Assert.True(harness.Key(Key.Enter));
        var area = objects.Objects[^1];
        Assert.Equal(MapObjectShape.Polygon, area.Shape);
        Assert.Equal(3, area.Polygon.Count);

        harness.Editor.Brush.ObjectMode = ObjectToolMode.Select;
        harness.Click(tool, new GridCoord(2, 2));
        Assert.Equal(point.Id, harness.Editor.SelectedObject?.Id);
        Assert.True(harness.Key(Key.Delete));
        Assert.DoesNotContain(objects.Objects, o => o.Id == point.Id);
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheObjectToolPlacesATilePickedInThePaletteAsAnImageObject(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        harness.Editor.Execute("Add objects", MapEdits.InsertLayer(new ObjectLayer("Props"), map.Layers.Count));
        harness.Editor.ActiveLayer = map.ObjectLayers[^1];
        var objects = map.ObjectLayers[^1];
        var tool = harness.Tool<ObjectTool>();
        var tilesets = harness.Fixture.Get<TileMapPanel>().ViewModel.Tilesets;
        tilesets.SelectedTileset = tilesets.Tilesets[0];

        harness.Tools.ActiveTool = tool;
        tilesets.Pick(new TilesPickedEventArgs([1], isToggle: false, isRectangle: false, columns: 1));
        Assert.Same(tool, harness.Tools.ActiveTool);
        Assert.Equal(ObjectToolMode.Tile, harness.Editor.Brush.ObjectMode);

        harness.Click(tool, new GridCoord(4, 3));
        var image = Assert.Single(objects.Objects);
        Assert.Equal(MapObjectShape.Tile, image.Shape);
        Assert.Equal(harness.Tile(1), image.Tile);
        Assert.Equal(new GridCoord(4, 3), image.Cell);
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void TheObjectToolPlacesTheBrushTileWhenActivatedAfterPickingIt(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        harness.Editor.Execute("Add objects", MapEdits.InsertLayer(new ObjectLayer("Props"), map.Layers.Count));
        var objects = map.ObjectLayers[^1];
        harness.Tools.ActiveTool = harness.Tool<BrushTool>();
        harness.Editor.Brush.Pick(harness.Tile(2));
        harness.Editor.ActiveLayer = objects;

        harness.Click(harness.Tool<ObjectTool>(), new GridCoord(4, 3));

        var image = Assert.Single(objects.Objects);
        Assert.Equal(MapObjectShape.Tile, image.Shape);
        Assert.Equal(harness.Tile(2), image.Tile);
    });

    [Fact]
    public void TheObjectToolKeepsAChosenModeWhenActivatedAgain() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        harness.Editor.Brush.Pick(harness.Tile(2));
        harness.Editor.Brush.ObjectMode = ObjectToolMode.Point;

        harness.Tools.ActiveTool = harness.Tool<BrushTool>();
        harness.Tools.ActiveTool = harness.Tool<ObjectTool>();

        Assert.Equal(ObjectToolMode.Point, harness.Editor.Brush.ObjectMode);
    });

    [Theory]
    [MemberData(nameof(Templates))]
    public void PlacingTilesSelectsAndMovesAnObjectClickedInsteadOfStackingAnother(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        var map = harness.Map;
        harness.Editor.Execute("Add objects", MapEdits.InsertLayer(new ObjectLayer("Props"), map.Layers.Count));
        harness.Editor.ActiveLayer = map.ObjectLayers[^1];
        var objects = map.ObjectLayers[^1];
        var tool = harness.Tool<ObjectTool>();
        harness.Editor.Brush.Pick(harness.Tile(1));
        harness.Editor.Brush.ObjectMode = ObjectToolMode.Tile;
        harness.Click(tool, new GridCoord(2, 2));
        var placed = Assert.Single(objects.Objects);
        harness.Editor.SelectedObject = null;

        harness.Drag(tool, [new GridCoord(2, 2), new GridCoord(3, 2), new GridCoord(5, 4)]);

        var moved = Assert.Single(objects.Objects);
        Assert.Equal(placed.Id, moved.Id);
        Assert.Equal(new GridCoord(5, 4), moved.Cell);
        Assert.Equal(placed.Id, harness.Editor.SelectedObject?.Id);
    });
}
