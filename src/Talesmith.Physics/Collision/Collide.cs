using System.Numerics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Collision;

/// <summary>Contact manifolds between world-space shapes.</summary>
/// <remarks>
/// Points are kept while the surfaces are closer than <c>speculative</c>, so the solver stops shapes before they touch instead of
/// after they overlap. Polygons and capsules share a separating-axis test with edge clipping; edges flagged as internal (shared with
/// neighboring solid tiles) never become the contact normal, so bodies slide across tile seams without catching on them.
/// </remarks>
internal static class Collide
{
    public static void Shapes(in Shape a, in Shape b, float speculative, float slop, out Manifold manifold)
    {
        if (a.Type < b.Type)
        {
            Ordered(b, a, speculative, slop, out manifold);
            manifold.Flip();
            return;
        }

        Ordered(a, b, speculative, slop, out manifold);
    }

    private static void Ordered(in Shape a, in Shape b, float speculative, float slop, out Manifold manifold)
    {
        switch (a.Type, b.Type)
        {
            case (ShapeType.Circle, ShapeType.Circle):
                Circles(a.Points[0], a.Radius, b.Points[0], b.Radius, speculative, out manifold);
                return;
            case (ShapeType.Capsule, ShapeType.Circle):
            {
                var closest = Shape.SegmentClosestPoint(a.Points[0], a.Points[1], b.Points[0]);
                Circles(closest, a.Radius, b.Points[0], b.Radius, speculative, out manifold);
                return;
            }
            case (ShapeType.Polygon, ShapeType.Circle):
                PolygonAndCircle(a, b.Points[0], b.Radius, speculative, out manifold);
                return;
            default:
                Polygons(a, b, speculative, slop, out manifold);
                return;
        }
    }

    private static void Circles(Vector2 centerA, float radiusA, Vector2 centerB, float radiusB, float speculative, out Manifold manifold)
    {
        manifold = default;
        var normal = Vec.Normalize(centerB - centerA, out var distance);
        var separation = distance - radiusA - radiusB;
        if (separation > speculative)
            return;
        if (normal == Vector2.Zero)
            normal = new Vector2(0, -1);
        var surfaceA = centerA + radiusA * normal;
        var surfaceB = centerB - radiusB * normal;
        manifold.Normal = normal;
        manifold.Add((surfaceA + surfaceB) * 0.5f, separation, 0);
    }

    private static void PolygonAndCircle(in Shape polygon, Vector2 center, float circleRadius, float speculative, out Manifold manifold)
    {
        manifold = default;
        var radius = polygon.Radius + circleRadius;
        var edge = 0;
        var separation = float.MinValue;
        var allowedEdge = -1;
        var allowedSeparation = float.MinValue;
        for (var i = 0; i < polygon.Count; i++)
        {
            var s = Vector2.Dot(polygon.Normals[i], center - polygon.Points[i]);
            if (s > separation)
            {
                separation = s;
                edge = i;
            }

            if (!polygon.IsInternal(i) && s > allowedSeparation)
            {
                allowedSeparation = s;
                allowedEdge = i;
            }
        }

        if (separation - radius > speculative)
            return;
        if (allowedEdge >= 0 && allowedEdge != edge)
        {
            edge = allowedEdge;
            separation = allowedSeparation;
        }

        var next = edge + 1 < polygon.Count ? edge + 1 : 0;
        var previous = edge > 0 ? edge - 1 : polygon.Count - 1;
        var v1 = polygon.Points[edge];
        var v2 = polygon.Points[next];
        var u1 = Vector2.Dot(center - v1, v2 - v1);
        var u2 = Vector2.Dot(center - v2, v1 - v2);
        Vector2 normal;
        if (u1 < 0 && separation > float.Epsilon && !polygon.IsInternal(previous))
        {
            normal = Vec.Normalize(center - v1, out var distance);
            if (distance - radius > speculative)
                return;
            AddRounded(ref manifold, v1, polygon.Radius, center, circleRadius, normal, Manifold.MakeId(edge, 0));
        }
        else if (u2 < 0 && separation > float.Epsilon && !polygon.IsInternal(next))
        {
            normal = Vec.Normalize(center - v2, out var distance);
            if (distance - radius > speculative)
                return;
            AddRounded(ref manifold, v2, polygon.Radius, center, circleRadius, normal, Manifold.MakeId(next, 0));
        }
        else
        {
            normal = polygon.Normals[edge];
            var surfaceA = center + (polygon.Radius - Vector2.Dot(center - v1, normal)) * normal;
            var surfaceB = center - circleRadius * normal;
            manifold.Normal = normal;
            manifold.Add((surfaceA + surfaceB) * 0.5f, Vector2.Dot(surfaceB - surfaceA, normal), Manifold.MakeId(edge, 0));
        }
    }

    private static void AddRounded(ref Manifold manifold, Vector2 a, float radiusA, Vector2 b, float radiusB, Vector2 normal, ushort id)
    {
        var surfaceA = a + radiusA * normal;
        var surfaceB = b - radiusB * normal;
        manifold.Normal = normal;
        manifold.Add((surfaceA + surfaceB) * 0.5f, Vector2.Dot(surfaceB - surfaceA, normal), id);
    }

    /// <summary>Two convex polygons, either of which may be a rounded two-point capsule.</summary>
    private static void Polygons(in Shape a, in Shape b, float speculative, float slop, out Manifold manifold)
    {
        manifold = default;
        var trueA = MaxSeparation(a, b, out var edgeA, out var allowedA, out var allowedEdgeA);
        var trueB = MaxSeparation(b, a, out var edgeB, out var allowedB, out var allowedEdgeB);
        var radius = a.Radius + b.Radius;
        if (trueA > speculative + radius || trueB > speculative + radius)
            return;

        var separationA = trueA;
        var separationB = trueB;
        if (a.InternalEdges != 0 || b.InternalEdges != 0)
        {
            if (allowedEdgeA >= 0 || allowedEdgeB >= 0)
            {
                (edgeA, separationA) = allowedEdgeA >= 0 ? (allowedEdgeA, allowedA) : (0, float.MinValue);
                (edgeB, separationB) = allowedEdgeB >= 0 ? (allowedEdgeB, allowedB) : (0, float.MinValue);
            }
        }

        bool flip;
        if (separationB > separationA + 0.1f * slop)
        {
            flip = true;
            edgeA = IncidentEdge(a, b.Normals[edgeB]);
        }
        else
        {
            flip = false;
            edgeB = IncidentEdge(b, a.Normals[edgeA]);
        }

        if (MathF.Max(separationA, separationB) > 0.1f * slop)
        {
            var i11 = edgeA;
            var i12 = edgeA + 1 < a.Count ? edgeA + 1 : 0;
            var i21 = edgeB;
            var i22 = edgeB + 1 < b.Count ? edgeB + 1 : 0;
            var v11 = a.Points[i11];
            var v12 = a.Points[i12];
            var v21 = b.Points[i21];
            var v22 = b.Points[i22];
            SegmentDistance(v11, v12, v21, v22, out var f1, out var f2, out var distanceSquared);
            var vertexA = f1 == 0 ? i11 : f1 == 1 ? i12 : -1;
            var vertexB = f2 == 0 ? i21 : f2 == 1 ? i22 : -1;
            if (vertexA >= 0 && vertexB >= 0 && !EdgesOverlap(v11, v12, v21, v22, slop)
                && !TouchesInternalEdge(a, vertexA) && !TouchesInternalEdge(b, vertexB))
            {
                var distance = MathF.Sqrt(distanceSquared);
                if (distance > speculative + radius)
                    return;
                var p1 = a.Points[vertexA];
                var p2 = b.Points[vertexB];
                var normal = Vec.Normalize(p2 - p1);
                if (normal == Vector2.Zero)
                    return;
                AddRounded(ref manifold, p1, a.Radius, p2, b.Radius, normal, Manifold.MakeId(vertexA, vertexB));
                return;
            }
        }

        Clip(a, b, edgeA, edgeB, flip, speculative, ref manifold);
    }

    /// <summary>Whether the second edge, projected onto the first, overlaps it, so the shapes meet face to face rather than corner to corner.</summary>
    private static bool EdgesOverlap(Vector2 v11, Vector2 v12, Vector2 v21, Vector2 v22, float slop)
    {
        var tangent = Vec.Normalize(v12 - v11, out var length);
        var b1 = Vector2.Dot(v21 - v11, tangent);
        var b2 = Vector2.Dot(v22 - v11, tangent);
        return MathF.Min(length, MathF.Max(b1, b2)) - MathF.Max(0, MathF.Min(b1, b2)) > 0.1f * slop;
    }

    private static bool TouchesInternalEdge(in Shape shape, int vertex) =>
        shape.InternalEdges != 0 && (shape.IsInternal(vertex) || shape.IsInternal(vertex > 0 ? vertex - 1 : shape.Count - 1));

    /// <summary>Finds the edge of <paramref name="poly1"/> along which <paramref name="poly2"/> is farthest away.</summary>
    /// <returns>The largest separation over every edge; the allowed results skip edges that must not become the normal.</returns>
    private static float MaxSeparation(in Shape poly1, in Shape poly2, out int edge, out float allowedSeparation, out int allowedEdge)
    {
        edge = 0;
        allowedEdge = -1;
        allowedSeparation = float.MinValue;
        var maxSeparation = float.MinValue;
        for (var i = 0; i < poly1.Count; i++)
        {
            var n = poly1.Normals[i];
            var v = poly1.Points[i];
            var si = float.MaxValue;
            for (var j = 0; j < poly2.Count; j++)
                si = MathF.Min(si, Vector2.Dot(n, poly2.Points[j] - v));
            if (si > maxSeparation)
            {
                maxSeparation = si;
                edge = i;
            }

            if (si > allowedSeparation && IsAllowedAxis(poly1, i, poly2))
            {
                allowedSeparation = si;
                allowedEdge = i;
            }
        }

        return maxSeparation;
    }

    private static bool IsAllowedAxis(in Shape poly1, int edge, in Shape poly2)
    {
        if (poly1.IsInternal(edge))
            return false;
        if (poly2.InternalEdges == 0)
            return true;
        var n = poly1.Normals[edge];
        for (var j = 0; j < poly2.Count; j++)
        {
            if (poly2.IsInternal(j) && Vector2.Dot(n, poly2.Normals[j]) < -0.999f)
                return false;
        }

        return true;
    }

    private static int IncidentEdge(in Shape polygon, Vector2 referenceNormal)
    {
        var edge = 0;
        var minDot = float.MaxValue;
        for (var i = 0; i < polygon.Count; i++)
        {
            var dot = Vector2.Dot(referenceNormal, polygon.Normals[i]);
            if (dot < minDot)
            {
                minDot = dot;
                edge = i;
            }
        }

        return edge;
    }

    private static void Clip(in Shape a, in Shape b, int edgeA, int edgeB, bool flip, float speculative, ref Manifold manifold)
    {
        ref readonly var poly1 = ref flip ? ref b : ref a;
        ref readonly var poly2 = ref flip ? ref a : ref b;
        var i11 = flip ? edgeB : edgeA;
        var i12 = i11 + 1 < poly1.Count ? i11 + 1 : 0;
        var i21 = flip ? edgeA : edgeB;
        var i22 = i21 + 1 < poly2.Count ? i21 + 1 : 0;

        var normal = poly1.Normals[i11];
        var v11 = poly1.Points[i11];
        var v12 = poly1.Points[i12];
        var v21 = poly2.Points[i21];
        var v22 = poly2.Points[i22];
        var tangent = Vec.Cross(1f, normal);

        const float lower1 = 0;
        var upper1 = Vector2.Dot(v12 - v11, tangent);
        var upper2 = Vector2.Dot(v21 - v11, tangent);
        var lower2 = Vector2.Dot(v22 - v11, tangent);
        if (upper2 < lower1 - speculative || upper1 + speculative < lower2)
            return;

        var vLower = lower2 < lower1 && upper2 - lower2 > float.Epsilon ? Vector2.Lerp(v22, v21, (lower1 - lower2) / (upper2 - lower2)) : v22;
        var vUpper = upper2 > upper1 && upper2 - lower2 > float.Epsilon ? Vector2.Lerp(v22, v21, (upper1 - lower2) / (upper2 - lower2)) : v21;
        var separationLower = Vector2.Dot(vLower - v11, normal);
        var separationUpper = Vector2.Dot(vUpper - v11, normal);
        var r1 = poly1.Radius;
        var r2 = poly2.Radius;
        vLower += 0.5f * (r1 - r2 - separationLower) * normal;
        vUpper += 0.5f * (r1 - r2 - separationUpper) * normal;
        var radius = r1 + r2;

        if (!flip)
        {
            manifold.Normal = normal;
            if (separationLower - radius <= speculative)
                manifold.Add(vLower, separationLower - radius, Manifold.MakeId(i11, i22));
            if (separationUpper - radius <= speculative)
                manifold.Add(vUpper, separationUpper - radius, Manifold.MakeId(i12, i21));
        }
        else
        {
            manifold.Normal = -normal;
            if (separationUpper - radius <= speculative)
                manifold.Add(vUpper, separationUpper - radius, Manifold.MakeId(i21, i12));
            if (separationLower - radius <= speculative)
                manifold.Add(vLower, separationLower - radius, Manifold.MakeId(i22, i11));
        }
    }

    /// <summary>The closest points of two segments as fractions along each.</summary>
    public static void SegmentDistance(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2, out float fraction1, out float fraction2, out float distanceSquared)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var dd1 = Vector2.Dot(d1, d1);
        var dd2 = Vector2.Dot(d2, d2);
        var rd1 = Vector2.Dot(r, d1);
        var rd2 = Vector2.Dot(r, d2);
        const float epsSqr = float.Epsilon * float.Epsilon;

        if (dd1 < epsSqr || dd2 < epsSqr)
        {
            if (dd1 >= epsSqr)
            {
                fraction1 = Math.Clamp(-rd1 / dd1, 0, 1);
                fraction2 = 0;
            }
            else if (dd2 >= epsSqr)
            {
                fraction1 = 0;
                fraction2 = Math.Clamp(rd2 / dd2, 0, 1);
            }
            else
            {
                fraction1 = 0;
                fraction2 = 0;
            }
        }
        else
        {
            var d12 = Vector2.Dot(d1, d2);
            var denominator = dd1 * dd2 - d12 * d12;
            var f1 = denominator != 0 ? Math.Clamp((d12 * rd2 - rd1 * dd2) / denominator, 0, 1) : 0;
            var f2 = (d12 * f1 + rd2) / dd2;
            if (f2 < 0)
            {
                f2 = 0;
                f1 = Math.Clamp(-rd1 / dd1, 0, 1);
            }
            else if (f2 > 1)
            {
                f2 = 1;
                f1 = Math.Clamp((d12 - rd1) / dd1, 0, 1);
            }

            fraction1 = f1;
            fraction2 = f2;
        }

        distanceSquared = Vector2.DistanceSquared(p1 + fraction1 * d1, p2 + fraction2 * d2);
    }
}
