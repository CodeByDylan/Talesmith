using System.Numerics;

namespace Talesmith.Grids.Tests;

public sealed class GridTopologyTests
{
    public static TheoryData<GridKind> Kinds => [GridKind.HexPointyTop, GridKind.HexFlatTop, GridKind.Square];

    /// <summary>Regular cells, so rotating and mirroring offsets can be checked against rotating and mirroring world positions.</summary>
    private static IGridLayout RegularLayout(GridKind kind) => kind switch
    {
        GridKind.HexPointyTop => new HexLayout(true, MathF.Sqrt(3) * 32, 64),
        GridKind.HexFlatTop => new HexLayout(false, 64, MathF.Sqrt(3) * 32),
        _ => new SquareLayout(48, 48, allowDiagonals: true)
    };

    private static readonly GridCoord[] Offsets = [new(1, 0), new(2, -1), new(0, 3), new(-2, 1), new(3, 2), new(-1, -4)];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void DirectionsAreTheEdgeNeighborsOfTheLayout(GridKind kind)
    {
        var layout = RegularLayout(kind);
        var topology = layout.Topology;

        Assert.Same(GridTopology.For(kind), topology);
        Assert.Equal(kind == GridKind.Square ? 4 : 6, topology.NeighborCount);
        Assert.Equal(layout.RotationSteps, topology.RotationSteps);
        foreach (var direction in topology.Directions)
            Assert.Equal(1, topology.Distance(GridCoord.Zero, direction));
        if (kind != GridKind.Square)
            Assert.Equal(layout.NeighborOffsets.ToArray(), topology.Directions.ToArray());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RotatingAnOffsetRotatesItsWorldPositionClockwise(GridKind kind)
    {
        var layout = RegularLayout(kind);
        var topology = layout.Topology;
        var step = MathF.Tau / topology.RotationSteps;
        foreach (var offset in Offsets)
        {
            for (var steps = -topology.RotationSteps; steps <= topology.RotationSteps; steps++)
            {
                var expected = Vector2.Transform(layout.CellToWorld(offset), Matrix3x2.CreateRotation(step * steps));
                AssertClose(expected, layout.CellToWorld(topology.Rotate(offset, steps)));
            }
        }
    }

    [Fact]
    public void HexRotationTurnsEastToSouthEast()
    {
        Assert.Equal(new GridCoord(0, 1), GridTopology.HexPointyTop.Rotate(new GridCoord(1, 0), 1));
        Assert.Equal(new GridCoord(0, 1), GridTopology.Square.Rotate(new GridCoord(1, 0), 1));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void MirroringAnOffsetMirrorsItsWorldPosition(GridKind kind)
    {
        var layout = RegularLayout(kind);
        var topology = layout.Topology;
        foreach (var offset in Offsets)
        {
            var world = layout.CellToWorld(offset);
            AssertClose(new Vector2(-world.X, world.Y), layout.CellToWorld(topology.MirrorHorizontal(offset)));
            AssertClose(new Vector2(world.X, -world.Y), layout.CellToWorld(topology.MirrorVertical(offset)));
            Assert.Equal(offset, topology.MirrorHorizontal(topology.MirrorHorizontal(offset)));
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void OffsetCoordinatesRoundTripAndLineUpOnScreen(GridKind kind)
    {
        var layout = RegularLayout(kind);
        var topology = layout.Topology;
        for (var y = -3; y <= 3; y++)
        {
            for (var x = -3; x <= 3; x++)
            {
                var cell = new GridCoord(x, y);
                Assert.Equal(cell, topology.FromOffset(topology.ToOffset(cell)));
            }
        }

        for (var row = -2; row <= 2; row++)
        {
            var a = layout.CellToWorld(topology.FromOffset(new GridCoord(0, row)));
            var b = layout.CellToWorld(topology.FromOffset(new GridCoord(4, row)));
            if (kind == GridKind.HexFlatTop)
                Assert.True(b.X > a.X);
            else
                Assert.Equal(a.Y, b.Y, 0.001f);
        }
    }

    [Fact]
    public void SquareDistancesCountDiagonalsAsOneStep()
    {
        Assert.Equal(3, GridTopology.Square.Distance(GridCoord.Zero, new GridCoord(3, -3)));
        Assert.Equal(6, GridTopology.HexPointyTop.Distance(GridCoord.Zero, new GridCoord(3, 3)));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void LinesAreUnbrokenAndIncludeBothEnds(GridKind kind)
    {
        var topology = GridTopology.For(kind);
        var start = new GridCoord(-3, 2);
        var end = new GridCoord(5, -4);
        var line = new List<GridCoord>();

        topology.Line(start, end, line);

        Assert.Equal(topology.Distance(start, end) + 1, line.Count);
        Assert.Equal(start, line[0]);
        Assert.Equal(end, line[^1]);
        for (var i = 1; i < line.Count; i++)
            Assert.Equal(1, topology.Distance(line[i - 1], line[i]));
    }

    [Theory]
    [InlineData(GridKind.HexPointyTop, 0, 1, 1)]
    [InlineData(GridKind.HexPointyTop, 2, 19, 12)]
    [InlineData(GridKind.HexFlatTop, 3, 37, 18)]
    [InlineData(GridKind.Square, 2, 25, 16)]
    public void RangesAndRingsHaveTheExpectedSize(GridKind kind, int radius, int rangeCount, int ringCount)
    {
        var topology = GridTopology.For(kind);
        var range = new CellSet();
        var ring = new CellSet();

        topology.Range(new GridCoord(4, -7), radius, range);
        topology.Ring(new GridCoord(4, -7), radius, ring);

        Assert.Equal(rangeCount, range.Count);
        Assert.Equal(ringCount, ring.Count);
        foreach (var cell in ring)
        {
            Assert.Equal(radius, topology.Distance(new GridCoord(4, -7), cell));
            Assert.Contains(cell, range);
        }
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01f);
        Assert.Equal(expected.Y, actual.Y, 0.01f);
    }
}
