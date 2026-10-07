using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Grids;

namespace Talesmith.Assets.Tests.Maps;

public sealed class TileEditTests
{
    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void CommitReturnsTheMinimalChangesWithTheirOriginalValues(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var layer = map.Ground();
        layer.SetCell(new GridCoord(1, 1), TestMaps.Tile(9));
        var edit = new TileEdit(map);

        edit.Set(layer, new GridCoord(0, 0), TestMaps.Tile(1));
        edit.Set(layer, new GridCoord(0, 0), TestMaps.Tile(2));
        edit.Set(layer, new GridCoord(1, 1), TestMaps.Tile(3));
        edit.Set(layer, new GridCoord(2, 2), TestMaps.Tile(4));
        edit.Set(layer, new GridCoord(2, 2), TileCell.Empty);
        edit.Set(layer, new GridCoord(5, 5), TileCell.Empty);
        var changes = edit.Commit();

        Assert.NotNull(changes);
        var layerChanges = Assert.Single(changes.Layers);
        Assert.Same(layer, layerChanges.Layer);
        Assert.Equal(
            [new CellChange(new GridCoord(0, 0), TileCell.Empty, TestMaps.Tile(2)), new CellChange(new GridCoord(1, 1), TestMaps.Tile(9), TestMaps.Tile(3))],
            layerChanges.Changes.OrderBy(c => c.Cell.X));
        Assert.Equal(TestMaps.Tile(2), layer.GetCell(new GridCoord(0, 0)));
    }

    [Theory]
    [MemberData(nameof(TestMaps.Kinds), MemberType = typeof(TestMaps))]
    public void ApplyingTheInverseUndoesAndApplyingItsInverseRedoes(GridKind kind)
    {
        var map = TestMaps.Create(kind);
        var ground = map.Ground();
        var details = map.CreateTileLayer("Details");
        map.AddLayer(details);
        ground.SetCell(new GridCoord(-4, 7), TestMaps.Tile(9));
        var before = (TestMaps.Cells(ground), TestMaps.Cells(details));

        var edit = new TileEdit(map);
        var cells = new CellSet();
        map.Layout.Topology.Range(new GridCoord(-4, 7), 4, cells);
        edit.Fill(ground, cells, TestMaps.Tile(1));
        edit.Set(details, new GridCoord(30, -30), TestMaps.Tile(2));
        var applied = edit.Commit()!;
        var after = (TestMaps.Cells(ground), TestMaps.Cells(details));

        var redo = applied.Invert().Apply(map);
        Assert.Equal(before, (TestMaps.Cells(ground), TestMaps.Cells(details)), CellsComparer.Instance);
        redo.Apply(map);
        Assert.Equal(after, (TestMaps.Cells(ground), TestMaps.Cells(details)), CellsComparer.Instance);
        Assert.Equal(cells.Count + 1, applied.Count);
    }

    [Fact]
    public void CancelRestoresEveryCell()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        layer.SetCell(new GridCoord(2, 2), TestMaps.Tile(7));
        var edit = new TileEdit(map);

        edit.Fill(layer, [new GridCoord(1, 1), new GridCoord(2, 2), new GridCoord(40, 40)], TestMaps.Tile(1));
        edit.Cancel();

        Assert.Equal([new PlacedTile(new GridCoord(2, 2), TestMaps.Tile(7))], TestMaps.Cells(layer));
        Assert.Throws<InvalidOperationException>(() => edit.Commit());
    }

    [Fact]
    public void EachOperationNotifiesOnceWithTheBoundsOfWhatChanged()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        var changes = TestMaps.Record(map);
        var edit = new TileEdit(map);

        edit.Fill(layer, [new GridCoord(-2, 5), new GridCoord(9, -1), new GridCoord(3, 3)], TestMaps.Tile(1));
        edit.Fill(layer, [new GridCoord(9, -1)], TestMaps.Tile(1));
        var change = Assert.Single(changes);

        Assert.Equal(new GridBounds(-2, -1, 9, 5), change.Cells);
        edit.Commit()!.Invert().Apply(map);
        Assert.Equal(2, changes.Count);
        Assert.Equal(new GridBounds(-2, -1, 9, 5), changes[^1].Cells);
    }

    [Fact]
    public void OriginalReportsTheValueBeforeTheEdit()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        layer.SetCell(GridCoord.Zero, TestMaps.Tile(3));
        var edit = new TileEdit(map);

        edit.Set(layer, GridCoord.Zero, TestMaps.Tile(4));

        Assert.Equal(TestMaps.Tile(3), edit.Original(layer, GridCoord.Zero));
        Assert.Equal(TileCell.Empty, edit.Original(layer, new GridCoord(1, 0)));
    }

    [Fact]
    public void NothingChangedCommitsToNothing()
    {
        var map = TestMaps.Create();
        var edit = new TileEdit(map);
        edit.Set(map.Ground(), GridCoord.Zero, TestMaps.Tile(1));
        edit.Set(map.Ground(), GridCoord.Zero, TileCell.Empty);

        Assert.Null(edit.Commit());
    }

    [Fact]
    public void EditsOnlyTouchLayersOfTheirMap()
    {
        var edit = new TileEdit(TestMaps.Create());

        Assert.Throws<InvalidOperationException>(() => edit.Set(TestMaps.Create().Ground(), GridCoord.Zero, TestMaps.Tile(1)));
    }

    [Fact]
    public void PaintingUsesTheBrushPerCell()
    {
        var map = TestMaps.Create();
        var brush = TileBrush.Random([new TileChoice(TestMaps.Tile(1)), new TileChoice(TestMaps.Tile(2))], seed: 3);
        var cells = new CellSet();
        map.Layout.Topology.Range(GridCoord.Zero, 6, cells);
        var edit = new TileEdit(map);

        edit.Paint(map.Ground(), cells, brush);

        Assert.All(cells, cell => Assert.Equal(brush.TileFor(cell), map.Ground().GetCell(cell)));
        Assert.Equal(2, cells.Select(map.Ground().GetCell).Distinct().Count());
    }

    private sealed class CellsComparer : IEqualityComparer<(PlacedTile[], PlacedTile[])>
    {
        public static CellsComparer Instance { get; } = new();

        public bool Equals((PlacedTile[], PlacedTile[]) x, (PlacedTile[], PlacedTile[]) y) => x.Item1.SequenceEqual(y.Item1) && x.Item2.SequenceEqual(y.Item2);

        public int GetHashCode((PlacedTile[], PlacedTile[]) obj) => obj.Item1.Length;
    }
}
