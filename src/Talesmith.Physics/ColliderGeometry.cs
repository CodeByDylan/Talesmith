using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Physics.Geometry;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics;

/// <summary>World-space geometry of colliders, for gizmos, debug views and editor tools; works without a running simulation.</summary>
public static class ColliderGeometry
{
    /// <summary>Points used for a full circle by default.</summary>
    public const int DefaultCircleSegments = 32;

    /// <summary>Writes the collider's outline in world space, in order around its edge.</summary>
    /// <param name="circleSegments">Points per full circle for circles and capsule ends.</param>
    /// <returns>The number of points written, or 0 when the collider is invalid or <paramref name="points"/> is too small.</returns>
    public static int GetOutline(in Collider2D collider, in Transform transform, Span<Vector2> points, int circleSegments = DefaultCircleSegments)
    {
        var scale = transform.Scale == Vector2.Zero ? Vector2.One : transform.Scale;
        var body = new Xf(transform.Position, transform.Rotation);
        if (collider.Shape == ColliderShape.Polygon)
        {
            if (collider.Points is not { Length: >= 3 } polygon || polygon.Length > points.Length)
                return 0;
            var local = new Xf(collider.Offset, collider.Rotation);
            for (var i = 0; i < polygon.Length; i++)
                points[i] = body.Apply(local.Apply(polygon[i]) * scale);
            return polygon.Length;
        }

        Shape shape;
        try
        {
            if (ColliderShapes.Primitive(collider.Shape, collider.Size, collider.Radius) is not { } primitive)
                return 0;
            shape = primitive.Transform(new Xf(collider.Offset, collider.Rotation)).Scale(scale).Transform(body);
        }
        catch (ArgumentException)
        {
            return 0;
        }

        return GetOutline(shape, points, circleSegments);
    }

    /// <summary>The world-space bounding box of the collider, or <see cref="Rect2.Empty"/> when it is invalid.</summary>
    public static Rect2 GetBounds(in Collider2D collider, in Transform transform)
    {
        Span<Vector2> points = stackalloc Vector2[Math.Max(DefaultCircleSegments, collider.Points?.Length ?? 0)];
        var count = GetOutline(collider, transform, points);
        return count == 0 ? Rect2.Empty : Rect2.Bounding(points[..count]);
    }

    /// <summary>Checks whether the collider can be simulated.</summary>
    /// <param name="error">Why it cannot, such as a polygon whose edges cross.</param>
    public static bool IsValid(in Collider2D collider, out string? error)
    {
        try
        {
            ColliderShapes.Build(collider, Vector2.One, []);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static int GetOutline(in Shape shape, Span<Vector2> points, int circleSegments)
    {
        switch (shape.Type)
        {
            case ShapeType.Circle:
            {
                var count = Math.Min(Math.Max(circleSegments, 8), points.Length);
                for (var i = 0; i < count; i++)
                {
                    var angle = 2 * MathF.PI * i / count;
                    points[i] = shape.Points[0] + shape.Radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }

                return count;
            }
            case ShapeType.Capsule:
            {
                var half = Math.Max(Math.Max(circleSegments, 8) / 2, 2);
                if (points.Length < 2 * (half + 1))
                    return 0;
                var axis = Vec.Normalize(shape.Points[1] - shape.Points[0]);
                var start = MathF.Atan2(axis.Y, axis.X) - MathF.PI / 2;
                var count = 0;
                for (var end = 0; end < 2; end++)
                {
                    var center = shape.Points[end == 0 ? 1 : 0];
                    var offset = end == 0 ? 0 : MathF.PI;
                    for (var i = 0; i <= half; i++)
                    {
                        var angle = start + offset + MathF.PI * i / half;
                        points[count++] = center + shape.Radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    }
                }

                return count;
            }
            default:
                if (points.Length < shape.Count)
                    return 0;
                for (var i = 0; i < shape.Count; i++)
                    points[i] = shape.Points[i];
                return shape.Count;
        }
    }
}
