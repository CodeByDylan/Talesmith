using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Grids;

namespace Talesmith.Assets.Tests.Maps;

/// <summary>The algorithms behind stamps, fills, the picker, random brushes and selections, on every grid.</summary>
public sealed class TileToolTests
{
    private static FloodFillResult Contiguous(TileMap map, TileLayer layer, GridCoord start, FillMatch match, int limit, CellSet output, CellSet? within = null) =>
        TileFill.Contiguous(map, layer, start, match, limit, output, within, TestContext.Current.CancellationToken);

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void CapturedStampsAreCenteredAndSkipEmptyCells(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var layer = map.Ground();
        layer.SetCell(new GridCoord(10, 10), TestMaps.Tile(1));
        layer.SetCell(new GridCoord(12, 10), TestMaps.Tile(2));
        var selection = new CellSet();
        GridShapes.Rectangle(map.Layout.Topology, new GridCoord(10, 10), new GridCoord(12, 10), filled: true, selection);

        var stamp = TileStamp.Capture(layer, selection, map.Layout.Topology);

        Assert.Equal(
            [new PlacedTile(new GridCoord(-1, 0), TestMaps.Tile(1)), new PlacedTile(new GridCoord(1, 0), TestMaps.Tile(2))],
            stamp.Cells.OrderBy(c => c.Cell.X));
    }

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void RotatingAStampRotatesItsPictureAndItsTiles(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var topology = map.Layout.Topology;
        var stamp = new TileStamp([new PlacedTile(new GridCoord(0, 0), TestMaps.Tile(1)), new PlacedTile(new GridCoord(2, -1), TestMaps.Tile(2, rotation: 1))]);

        var once = stamp.Rotate(1, topology);
        var full = stamp;
        for (var i = 0; i < topology.RotationSteps; i++)
            full = full.Rotate(1, topology);

        Assert.Equal(stamp.Cells, full.Cells);
        Assert.Equal(new PlacedTile(topology.Rotate(new GridCoord(2, -1), 1), TestMaps.Tile(2, rotation: 2)), once.Cells[1]);
        Assert.Equal(TestMaps.Tile(1, rotation: topology.RotationSteps - 1), stamp.Rotate(-1, topology).Cells[0].Tile);
    }

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void FlippingAStampMirrorsItsPictureAndItsTiles(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var topology = map.Layout.Topology;
        var stamp = new TileStamp([new PlacedTile(new GridCoord(3, -1), TestMaps.Tile(4, rotation: 1))]);

        var horizontal = stamp.FlipHorizontal(topology);
        var vertical = stamp.FlipVertical(topology);

        var world = map.Layout.CellToWorld(new GridCoord(3, -1));
        var mirrored = map.Layout.CellToWorld(horizontal.Cells[0].Cell);
        Assert.Equal(-world.X, mirrored.X, 0.01f);
        Assert.Equal(world.Y, mirrored.Y, 0.01f);
        Assert.Equal(TestMaps.Tile(4, rotation: topology.RotationSteps - 1, flip: true), horizontal.Cells[0].Tile);
        Assert.Equal(TestMaps.Tile(4, rotation: topology.RotationSteps / 2 - 1, flip: true), vertical.Cells[0].Tile);
        Assert.Equal(stamp.Cells, horizontal.FlipHorizontal(topology).Cells);
        Assert.Equal(stamp.Cells, vertical.FlipVertical(topology).Cells);
    }

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void ContiguousFillsStopAtOtherTiles(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var layer = map.Ground();
        var walls = new CellSet();
        map.Layout.Topology.Ring(GridCoord.Zero, 3, walls);
        foreach (var cell in walls)
            layer.SetCell(cell, TestMaps.Tile(9));
        var region = new CellSet();

        var result = Contiguous(map, layer, GridCoord.Zero, FillMatch.SameTile, TileFill.DefaultLimit, region);

        var inside = new CellSet();
        map.Layout.Topology.Range(GridCoord.Zero, 2, inside);
        Assert.False(result.ReachedLimit);
        Assert.Equal(inside.Sorted(), region.Sorted());
    }

    [Fact]
    public void FillsOfEmptySpaceStopAtTheLimit()
    {
        var map = TestMaps.Create();
        var region = new CellSet();

        var result = Contiguous(map, map.Ground(), GridCoord.Zero, FillMatch.SameTile, 1000, region);

        Assert.True(result.ReachedLimit);
    }

    [Fact]
    public void FillsCanIgnoreOrientationOrFollowTerrains()
    {
        var map = TestMaps.Create(GridKind.Square);
        var layer = map.Ground();
        map.AddTerrain(new Terrain("Grass", 1, 4, [new AutoTileRule("****", [new WeightedTile(5)])]));
        for (var x = 0; x < 6; x++)
            layer.SetCell(new GridCoord(x, 0), x < 3 ? TestMaps.Tile(4, rotation: x) : TestMaps.Tile(5));
        layer.SetCell(new GridCoord(6, 0), TestMaps.Tile(7));

        var exact = new CellSet();
        var anyOrientation = new CellSet();
        var terrain = new CellSet();
        Contiguous(map, layer, GridCoord.Zero, FillMatch.SameTile, 100, exact);
        Contiguous(map, layer, GridCoord.Zero, FillMatch.SameTileAnyOrientation, 100, anyOrientation);
        Contiguous(map, layer, GridCoord.Zero, FillMatch.SameTerrain, 100, terrain);

        Assert.Single(exact);
        Assert.Equal(3, anyOrientation.Count);
        Assert.Equal(6, terrain.Count);
    }

    [Fact]
    public void FillsStayInsideTheSelection()
    {
        var map = TestMaps.Create(GridKind.Square);
        var selection = new CellSet();
        GridShapes.Rectangle(GridTopology.Square, new GridCoord(-2, -2), new GridCoord(2, 2), filled: true, selection);
        var region = new CellSet();

        var result = Contiguous(map, map.Ground(), GridCoord.Zero, FillMatch.SameTile, 1000, region, selection);

        Assert.Equal(new FloodFillResult(25, false), result);
        Assert.Equal(default, Contiguous(map, map.Ground(), new GridCoord(9, 9), FillMatch.SameTile, 1000, new CellSet(), selection));
    }

    [Fact]
    public void GlobalFillsFindEveryMatchingCell()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        layer.SetCell(new GridCoord(0, 0), TestMaps.Tile(1));
        layer.SetCell(new GridCoord(90, -40), TestMaps.Tile(1));
        layer.SetCell(new GridCoord(5, 5), TestMaps.Tile(2));
        var region = new CellSet();

        var result = TileFill.Global(map, layer, TestMaps.Tile(1), FillMatch.SameTile, 1000, region);

        Assert.Equal(new FloodFillResult(2, false), result);
        Assert.Equal([new GridCoord(90, -40), new GridCoord(0, 0)], region.Sorted());
        Assert.True(TileFill.Global(map, layer, TileCell.Empty, FillMatch.SameTile, 1000, new CellSet()).ReachedLimit);
    }

    [Fact]
    public void ThePickerPrefersTheActiveLayerThenTheTopmostVisibleTile()
    {
        var map = TestMaps.Create();
        var ground = map.Ground();
        var top = map.CreateTileLayer("Top");
        map.AddLayer(top);
        ground.SetCell(GridCoord.Zero, TestMaps.Tile(1));
        top.SetCell(GridCoord.Zero, TestMaps.Tile(2, rotation: 3));

        Assert.Equal(new PickedTile(top, TestMaps.Tile(2, rotation: 3)), TilePicker.Pick(map, GridCoord.Zero));
        Assert.Equal(new PickedTile(ground, TestMaps.Tile(1)), TilePicker.Pick(map, GridCoord.Zero, ground));
        top.IsVisible = false;
        Assert.Equal(ground, TilePicker.Pick(map, GridCoord.Zero)!.Value.Layer);
        Assert.Null(TilePicker.Pick(map, new GridCoord(4, 4)));
    }

    [Fact]
    public void RandomBrushesFollowTheirWeights()
    {
        var brush = TileBrush.Random([new TileChoice(TestMaps.Tile(1), 3), new TileChoice(TestMaps.Tile(2), 1), new TileChoice(TestMaps.Tile(3), 0)]);
        var counts = new Dictionary<TileCell, int>();

        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var tile = brush.TileFor(new GridCoord(x, y));
                counts[tile] = counts.GetValueOrDefault(tile) + 1;
                Assert.Equal(tile, brush.TileFor(new GridCoord(x, y)));
            }
        }

        Assert.False(counts.ContainsKey(TestMaps.Tile(3)));
        Assert.InRange(counts[TestMaps.Tile(1)] / (double)counts[TestMaps.Tile(2)], 2.5, 3.5);
        Assert.Contains(Enumerable.Range(0, 50), i => brush.TileFor(new GridCoord(i, 0)) != brush.WithSeed(99).TileFor(new GridCoord(i, 0)));
    }

    [Fact]
    public void BrushOrientationAppliesToEveryTile()
    {
        var brush = TileBrush.Random([new TileChoice(TestMaps.Tile(1)), new TileChoice(TestMaps.Tile(2))]).WithTransform(2, true, 6);

        Assert.All(brush.Choices, choice => Assert.Equal((2, true), (choice.Tile.Rotation, choice.Tile.FlipX)));
        Assert.True(TileBrush.Single(TileCell.Empty).IsEraser);
        Assert.Equal(TileCell.Empty, TileBrush.Eraser.TileFor(GridCoord.Zero));
    }

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void CutAndPasteMoveTilesAndUndoInOneStep(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var layer = map.Ground();
        layer.SetCell(new GridCoord(0, 0), TestMaps.Tile(1));
        layer.SetCell(new GridCoord(1, 0), TestMaps.Tile(2));
        var before = TestMaps.Cells(layer);
        var selection = new CellSet([new GridCoord(0, 0), new GridCoord(1, 0), new GridCoord(0, 1)]);

        var edit = new TileEdit(map);
        var stamp = TileClipboard.Cut(edit, layer, selection);
        TileClipboard.Paste(edit, layer, stamp, new GridCoord(20, 20));
        var redo = edit.Commit()!.Invert().Apply(map);

        Assert.Equal(before, TestMaps.Cells(layer));
        redo.Apply(map);
        Assert.Equal(2, TestMaps.Cells(layer).Length);
        Assert.All(TestMaps.Cells(layer), placed => Assert.True(placed.Cell.X >= 19));
    }

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void MovingASelectionCarriesItsTilesAndOverlapsCleanly(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var layer = map.Ground();
        for (var x = 0; x < 4; x++)
            layer.SetCell(new GridCoord(x, 0), TestMaps.Tile(x + 1));
        var selection = new CellSet([new GridCoord(0, 0), new GridCoord(1, 0), new GridCoord(2, 0)]);
        var edit = new TileEdit(map);

        var moved = TileClipboard.Move(edit, layer, selection, new GridCoord(1, 0));

        Assert.Equal([new GridCoord(1, 0), new GridCoord(2, 0), new GridCoord(3, 0)], moved.Sorted());
        Assert.Equal(
            [new PlacedTile(new GridCoord(1, 0), TestMaps.Tile(1)), new PlacedTile(new GridCoord(2, 0), TestMaps.Tile(2)), new PlacedTile(new GridCoord(3, 0), TestMaps.Tile(3))],
            TestMaps.Cells(layer));
    }

    [Fact]
    public void LassoSelectionsCombineWithModes()
    {
        var map = TestMaps.Create(GridKind.Square);
        var selection = new CellSet();
        var lasso = new CellSet();
        GridShapes.Polygon(map.Layout, [new Vector2(-40, -40), new Vector2(100, -40), new Vector2(100, 40), new Vector2(-40, 40)], lasso);
        var rectangle = new CellSet();
        GridShapes.Rectangle(GridTopology.Square, new GridCoord(1, 0), new GridCoord(5, 0), filled: true, rectangle);

        selection.Apply(lasso, SelectionMode.Replace);
        selection.Apply(rectangle, SelectionMode.Subtract);

        Assert.Equal([new GridCoord(0, 0)], selection.ToArray());
    }
}
