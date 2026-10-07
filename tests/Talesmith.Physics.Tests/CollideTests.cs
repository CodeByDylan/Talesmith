using System.Numerics;
using Talesmith.Physics.Collision;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Tests;

public sealed class CollideTests
{
    private const float Speculative = 2;
    private const float Slop = 0.5f;

    public static TheoryData<string> Pairs => ["circle-circle", "capsule-circle", "polygon-circle", "capsule-capsule", "polygon-capsule", "polygon-polygon"];

    [Theory]
    [MemberData(nameof(Pairs))]
    public void OverlappingShapesProduceAContactWithANormalFromTheFirstToTheSecond(string pair)
    {
        var (a, b) = Overlapping(pair);

        Collide.Shapes(a, b, Speculative, Slop, out var manifold);

        Assert.True(manifold.PointCount > 0);
        Assert.True(manifold.MinSeparation < 0);
        Assert.Equal(0, manifold.Normal.X, 3);
        Assert.Equal(1, manifold.Normal.Y, 3);
        Assert.Equal(-2, manifold.MinSeparation, 2);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void SwappingTheShapesFlipsTheNormal(string pair)
    {
        var (a, b) = Overlapping(pair);

        Collide.Shapes(b, a, Speculative, Slop, out var manifold);

        Assert.Equal(-1, manifold.Normal.Y, 3);
        Assert.Equal(-2, manifold.MinSeparation, 2);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ShapesFartherApartThanTheSpeculativeDistanceDoNotTouch(string pair)
    {
        var (a, b) = Overlapping(pair);
        var moved = b.Transform(new Xf(new Vector2(0, 10), 0));

        Collide.Shapes(a, moved, Speculative, Slop, out var manifold);

        Assert.Equal(0, manifold.PointCount);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void ShapesWithinTheSpeculativeDistanceReportAPositiveSeparation(string pair)
    {
        var (a, b) = Overlapping(pair);
        var moved = b.Transform(new Xf(new Vector2(0, 3), 0));

        Collide.Shapes(a, moved, Speculative, Slop, out var manifold);

        Assert.True(manifold.PointCount > 0);
        Assert.Equal(1, manifold.MinSeparation, 2);
    }

    [Fact]
    public void ABoxRestingOnABoxHasTwoContactPoints()
    {
        var ground = Shape.Box(new Vector2(100, 10), new Vector2(0, 10));
        var box = Shape.Box(new Vector2(10, 10), new Vector2(0, -9.5f));

        Collide.Shapes(ground, box, Speculative, Slop, out var manifold);

        Assert.Equal(2, manifold.PointCount);
        Assert.Equal(-1, manifold.Normal.Y, 3);
        Assert.Equal(-0.5f, manifold.Points[0].Separation, 3);
        Assert.Equal(-0.5f, manifold.Points[1].Separation, 3);
        Assert.Equal(20, MathF.Abs(manifold.Points[0].Point.X - manifold.Points[1].Point.X), 2);
    }

    [Fact]
    public void ContactIdsStayTheSameWhileTheFeaturesDoNot()
    {
        var ground = Shape.Box(new Vector2(100, 10), new Vector2(0, 10));
        Collide.Shapes(ground, Shape.Box(new Vector2(10, 10), new Vector2(0, -9.5f)), Speculative, Slop, out var first);
        Collide.Shapes(ground, Shape.Box(new Vector2(10, 10), new Vector2(3, -9.6f)), Speculative, Slop, out var second);

        Assert.Equal(first.Points[0].Id, second.Points[0].Id);
        Assert.Equal(first.Points[1].Id, second.Points[1].Id);
    }

    [Fact]
    public void InternalEdgesNeverBecomeTheNormal()
    {
        // Two floor tiles side by side; a box entering the right tile from above-left must be pushed up, not left.
        var right = Shape.Box(new Vector2(16, 16), new Vector2(16, 16));
        right.InternalEdges = 0b1000;
        var box = Shape.Box(new Vector2(10, 10), new Vector2(-9.8f, -9.5f));

        Collide.Shapes(right, box, Speculative, Slop, out var manifold);

        Assert.True(manifold.PointCount > 0);
        Assert.Equal(-1, manifold.Normal.Y, 3);
    }

    [Fact]
    public void CirclesNearInternalCornersAreNotPushedSideways()
    {
        var right = Shape.Box(new Vector2(16, 16), new Vector2(16, 16));
        right.InternalEdges = 0b1000;
        var circle = Shape.Circle(new Vector2(-2, -9.5f), 10);

        Collide.Shapes(right, circle, Speculative, Slop, out var manifold);

        Assert.True(manifold.PointCount > 0);
        Assert.Equal(-1, manifold.Normal.Y, 3);
    }

    [Fact]
    public void RotatedBoxesCollideOnTheirCorner()
    {
        var ground = Shape.Box(new Vector2(100, 10), new Vector2(0, 10));
        var diamond = Shape.Box(new Vector2(10, 10), new Vector2(0, -13), MathF.PI / 4);

        Collide.Shapes(ground, diamond, Speculative, Slop, out var manifold);

        Assert.Equal(1, manifold.PointCount);
        Assert.Equal(-1, manifold.Normal.Y, 3);
        Assert.Equal(0, manifold.Points[0].Point.X, 2);
    }

    /// <summary>Two shapes overlapping by 2 units along +Y, the second below the first.</summary>
    private static (Shape A, Shape B) Overlapping(string pair) => pair switch
    {
        "circle-circle" => (Shape.Circle(Vector2.Zero, 10), Shape.Circle(new Vector2(0, 18), 10)),
        "capsule-circle" => (Shape.Capsule(new Vector2(-20, 0), new Vector2(20, 0), 10), Shape.Circle(new Vector2(0, 18), 10)),
        "polygon-circle" => (Shape.Box(new Vector2(20, 10)), Shape.Circle(new Vector2(0, 18), 10)),
        "capsule-capsule" => (Shape.Capsule(new Vector2(-20, 0), new Vector2(20, 0), 10), Shape.Capsule(new Vector2(-5, 18), new Vector2(5, 18), 10)),
        "polygon-capsule" => (Shape.Box(new Vector2(20, 10)), Shape.Capsule(new Vector2(-5, 18), new Vector2(5, 18), 10)),
        _ => (Shape.Box(new Vector2(20, 10)), Shape.Box(new Vector2(10, 10), new Vector2(0, 18)))
    };
}
