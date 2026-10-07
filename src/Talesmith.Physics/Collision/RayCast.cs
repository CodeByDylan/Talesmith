using System.Numerics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Collision;

/// <summary>Where a ray first enters a shape.</summary>
internal readonly record struct RayResult(bool Hit, float Fraction, Vector2 Point, Vector2 Normal);

/// <summary>Ray casts against world-space shapes; rays starting inside a shape do not hit it.</summary>
internal static class RayCast
{
    public static RayResult Shape(in Shape shape, Vector2 origin, Vector2 translation, float maxFraction) => shape.Type switch
    {
        ShapeType.Circle => Circle(shape.Points[0], shape.Radius, origin, translation, maxFraction),
        ShapeType.Capsule => Capsule(shape.Points[0], shape.Points[1], shape.Radius, origin, translation, maxFraction),
        _ => Polygon(shape, origin, translation, maxFraction)
    };

    private static RayResult Circle(Vector2 center, float radius, Vector2 origin, Vector2 translation, float maxFraction)
    {
        var s = origin - center;
        var d = Vec.Normalize(translation, out var length);
        if (length == 0)
            return default;
        var t = -Vector2.Dot(s, d);
        var c = s + t * d;
        var cc = Vector2.Dot(c, c);
        var rr = radius * radius;
        if (cc > rr)
            return default;
        var h = MathF.Sqrt(rr - cc);
        var fraction = t - h;
        if (fraction < 0 || fraction > maxFraction * length)
            return default;
        var hit = s + fraction * d;
        var normal = Vec.Normalize(hit);
        return new RayResult(true, fraction / length, center + radius * normal, normal);
    }

    private static RayResult Capsule(Vector2 v1, Vector2 v2, float radius, Vector2 origin, Vector2 translation, float maxFraction)
    {
        var a = Vec.Normalize(v2 - v1, out var capsuleLength);
        if (capsuleLength < float.Epsilon)
            return Circle(v1, radius, origin, translation, maxFraction);

        var q = origin - v1;
        var qa = Vector2.Dot(q, a);
        var qp = q - qa * a;
        if (Vector2.Dot(qp, qp) < radius * radius)
        {
            if (qa < 0)
                return Circle(v1, radius, origin, translation, maxFraction);
            if (qa > capsuleLength)
                return Circle(v2, radius, origin, translation, maxFraction);
            return default;
        }

        var n = new Vector2(a.Y, -a.X);
        var u = Vec.Normalize(translation, out var rayLength);
        if (rayLength == 0)
            return default;
        var denominator = -a.X * u.Y + u.X * a.Y;
        if (MathF.Abs(denominator) < float.Epsilon)
            return default;

        var b1 = q - radius * n;
        var b2 = q + radius * n;
        var inv = 1 / denominator;
        var s21 = (a.X * b1.Y - b1.X * a.Y) * inv;
        var s22 = (a.X * b2.Y - b2.X * a.Y) * inv;
        float s2;
        Vector2 b;
        if (s21 < s22)
        {
            s2 = s21;
            b = b1;
        }
        else
        {
            s2 = s22;
            b = b2;
            n = -n;
        }

        if (s2 < 0 || maxFraction * rayLength < s2)
            return default;

        var s1 = (-b.X * u.Y + u.X * b.Y) * inv;
        if (s1 < 0)
            return Circle(v1, radius, origin, translation, maxFraction);
        if (capsuleLength < s1)
            return Circle(v2, radius, origin, translation, maxFraction);
        return new RayResult(true, s2 / rayLength, Vector2.Lerp(v1, v2, s1 / capsuleLength) + radius * n, n);
    }

    private static RayResult Polygon(in Shape polygon, Vector2 origin, Vector2 translation, float maxFraction)
    {
        var lower = 0f;
        var upper = maxFraction;
        var index = -1;
        for (var i = 0; i < polygon.Count; i++)
        {
            var numerator = Vector2.Dot(polygon.Normals[i], polygon.Points[i] - origin);
            var denominator = Vector2.Dot(polygon.Normals[i], translation);
            if (denominator == 0)
            {
                if (numerator < 0)
                    return default;
            }
            else if (denominator < 0 && numerator < lower * denominator)
            {
                lower = numerator / denominator;
                index = i;
            }
            else if (denominator > 0 && numerator < upper * denominator)
            {
                upper = numerator / denominator;
            }

            if (upper < lower)
                return default;
        }

        return index >= 0 ? new RayResult(true, lower, origin + lower * translation, polygon.Normals[index]) : default;
    }
}
