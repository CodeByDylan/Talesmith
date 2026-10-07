using System.Numerics;
using Talesmith.Grids;
using Talesmith.VFX.Simulation;

namespace Talesmith.VFX.Tests;

public sealed class ShapeTests
{
    private const int Samples = 2000;

    [Fact]
    public void PointEmitsFromTheOriginAlongTheAngle()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Point, Angle = 0 };

        foreach (var (position, direction) in Sample(shape))
        {
            Assert.Equal(Vector2.Zero, position);
            Assert.Equal(1, direction.X, 1e-4f);
        }
    }

    [Fact]
    public void LineStaysOnItsSegment()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Line, Size = new Vector2(100, 0), Direction = ParticleDirectionMode.Shape };

        foreach (var (position, direction) in Sample(shape))
        {
            Assert.InRange(position.X, -50, 50);
            Assert.Equal(0, position.Y);
            Assert.Equal(-1, direction.Y, 1e-4f);
        }
    }

    [Theory]
    [InlineData(ParticleEmitFrom.Volume)]
    [InlineData(ParticleEmitFrom.Edge)]
    public void RectangleStaysInside(ParticleEmitFrom from)
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Rectangle, Size = new Vector2(80, 40), EmitFrom = from };

        foreach (var (position, _) in Sample(shape))
        {
            Assert.InRange(position.X, -40.001f, 40.001f);
            Assert.InRange(position.Y, -20.001f, 20.001f);
            if (from == ParticleEmitFrom.Edge)
                Assert.True(MathF.Abs(MathF.Abs(position.X) - 40) < 1e-3f || MathF.Abs(MathF.Abs(position.Y) - 20) < 1e-3f);
        }
    }

    [Fact]
    public void RingStaysBetweenItsRadii()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 50, InnerRadius = 30, Direction = ParticleDirectionMode.Shape };

        foreach (var (position, direction) in Sample(shape))
        {
            Assert.InRange(position.Length(), 29.99f, 50.01f);
            Assert.Equal(1, Vector2.Dot(Vector2.Normalize(position), direction), 1e-3f);
        }
    }

    [Fact]
    public void ArcCoversOnlyItsAngle()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Circle, Radius = 20, Arc = MathF.PI, Rotation = MathF.PI, EmitFrom = ParticleEmitFrom.Edge };

        foreach (var (position, _) in Sample(shape))
        {
            Assert.Equal(20, position.Length(), 1e-3f);
            Assert.True(position.Y <= 1e-3f, "A half arc rotated by 180° should cover only the upper half.");
        }
    }

    [Fact]
    public void ConeFansWithinItsAngle()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Cone, Radius = 10, ConeAngle = MathF.PI / 3, Angle = -MathF.PI / 2, Direction = ParticleDirectionMode.Shape };

        foreach (var (position, direction) in Sample(shape))
        {
            Assert.InRange(position.X, -10.001f, 10.001f);
            Assert.Equal(0, position.Y, 1e-3f);
            var angle = MathF.Acos(Vector2.Dot(direction, -Vector2.UnitY));
            Assert.True(angle <= MathF.PI / 6 + 1e-3f);
        }
    }

    public static TheoryData<GridKind, ParticleEmitFrom> Tiles => new()
    {
        { GridKind.HexPointyTop, ParticleEmitFrom.Volume },
        { GridKind.HexPointyTop, ParticleEmitFrom.Edge },
        { GridKind.HexFlatTop, ParticleEmitFrom.Volume },
        { GridKind.HexFlatTop, ParticleEmitFrom.Edge },
        { GridKind.Square, ParticleEmitFrom.Volume },
        { GridKind.Square, ParticleEmitFrom.Edge }
    };

    [Theory]
    [MemberData(nameof(Tiles))]
    public void TileStaysInsideItsGridCell(GridKind kind, ParticleEmitFrom from)
    {
        var size = new Vector2(64, 56);
        var shape = new ShapeModule { Kind = ParticleShapeKind.Tile, Tile = kind, CellSize = size, EmitFrom = from };
        IGridLayout layout = kind == GridKind.Square ? new SquareLayout(size.X, size.Y) : new HexLayout(kind == GridKind.HexPointyTop, size.X, size.Y);
        var outline = Enumerable.Range(0, layout.CornerCount).Select(layout.CornerOffset).ToArray();
        var cells = new HashSet<GridCoord>();

        foreach (var (position, _) in Sample(shape))
        {
            Assert.True(Inside(outline, position, 1e-3f), $"{position} is outside the {kind} cell.");
            if (from == ParticleEmitFrom.Edge)
                Assert.True(DistanceToOutline(outline, position) < 1e-3f, $"{position} is not on the {kind} cell's outline.");
            else
                cells.Add(layout.WorldToCell(position));
        }

        if (from == ParticleEmitFrom.Volume)
            Assert.Equal([new GridCoord(0, 0)], cells);
    }

    [Theory]
    [InlineData(ParticleEmitFrom.Volume)]
    [InlineData(ParticleEmitFrom.Edge)]
    public void ConcavePolygonStaysInside(ParticleEmitFrom from)
    {
        Vector2[] points = [new(0, 0), new(100, 0), new(100, 30), new(30, 30), new(30, 100), new(0, 100)];
        var shape = new ShapeModule { Kind = ParticleShapeKind.Polygon, Points = [.. points], EmitFrom = from };
        var samples = Sample(shape).ToList();

        foreach (var (position, _) in samples)
        {
            Assert.True(Inside(points, position, 1e-3f), $"{position} is outside the polygon.");
            Assert.False(position.X > 30.01f && position.Y > 30.01f, $"{position} is in the polygon's notch.");
        }

        if (from == ParticleEmitFrom.Volume)
        {
            Assert.Contains(samples, s => s.Position.X > 60);
            Assert.Contains(samples, s => s.Position.Y > 60);
        }
    }

    [Fact]
    public void SpreadTurnsDirectionsWithinTheAngle()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Point, Angle = 0, Spread = MathF.PI / 2 };

        foreach (var (_, direction) in Sample(shape))
            Assert.True(MathF.Acos(Math.Clamp(direction.X, -1, 1)) <= MathF.PI / 4 + 1e-3f);
    }

    [Fact]
    public void ExtentCoversEverySample()
    {
        var shape = new ShapeModule { Kind = ParticleShapeKind.Tile, Tile = GridKind.HexFlatTop, CellSize = new Vector2(80, 70), Offset = new Vector2(10, -5) };
        var sampler = new ShapeSampler();

        var extent = sampler.Extent(shape);

        Assert.All(Sample(shape), s => Assert.True(s.Position.Length() <= extent + 1e-3f));
    }

    private static List<(Vector2 Position, Vector2 Direction)> Sample(ShapeModule shape)
    {
        var sampler = new ShapeSampler();
        sampler.Prepare(shape);
        var random = new ParticleRandom(12345);
        var results = new List<(Vector2 Position, Vector2 Direction)>(Samples);
        for (var i = 0; i < Samples; i++)
        {
            sampler.Sample(shape, ref random, out var position, out var direction);
            Assert.Equal(1, direction.Length(), 1e-3f);
            results.Add((position, direction));
        }

        return results;
    }

    private static bool Inside(Vector2[] polygon, Vector2 point, float tolerance)
    {
        if (DistanceToOutline(polygon, point) <= tolerance)
            return true;
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (a.Y > point.Y != b.Y > point.Y && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }

    private static float DistanceToOutline(Vector2[] polygon, Vector2 point)
    {
        var best = float.MaxValue;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var t = Math.Clamp(Vector2.Dot(point - a, b - a) / (b - a).LengthSquared(), 0, 1);
            best = MathF.Min(best, Vector2.Distance(point, a + (b - a) * t));
        }

        return best;
    }
}
