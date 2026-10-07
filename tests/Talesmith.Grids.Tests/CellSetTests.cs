namespace Talesmith.Grids.Tests;

public sealed class CellSetTests
{
    [Fact]
    public void AddsRemovesAndFindsCellsAnywhere()
    {
        var set = new CellSet();
        GridCoord[] cells = [new(0, 0), new(-1, -1), new(31, 31), new(32, 0), new(-33, 70), new(1_000_000, -1_000_000)];

        foreach (var cell in cells)
            Assert.True(set.Add(cell));
        Assert.False(set.Add(new GridCoord(31, 31)));

        Assert.Equal(cells.Length, set.Count);
        Assert.All(cells, cell => Assert.Contains(cell, set));
        Assert.DoesNotContain(new GridCoord(1, 0), set);
        Assert.Equal(cells.Sorted(), set.Sorted());

        Assert.True(set.Remove(new GridCoord(-1, -1)));
        Assert.False(set.Remove(new GridCoord(-1, -1)));
        Assert.Equal(cells.Length - 1, set.Count);
    }

    [Fact]
    public void BoundsCoverEveryCell()
    {
        var set = new CellSet([new GridCoord(-5, 3), new GridCoord(40, -2), new GridCoord(7, 90)]);

        Assert.Equal(new GridBounds(-5, -2, 40, 90), set.Bounds);
        Assert.True(new CellSet().Bounds.IsEmpty);
    }

    [Fact]
    public void SelectionModesCombineSets()
    {
        var a = new CellSet([new GridCoord(0, 0), new GridCoord(1, 0), new GridCoord(2, 0)]);
        var b = new CellSet([new GridCoord(2, 0), new GridCoord(3, 0), new GridCoord(-40, 5)]);

        var added = new CellSet(a);
        added.Apply(b, SelectionMode.Add);
        var subtracted = new CellSet(a);
        subtracted.Apply(b, SelectionMode.Subtract);
        var intersected = new CellSet(a);
        intersected.Apply(b, SelectionMode.Intersect);
        var replaced = new CellSet(a);
        replaced.Apply(b, SelectionMode.Replace);

        Assert.Equal(5, added.Count);
        Assert.Equal([new GridCoord(0, 0), new GridCoord(1, 0)], subtracted.Sorted());
        Assert.Equal([new GridCoord(2, 0)], intersected.ToArray());
        Assert.Equal(b.Sorted(), replaced.Sorted());
    }

    [Fact]
    public void TranslateMovesEveryCell()
    {
        var set = new CellSet([new GridCoord(0, 0), new GridCoord(31, 2)]);

        var moved = set.Translate(new GridCoord(1, -3));

        Assert.Equal([new GridCoord(1, -3), new GridCoord(32, -1)], moved.Sorted());
    }

    [Fact]
    public void ClearingKeepsTheSetUsable()
    {
        var set = new CellSet();
        for (var i = 0; i < 1000; i++)
            set.Add(new GridCoord(i * 7, -i * 3));

        set.Clear();
        set.Add(new GridCoord(5, 5));

        Assert.Equal([new GridCoord(5, 5)], set.ToArray());
    }
}
