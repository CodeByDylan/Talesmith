using System.Numerics;

namespace Talesmith.Grids.Tests;

public sealed class SquareLayoutTests
{
    [Fact]
    public void CellCentersMatchHexyOrthogonalMaps()
    {
        var layout = new SquareLayout(64, 48);

        Assert.Equal(Vector2.Zero, layout.CellToWorld(GridCoord.Zero));
        Assert.Equal(new Vector2(192, -96), layout.CellToWorld(new GridCoord(3, -2)));
        Assert.Equal(new Vector2(32, 12), layout.CellToWorld(new Vector2(0.5f, 0.25f)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(31.9f, 23.9f, 0, 0)]
    [InlineData(32.1f, 24.1f, 1, 1)]
    [InlineData(-32.1f, -24.1f, -1, -1)]
    public void WorldToCellFindsTheCellContainingAPoint(float x, float y, int column, int row)
    {
        var layout = new SquareLayout(64, 48);

        Assert.Equal(new GridCoord(column, row), layout.WorldToCell(new Vector2(x, y)));
    }

    [Fact]
    public void NeighborsFollowHexyRectangularOrder()
    {
        var layout = new SquareLayout(64, 64);

        Assert.Equal([new GridCoord(1, 0), new GridCoord(0, -1), new GridCoord(-1, 0), new GridCoord(0, 1)], layout.NeighborOffsets.ToArray());
    }

    [Fact]
    public void RotationStepsAreQuarterTurnsOnSquareGridsAndSixthsOnHexGrids()
    {
        Assert.Equal(4, new SquareLayout(64, 64).RotationSteps);
        Assert.Equal(6, new HexLayout(pointyTop: true, 128, 148).RotationSteps);
        Assert.Equal(6, new HexLayout(pointyTop: false, 148, 128).RotationSteps);
    }

    [Fact]
    public void CornersOutlineTheCell()
    {
        var layout = new SquareLayout(64, 48);
        var corners = Enumerable.Range(0, layout.CornerCount).Select(layout.CornerOffset).ToArray();

        Assert.Equal(4, corners.Length);
        Assert.All(corners, corner => Assert.Equal(32, MathF.Abs(corner.X)));
        Assert.All(corners, corner => Assert.Equal(24, MathF.Abs(corner.Y)));
        Assert.Equal(4, corners.Distinct().Count());
    }
}
