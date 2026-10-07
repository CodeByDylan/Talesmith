namespace Talesmith.Grids.Tests;

public sealed class FloodFillTests
{
    public static TheoryData<GridKind> Kinds => [GridKind.HexPointyTop, GridKind.HexFlatTop, GridKind.Square];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void FillsTheRegionEnclosedByWalls(GridKind kind)
    {
        var topology = GridTopology.For(kind);
        var walls = new CellSet();
        topology.Ring(GridCoord.Zero, 3, walls);
        var inside = new CellSet();
        topology.Range(GridCoord.Zero, 2, inside);
        var region = new CellSet();

        var result = FloodFill.Collect(topology, new GridCoord(1, 0), cell => !walls.Contains(cell), 10_000, region, TestContext.Current.CancellationToken);

        Assert.False(result.ReachedLimit);
        Assert.Equal(inside.Count, result.Count);
        Assert.Equal(inside.Sorted(), region.Sorted());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void StopsAtTheLimitInOpenSpace(GridKind kind)
    {
        var region = new CellSet();

        var result = FloodFill.Collect(GridTopology.For(kind), GridCoord.Zero, _ => true, 500, region, TestContext.Current.CancellationToken);

        Assert.True(result.ReachedLimit);
        Assert.Equal(500, result.Count);
        Assert.Equal(500, region.Count);
    }

    [Fact]
    public void ARegionThatFitsTheLimitExactlyIsComplete()
    {
        var region = new CellSet();

        var result = FloodFill.Collect(GridTopology.Square, GridCoord.Zero, cell => cell.Y == 0 && cell.X is >= 0 and < 10, 10, region, TestContext.Current.CancellationToken);

        Assert.Equal(new FloodFillResult(10, false), result);
    }

    [Fact]
    public void AStartThatDoesNotMatchFillsNothing()
    {
        var region = new CellSet();

        var result = FloodFill.Collect(GridTopology.HexPointyTop, GridCoord.Zero, _ => false, 10, region, TestContext.Current.CancellationToken);

        Assert.Equal(default, result);
        Assert.True(region.IsEmpty);
    }

    [Fact]
    public void SquareFillsFollowEdgesNotCorners()
    {
        var region = new CellSet();
        CellSet open = [new GridCoord(0, 0), new GridCoord(1, 1)];

        FloodFill.Collect(GridTopology.Square, GridCoord.Zero, open.Contains, 10, region, TestContext.Current.CancellationToken);

        Assert.Equal([GridCoord.Zero], region.ToArray());
    }
}
