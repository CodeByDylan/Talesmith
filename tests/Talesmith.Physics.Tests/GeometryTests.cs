using System.Numerics;
using Talesmith.Physics.Collision;
using Talesmith.Physics.Geometry;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void ConcavePolygonsAreSplitIntoConvexPiecesOfTheSameArea()
    {
        Vector2[] arrow = [new(0, 0), new(40, 20), new(0, 40), new(10, 20)];

        var pieces = PolygonTools.Decompose(arrow);

        Assert.True(pieces.Count >= 2);
        Assert.All(pieces, p => Assert.True(PolygonTools.IsConvex(p)));
        Assert.Equal(MathF.Abs(PolygonTools.SignedArea(arrow)), pieces.Sum(p => PolygonTools.SignedArea(p)), 2);
    }

    [Fact]
    public void ConvexPolygonsWithManyPointsAreSplitIntoPiecesOfAtMostEightPoints()
    {
        var circle = Enumerable.Range(0, 20).Select(i => 50 * new Vector2(MathF.Cos(i * MathF.Tau / 20), MathF.Sin(i * MathF.Tau / 20))).ToArray();

        var pieces = PolygonTools.Decompose(circle);

        Assert.All(pieces, p => Assert.InRange(p.Length, 3, Shape.MaxVertices));
        Assert.Equal(MathF.Abs(PolygonTools.SignedArea(circle)), pieces.Sum(p => PolygonTools.SignedArea(p)), 1);
    }

    [Fact]
    public void SelfIntersectingPolygonsAreRejectedWithAClearReason()
    {
        Vector2[] bowTie = [new(0, 0), new(10, 10), new(10, 0), new(0, 10)];

        var error = Assert.Throws<ArgumentException>(() => PolygonTools.Decompose(bowTie));

        Assert.Contains("cross", error.Message);
        Assert.False(ColliderGeometry.IsValid(Collider2D.Polygon(bowTie), out var reason));
        Assert.Contains("cross", reason);
    }

    [Fact]
    public void PolygonsAreStoredWithOutwardNormalsWhateverTheirWinding()
    {
        var clockwise = Shape.Polygon([new(0, 0), new(0, 10), new(10, 10), new(10, 0)]);
        var counter = Shape.Polygon([new(0, 0), new(10, 0), new(10, 10), new(0, 10)]);

        foreach (var shape in new[] { clockwise, counter })
        {
            var center = shape.Centroid;
            for (var i = 0; i < shape.Count; i++)
                Assert.True(Vector2.Dot(shape.Normals[i], shape.Points[i] - center) > 0);
        }
    }

    [Fact]
    public void DistanceBetweenSeparatedShapesIsExact()
    {
        var box = Shape.Box(new Vector2(10, 10));
        var circle = Shape.Circle(Vector2.Zero, 5);

        var result = Distance.Compute(box, circle, new Vector2(30, 0));

        Assert.Equal(20, result.Distance, 3);
        Assert.Equal(new Vector2(10, 0), result.PointA);
        Assert.Equal(1, result.Normal.X, 3);
    }

    [Fact]
    public void DistanceBetweenOverlappingCoresIsZero()
    {
        var result = Distance.Compute(Shape.Box(new Vector2(10, 10)), Shape.Box(new Vector2(10, 10)), new Vector2(5, 3));

        Assert.Equal(0, result.Distance);
    }

    [Fact]
    public void ShapeCastsStopAtTheGap()
    {
        var wall = Shape.Box(new Vector2(5, 100), new Vector2(100, 0));
        var ball = Shape.Circle(Vector2.Zero, 10);

        var cast = Distance.Cast(wall, ball, new Vector2(200, 0), 1, 1, 0.25f);

        Assert.True(cast.Hit);
        Assert.Equal((95 - 10 - 1) / 200f, cast.Fraction, 3);
        Assert.Equal(-1, cast.Normal.X, 3);
    }

    [Fact]
    public void ShapeCastsMovingAwayDoNotHit()
    {
        var wall = Shape.Box(new Vector2(5, 100), new Vector2(100, 0));
        var ball = Shape.Circle(new Vector2(84.5f, 0), 10);

        Assert.False(Distance.Cast(wall, ball, new Vector2(-50, 0), 1, 1, 0.25f).Hit);
        Assert.True(Distance.Cast(wall, ball, new Vector2(50, 0), 1, 1, 0.25f).Hit);
    }

    [Theory]
    [InlineData("circle")]
    [InlineData("capsule")]
    [InlineData("polygon")]
    public void RaysHitTheNearSurfaceOfEachShape(string kind)
    {
        var shape = kind switch
        {
            "circle" => Shape.Circle(new Vector2(100, 0), 10),
            "capsule" => Shape.Capsule(new Vector2(100, -20), new Vector2(100, 20), 10),
            _ => Shape.Box(new Vector2(10, 30), new Vector2(100, 0))
        };

        var result = RayCast.Shape(shape, new Vector2(0, 5), new Vector2(200, 0), 1);

        Assert.True(result.Hit);
        Assert.Equal(kind == "circle" ? -MathF.Sqrt(75) / 10 : -1, result.Normal.X, 3);
        Assert.Equal(kind == "circle" ? 100 - MathF.Sqrt(75) : 90, result.Point.X, 2);
    }

    [Fact]
    public void ColliderOutlinesFollowTheTransform()
    {
        var collider = Collider2D.Box(new Vector2(20, 10)) with { Offset = new Vector2(10, 0) };
        var transform = new Transform(new Vector2(100, 100), MathF.PI / 2) { Scale = new Vector2(2, 2) };
        Span<Vector2> points = stackalloc Vector2[8];

        var count = ColliderGeometry.GetOutline(collider, transform, points);
        var bounds = ColliderGeometry.GetBounds(collider, transform);

        Assert.Equal(4, count);
        // Scaled by two and turned a quarter: the box spans 20 × 40 around (100, 120).
        Assert.Equal(90, bounds.Left, 3);
        Assert.Equal(110, bounds.Right, 3);
        Assert.Equal(100, bounds.Top, 3);
        Assert.Equal(140, bounds.Bottom, 3);
    }

    [Fact]
    public void CircleAndCapsuleOutlinesAreRounded()
    {
        Span<Vector2> points = stackalloc Vector2[64];
        var transform = new Transform(Vector2.Zero);

        var circle = ColliderGeometry.GetOutline(Collider2D.Circle(10), transform, points);
        Assert.Equal(ColliderGeometry.DefaultCircleSegments, circle);
        Assert.All(points[..circle].ToArray(), p => Assert.Equal(10, p.Length(), 3));

        var capsule = ColliderGeometry.GetOutline(Collider2D.Capsule(new Vector2(20, 60)), transform, points);
        var bounds = Talesmith.Mathematics.Rect2.Bounding(points[..capsule]);
        Assert.Equal(20, bounds.Width, 2);
        Assert.Equal(60, bounds.Height, 2);
    }

    [Fact]
    public void MassFollowsAreaAndDensity()
    {
        Assert.Equal(400, Shape.Box(new Vector2(10, 10)).ComputeMass(1).Mass, 3);
        Assert.Equal(MathF.PI * 100 * 2, Shape.Circle(Vector2.Zero, 10).ComputeMass(2).Mass, 2);
        Assert.Equal(MathF.PI * 100 + 20 * 40, Shape.Capsule(new Vector2(0, -20), new Vector2(0, 20), 10).ComputeMass(1).Mass, 2);
    }
}
