using System.Numerics;

namespace Talesmith.Grids.Tests;

public sealed class GridShapesTests
{
    public static TheoryData<GridKind> Kinds => [GridKind.HexPointyTop, GridKind.HexFlatTop, GridKind.Square];

    private static IGridLayout Layout(GridKind kind) => kind switch
    {
        GridKind.HexPointyTop => new HexLayout(true, 128, 148),
        GridKind.HexFlatTop => new HexLayout(false, 148, 128),
        _ => new SquareLayout(64, 48)
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RectanglesCoverEveryRowAndColumnBetweenTheCorners(GridKind kind)
    {
        var topology = GridTopology.For(kind);
        var a = topology.FromOffset(new GridCoord(-2, 1));
        var b = topology.FromOffset(new GridCoord(3, 4));
        var filled = new CellSet();
        var outline = new CellSet();

        GridShapes.Rectangle(topology, a, b, filled: true, filled);
        GridShapes.Rectangle(topology, b, a, filled: false, outline);

        Assert.Equal(6 * 4, filled.Count);
        Assert.Equal(6 * 4 - 4 * 2, outline.Count);
        Assert.All(outline, cell => Assert.Contains(cell, filled));
        Assert.All(filled, cell => Assert.InRange(topology.ToOffset(cell).Y, 1, 4));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void CirclesHoldTheCellsWithinTheRadius(GridKind kind)
    {
        var layout = Layout(kind);
        var center = new GridCoord(3, -2);
        var radius = 300f;
        var circle = new CellSet();

        GridShapes.Circle(layout, center, radius, circle);

        Assert.Contains(center, circle);
        Assert.All(circle, cell => Assert.True(Vector2.Distance(layout.CellToWorld(cell), layout.CellToWorld(center)) <= radius));
        var outside = new CellSet();
        layout.Topology.Range(center, 10, outside);
        outside.ExceptWith(circle);
        Assert.All(outside, cell => Assert.True(Vector2.Distance(layout.CellToWorld(cell), layout.CellToWorld(center)) > radius));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void CircleOutlinesAreTheBorderOfTheFilledCircle(GridKind kind)
    {
        var layout = Layout(kind);
        var filled = new CellSet();
        var outline = new CellSet();
        var edge = layout.Topology.FromOffset(new GridCoord(4, 0));

        GridShapes.Circle(layout, GridCoord.Zero, edge, filled: true, filled);
        GridShapes.Circle(layout, GridCoord.Zero, edge, filled: false, outline);

        Assert.True(outline.Count > 0 && outline.Count < filled.Count);
        Assert.All(outline, cell => Assert.Contains(cell, filled));
        Assert.DoesNotContain(GridCoord.Zero, outline);
    }

    [Theory]
    [InlineData(GridKind.HexPointyTop, 2, true, 19)]
    [InlineData(GridKind.HexFlatTop, 2, false, 12)]
    [InlineData(GridKind.Square, 1, true, 9)]
    public void HexagonsAreRangesOrRings(GridKind kind, int radius, bool filled, int count)
    {
        var cells = new CellSet();

        GridShapes.Hexagon(GridTopology.For(kind), new GridCoord(-4, 4), radius, filled, cells);

        Assert.Equal(count, cells.Count);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void LassoSelectsTheCellsWhoseCentersAreInside(GridKind kind)
    {
        var layout = Layout(kind);
        Vector2[] polygon = [new(-200, -200), new(250, -150), new(300, 260), new(-180, 220)];
        var cells = new CellSet();

        GridShapes.Polygon(layout, polygon, cells);

        Assert.Contains(GridCoord.Zero, cells);
        Assert.All(cells, cell =>
        {
            var center = layout.CellToWorld(cell);
            Assert.InRange(center.X, -200, 300);
            Assert.InRange(center.Y, -200, 260);
        });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BrushesOfSizeOnePaintOneCell(GridKind kind)
    {
        var layout = Layout(kind);
        foreach (var shape in Enum.GetValues<BrushShape>())
        {
            var cells = new CellSet();
            new GridBrush(shape, 1).Footprint(layout, new GridCoord(7, 7), cells);
            Assert.Equal([new GridCoord(7, 7)], cells.ToArray());
        }
    }

    [Theory]
    [InlineData(GridKind.HexPointyTop, BrushShape.Range, 3, 19)]
    [InlineData(GridKind.Square, BrushShape.Range, 3, 25)]
    [InlineData(GridKind.Square, BrushShape.Circle, 2, 9)]
    public void BrushesGrowWithTheirSize(GridKind kind, BrushShape shape, int size, int count)
    {
        var cells = new CellSet();

        new GridBrush(shape, size).Footprint(Layout(kind) is var layout && kind == GridKind.Square ? new SquareLayout(32, 32) : layout, GridCoord.Zero, cells);

        Assert.Equal(count, cells.Count);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void StrokesCoverTheBrushAlongTheLine(GridKind kind)
    {
        var layout = Layout(kind);
        var cells = new CellSet();

        new GridBrush(BrushShape.Range, 2).Stroke(layout, new GridCoord(0, 0), new GridCoord(6, 0), cells);

        for (var x = 0; x <= 6; x++)
            Assert.Contains(new GridCoord(x, 0), cells);
        Assert.Contains(new GridCoord(0, -1), cells);
        Assert.Contains(new GridCoord(6, 1), cells);
    }
}
