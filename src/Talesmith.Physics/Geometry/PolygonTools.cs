using System.Numerics;

namespace Talesmith.Physics.Geometry;

/// <summary>Validation, cleanup and convex decomposition of simple polygons.</summary>
internal static class PolygonTools
{
    private const float Epsilon = 1e-4f;

    public static float SignedArea(ReadOnlySpan<Vector2> points)
    {
        var area = 0f;
        for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            area += Vec.Cross(points[j], points[i]);
        return area * 0.5f;
    }

    /// <summary>Copies the points without duplicates and without points on a straight line between their neighbors.</summary>
    /// <returns>The number of points written to <paramref name="output"/>.</returns>
    public static int Simplify(ReadOnlySpan<Vector2> points, Span<Vector2> output)
    {
        var count = 0;
        foreach (var point in points)
        {
            if (count == 0 || Vector2.DistanceSquared(output[count - 1], point) > Epsilon * Epsilon)
                output[count++] = point;
        }

        while (count > 1 && Vector2.DistanceSquared(output[0], output[count - 1]) <= Epsilon * Epsilon)
            count--;

        var removed = true;
        while (removed && count >= 3)
        {
            removed = false;
            for (var i = 0; i < count; i++)
            {
                var previous = output[(i + count - 1) % count];
                var next = output[(i + 1) % count];
                var edge = next - previous;
                var length = edge.Length();
                if (length > 0 && MathF.Abs(Vec.Cross(edge, output[i] - previous)) / length > Epsilon)
                    continue;
                for (var j = i; j < count - 1; j++)
                    output[j] = output[j + 1];
                count--;
                removed = true;
                break;
            }
        }

        return count;
    }

    /// <summary>Whether a polygon is convex and simple, in either winding.</summary>
    public static bool IsConvex(ReadOnlySpan<Vector2> points)
    {
        if (points.Length < 3)
            return false;
        var sign = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            var c = points[(i + 2) % points.Length];
            var cross = Vec.Cross(b - a, c - b);
            if (MathF.Abs(cross) <= Epsilon)
                continue;
            var current = MathF.Sign(cross);
            if (sign == 0)
                sign = current;
            else if (current != sign)
                return false;
        }

        // Turning the same way at every vertex still allows star polygons that wind twice; their area differs from their hull's.
        Span<Vector2> hull = stackalloc Vector2[points.Length + 1];
        var hullCount = ConvexHull(points, hull);
        return MathF.Abs(MathF.Abs(SignedArea(points)) - MathF.Abs(SignedArea(hull[..hullCount]))) <= Epsilon * MathF.Max(1, MathF.Abs(SignedArea(points)));
    }

    /// <summary>Andrew's monotone chain; writes the hull in positive winding and returns its point count.</summary>
    public static int ConvexHull(ReadOnlySpan<Vector2> points, Span<Vector2> hull)
    {
        Span<Vector2> sorted = stackalloc Vector2[points.Length];
        points.CopyTo(sorted);
        sorted.Sort(static (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        if (sorted.Length < 3)
        {
            sorted.CopyTo(hull);
            return sorted.Length;
        }

        var k = 0;
        for (var i = 0; i < sorted.Length; i++)
        {
            while (k >= 2 && Vec.Cross(hull[k - 1] - hull[k - 2], sorted[i] - hull[k - 2]) <= 0)
                k--;
            hull[k++] = sorted[i];
        }

        for (int i = sorted.Length - 2, lower = k + 1; i >= 0; i--)
        {
            while (k >= lower && Vec.Cross(hull[k - 1] - hull[k - 2], sorted[i] - hull[k - 2]) <= 0)
                k--;
            hull[k++] = sorted[i];
        }

        return k - 1;
    }

    /// <summary>Whether any two non-adjacent edges cross.</summary>
    public static bool SelfIntersects(ReadOnlySpan<Vector2> points)
    {
        var n = points.Length;
        for (var i = 0; i < n; i++)
        {
            var a1 = points[i];
            var a2 = points[(i + 1) % n];
            for (var j = i + 1; j < n; j++)
            {
                if (j == i || (j + 1) % n == i || (i + 1) % n == j)
                    continue;
                if (SegmentsIntersect(a1, a2, points[j], points[(j + 1) % n]))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Splits a simple polygon into convex pieces of at most <see cref="Shape.MaxVertices"/> points.</summary>
    /// <exception cref="ArgumentException">The polygon has fewer than three points, crosses itself or has no area.</exception>
    public static List<Vector2[]> Decompose(ReadOnlySpan<Vector2> points)
    {
        var clean = new Vector2[points.Length];
        var count = Simplify(points, clean);
        if (count < 3)
            throw new ArgumentException("A polygon needs at least three points that are not on one line.");
        var polygon = clean.AsSpan(0, count);
        if (SelfIntersects(polygon))
            throw new ArgumentException("The polygon's edges cross each other; draw its outline without crossings.");
        if (SignedArea(polygon) < 0)
            polygon.Reverse();

        if (count <= Shape.MaxVertices && IsConvex(polygon))
            return [polygon.ToArray()];

        var pieces = Triangulate(polygon);
        MergeConvex(pieces, polygon);
        var result = new List<Vector2[]>(pieces.Count);
        foreach (var piece in pieces)
        {
            var vertices = new Vector2[piece.Count];
            for (var i = 0; i < piece.Count; i++)
                vertices[i] = polygon[piece[i]];
            result.Add(vertices);
        }

        return result;
    }

    private static List<List<int>> Triangulate(ReadOnlySpan<Vector2> polygon)
    {
        var remaining = new List<int>(polygon.Length);
        for (var i = 0; i < polygon.Length; i++)
            remaining.Add(i);

        var triangles = new List<List<int>>();
        var guard = polygon.Length * polygon.Length;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                var ib = remaining[i];
                var ic = remaining[(i + 1) % remaining.Count];
                if (!IsEar(polygon, remaining, ia, ib, ic))
                    continue;
                triangles.Add([ia, ib, ic]);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
                throw new ArgumentException("The polygon could not be split into convex pieces.");
        }

        if (remaining.Count == 3)
            triangles.Add([remaining[0], remaining[1], remaining[2]]);
        return triangles;
    }

    private static bool IsEar(ReadOnlySpan<Vector2> polygon, List<int> remaining, int ia, int ib, int ic)
    {
        var a = polygon[ia];
        var b = polygon[ib];
        var c = polygon[ic];
        if (Vec.Cross(b - a, c - b) <= Epsilon)
            return false;
        foreach (var index in remaining)
        {
            if (index == ia || index == ib || index == ic)
                continue;
            var p = polygon[index];
            if (Vec.Cross(b - a, p - a) >= 0 && Vec.Cross(c - b, p - b) >= 0 && Vec.Cross(a - c, p - c) >= 0)
                return false;
        }

        return true;
    }

    /// <summary>Hertel-Mehlhorn: removes diagonals between pieces while the merged piece stays convex and small enough.</summary>
    private static void MergeConvex(List<List<int>> pieces, ReadOnlySpan<Vector2> polygon)
    {
        Span<Vector2> buffer = stackalloc Vector2[Shape.MaxVertices];
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < pieces.Count && !merged; i++)
            {
                for (var j = i + 1; j < pieces.Count && !merged; j++)
                {
                    if (TryMerge(pieces[i], pieces[j], polygon, buffer) is not { } combined)
                        continue;
                    pieces[i] = combined;
                    pieces.RemoveAt(j);
                    merged = true;
                }
            }
        }
    }

    private static List<int>? TryMerge(List<int> a, List<int> b, ReadOnlySpan<Vector2> polygon, Span<Vector2> buffer)
    {
        if (a.Count + b.Count - 2 > Shape.MaxVertices)
            return null;
        for (var i = 0; i < a.Count; i++)
        {
            var a1 = a[i];
            var a2 = a[(i + 1) % a.Count];
            for (var j = 0; j < b.Count; j++)
            {
                if (b[j] != a2 || b[(j + 1) % b.Count] != a1)
                    continue;

                var combined = new List<int>(a.Count + b.Count - 2);
                for (var k = 0; k < a.Count; k++)
                    combined.Add(a[(i + 1 + k) % a.Count]);
                for (var k = 2; k < b.Count; k++)
                    combined.Add(b[(j + k) % b.Count]);
                for (var k = 0; k < combined.Count; k++)
                    buffer[k] = polygon[combined[k]];
                return IsConvex(buffer[..combined.Count]) ? combined : null;
            }
        }

        return null;
    }

    private static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        var d1 = Vec.Cross(q2 - q1, p1 - q1);
        var d2 = Vec.Cross(q2 - q1, p2 - q1);
        var d3 = Vec.Cross(p2 - p1, q1 - p1);
        var d4 = Vec.Cross(p2 - p1, q2 - p1);
        return ((d1 > Epsilon && d2 < -Epsilon) || (d1 < -Epsilon && d2 > Epsilon)) && ((d3 > Epsilon && d4 < -Epsilon) || (d3 < -Epsilon && d4 > Epsilon));
    }
}
