using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talesmith.Physics.Geometry;

internal enum ShapeType : byte
{
    Circle,
    Capsule,
    Polygon
}

[InlineArray(Shape.MaxVertices)]
internal struct ShapeVertices
{
    private Vector2 _element;
}

/// <summary>Mass, center of mass and rotational inertia about the shape's origin.</summary>
internal readonly record struct MassData(float Mass, Vector2 Center, float Inertia);

/// <summary>A convex shape with an optional rounding radius.</summary>
/// <remarks>
/// A circle is one point with a radius, a capsule two points with a radius and a polygon up to <see cref="MaxVertices"/> points with
/// a positive signed area (by <see cref="Vec.Cross(Vector2, Vector2)"/>), whose edge normals point outward. Capsules carry the two
/// opposite side normals so the polygon routines handle them as rounded two-sided polygons.
/// </remarks>
internal struct Shape
{
    public const int MaxVertices = 8;

    public ShapeType Type;
    public int Count;
    public float Radius;
    public ShapeVertices Points;
    public ShapeVertices Normals;

    /// <summary>Bit i is set when edge i (from point i to point i + 1) touches neighboring solid geometry, as between tile cells.</summary>
    public byte InternalEdges;

    public static Shape Circle(Vector2 center, float radius)
    {
        var shape = new Shape { Type = ShapeType.Circle, Count = 1, Radius = radius };
        shape.Points[0] = center;
        return shape;
    }

    public static Shape Capsule(Vector2 p1, Vector2 p2, float radius)
    {
        if (Vector2.DistanceSquared(p1, p2) < 1e-10f)
            return Circle((p1 + p2) * 0.5f, radius);
        var shape = new Shape { Type = ShapeType.Capsule, Count = 2, Radius = radius };
        shape.Points[0] = p1;
        shape.Points[1] = p2;
        var normal = Vec.Normalize(Vec.Cross(p2 - p1, 1f));
        shape.Normals[0] = normal;
        shape.Normals[1] = -normal;
        return shape;
    }

    public static Shape Box(Vector2 halfSize, Vector2 center = default, float angle = 0)
    {
        var q = Rot.FromAngle(angle);
        Span<Vector2> points =
        [
            center + q.Rotate(new Vector2(-halfSize.X, -halfSize.Y)),
            center + q.Rotate(new Vector2(halfSize.X, -halfSize.Y)),
            center + q.Rotate(new Vector2(halfSize.X, halfSize.Y)),
            center + q.Rotate(new Vector2(-halfSize.X, halfSize.Y))
        ];
        return Polygon(points);
    }

    /// <summary>Creates a polygon from convex points in either winding; collinear and duplicate points are dropped.</summary>
    /// <exception cref="ArgumentException">Fewer than three distinct points remain, or more than <see cref="MaxVertices"/>.</exception>
    public static Shape Polygon(ReadOnlySpan<Vector2> points)
    {
        Span<Vector2> clean = stackalloc Vector2[points.Length];
        var count = PolygonTools.Simplify(points, clean);
        if (count < 3)
            throw new ArgumentException("A polygon needs at least three points that are not on one line.", nameof(points));
        if (count > MaxVertices)
            throw new ArgumentException($"A convex polygon can have at most {MaxVertices} points.", nameof(points));

        var shape = new Shape { Type = ShapeType.Polygon, Count = count };
        var reverse = PolygonTools.SignedArea(clean[..count]) < 0;
        for (var i = 0; i < count; i++)
            shape.Points[i] = clean[reverse ? count - 1 - i : i];
        shape.ComputeNormals();
        return shape;
    }

    /// <summary>The point used as the shape's center, such as for continuous collision fallbacks.</summary>
    public readonly Vector2 Centroid
    {
        get
        {
            if (Type != ShapeType.Polygon)
                return Count == 1 ? Points[0] : (Points[0] + Points[1]) * 0.5f;
            var sum = Vector2.Zero;
            for (var i = 0; i < Count; i++)
                sum += Points[i];
            return sum / Count;
        }
    }

    public readonly bool IsInternal(int edge) => (InternalEdges & (1 << edge)) != 0;

    /// <summary>The shape moved into another frame.</summary>
    public readonly Shape Transform(in Xf xf)
    {
        var result = this;
        for (var i = 0; i < Count; i++)
        {
            result.Points[i] = xf.Apply(Points[i]);
            result.Normals[i] = xf.Q.Rotate(Normals[i]);
        }

        return result;
    }

    /// <summary>The shape scaled per axis; non-uniform scale turns circles and capsules by their largest factor.</summary>
    public readonly Shape Scale(Vector2 scale)
    {
        if (scale == Vector2.One)
            return this;
        var uniform = MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y));
        switch (Type)
        {
            case ShapeType.Circle:
                return Circle(Points[0] * scale, Radius * uniform);
            case ShapeType.Capsule:
                return Capsule(Points[0] * scale, Points[1] * scale, Radius * uniform);
            default:
                Span<Vector2> points = stackalloc Vector2[Count];
                for (var i = 0; i < Count; i++)
                    points[i] = Points[i] * scale;
                return Polygon(points);
        }
    }

    public readonly Aabb ComputeAabb()
    {
        var min = Points[0];
        var max = min;
        for (var i = 1; i < Count; i++)
        {
            min = Vector2.Min(min, Points[i]);
            max = Vector2.Max(max, Points[i]);
        }

        var r = new Vector2(Radius);
        return new Aabb(min - r, max + r);
    }

    /// <summary>The index of the point farthest along <paramref name="direction"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly int Support(Vector2 direction)
    {
        var best = 0;
        var bestValue = Vector2.Dot(Points[0], direction);
        for (var i = 1; i < Count; i++)
        {
            var value = Vector2.Dot(Points[i], direction);
            if (value > bestValue)
            {
                best = i;
                bestValue = value;
            }
        }

        return best;
    }

    public readonly bool ContainsPoint(Vector2 point)
    {
        switch (Type)
        {
            case ShapeType.Circle:
                return Vector2.DistanceSquared(point, Points[0]) <= Radius * Radius;
            case ShapeType.Capsule:
            {
                var closest = SegmentClosestPoint(Points[0], Points[1], point);
                return Vector2.DistanceSquared(point, closest) <= Radius * Radius;
            }
            default:
                for (var i = 0; i < Count; i++)
                {
                    if (Vector2.Dot(Normals[i], point - Points[i]) > Radius)
                        return false;
                }

                return true;
        }
    }

    public readonly MassData ComputeMass(float density)
    {
        switch (Type)
        {
            case ShapeType.Circle:
            {
                var rr = Radius * Radius;
                var mass = density * MathF.PI * rr;
                var center = Points[0];
                return new MassData(mass, center, mass * (0.5f * rr + Vector2.Dot(center, center)));
            }
            case ShapeType.Capsule:
            {
                var rr = Radius * Radius;
                var length = Vector2.Distance(Points[0], Points[1]);
                var circleMass = density * MathF.PI * rr;
                var boxMass = density * 2 * Radius * length;
                var mass = circleMass + boxMass;
                var center = (Points[0] + Points[1]) * 0.5f;
                var lc = 4 * Radius / (3 * MathF.PI);
                var h = 0.5f * length;
                var circleInertia = circleMass * (0.5f * rr + h * h + 2 * h * lc);
                var boxInertia = boxMass * (4 * rr + length * length) / 12;
                return new MassData(mass, center, circleInertia + boxInertia + mass * Vector2.Dot(center, center));
            }
            default:
                return PolygonMass(density);
        }
    }

    public static Vector2 SegmentClosestPoint(Vector2 a, Vector2 b, Vector2 point)
    {
        var e = b - a;
        var ee = Vector2.Dot(e, e);
        if (ee < 1e-12f)
            return a;
        var t = Math.Clamp(Vector2.Dot(point - a, e) / ee, 0, 1);
        return a + e * t;
    }

    private void ComputeNormals()
    {
        for (var i = 0; i < Count; i++)
        {
            var edge = Points[i + 1 < Count ? i + 1 : 0] - Points[i];
            Normals[i] = Vec.Normalize(Vec.Cross(edge, 1f));
        }
    }

    private readonly MassData PolygonMass(float density)
    {
        var origin = Points[0];
        var center = Vector2.Zero;
        var area = 0f;
        var inertia = 0f;
        const float inv3 = 1f / 3f;
        for (var i = 1; i < Count - 1; i++)
        {
            var e1 = Points[i] - origin;
            var e2 = Points[i + 1] - origin;
            var d = Vec.Cross(e1, e2);
            var triangleArea = 0.5f * d;
            area += triangleArea;
            center += triangleArea * inv3 * (e1 + e2);
            var intx2 = e1.X * e1.X + e2.X * e1.X + e2.X * e2.X;
            var inty2 = e1.Y * e1.Y + e2.Y * e1.Y + e2.Y * e2.Y;
            inertia += 0.25f * inv3 * d * (intx2 + inty2);
        }

        if (area <= 0)
            return new MassData(0, origin, 0);
        center /= area;
        var mass = density * area;
        var centroid = center + origin;
        var originInertia = density * inertia + mass * (Vector2.Dot(centroid, centroid) - Vector2.Dot(center, center));
        return new MassData(mass, centroid, originInertia);
    }
}
