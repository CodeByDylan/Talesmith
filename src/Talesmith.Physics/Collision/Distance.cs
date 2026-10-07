using System.Numerics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Collision;

/// <summary>The closest points of two convex shapes' cores, ignoring their radii.</summary>
/// <param name="Normal">Points from <see cref="PointA"/> toward <see cref="PointB"/>; zero when the cores overlap.</param>
internal readonly record struct DistanceResult(Vector2 PointA, Vector2 PointB, Vector2 Normal, float Distance);

/// <summary>The time of impact of a shape moving in a straight line toward another.</summary>
/// <param name="Normal">Points from the obstacle toward the moving shape.</param>
/// <param name="StartedInside">The shapes were already closer than the target gap and moving closer.</param>
internal readonly record struct CastResult(bool Hit, float Fraction, Vector2 Point, Vector2 Normal, bool StartedInside);

/// <summary>GJK distance between convex shapes and conservative-advancement shape casts.</summary>
internal static class Distance
{
    private const int MaxIterations = 20;

    /// <summary>Computes the distance between the cores of <paramref name="a"/> and of <paramref name="b"/> moved by <paramref name="offsetB"/>.</summary>
    public static DistanceResult Compute(in Shape a, in Shape b, Vector2 offsetB)
    {
        var simplex = new Simplex();
        simplex.V1 = MakeVertex(a, 0, b, 0, offsetB);
        simplex.Count = 1;

        Span<int> saveA = stackalloc int[3];
        Span<int> saveB = stackalloc int[3];
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var saveCount = simplex.Count;
            for (var i = 0; i < saveCount; i++)
            {
                ref readonly var v = ref simplex.Vertex(i);
                saveA[i] = v.IndexA;
                saveB[i] = v.IndexB;
            }

            switch (simplex.Count)
            {
                case 2:
                    simplex.Solve2();
                    break;
                case 3:
                    simplex.Solve3();
                    break;
            }

            if (simplex.Count == 3)
                break;

            var d = simplex.SearchDirection();
            if (d.LengthSquared() < float.Epsilon * float.Epsilon)
                break;

            var indexA = a.Support(-d);
            var indexB = b.Support(d);
            var duplicate = false;
            for (var i = 0; i < saveCount; i++)
            {
                if (saveA[i] == indexA && saveB[i] == indexB)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
                break;
            simplex.Vertex(simplex.Count) = MakeVertex(a, indexA, b, indexB, offsetB);
            simplex.Count++;
        }

        simplex.WitnessPoints(out var pointA, out var pointB);
        var normal = Vec.Normalize(pointB - pointA, out var distance);
        return new DistanceResult(pointA, pointB, normal, simplex.Count == 3 ? 0 : distance);
    }

    /// <summary>Moves <paramref name="mover"/> along <paramref name="translation"/> until its surface comes within <paramref name="gap"/> of <paramref name="obstacle"/>.</summary>
    /// <param name="gap">The surface distance to stop at; at least <paramref name="minimumGap"/> because touching cores have no normal.</param>
    public static CastResult Cast(in Shape obstacle, in Shape mover, Vector2 translation, float maxFraction, float gap, float minimumGap)
    {
        var totalRadius = obstacle.Radius + mover.Radius;
        var target = MathF.Max(minimumGap, totalRadius + gap);
        var tolerance = 0.25f * minimumGap;
        var fraction = 0f;
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var result = Compute(obstacle, mover, translation * fraction);
            if (result.Distance < target + tolerance)
            {
                var normal = result.Normal;
                var point = result.PointA + obstacle.Radius * normal;
                if (iteration == 0 && result.Distance <= tolerance)
                {
                    normal = TouchNormal(obstacle, mover, minimumGap);
                    point = result.PointA;
                    if (normal == Vector2.Zero)
                        return new CastResult(true, 0, point, Vector2.Zero, true);
                }
                else if (result.Distance <= 0 || normal == Vector2.Zero)
                {
                    return new CastResult(true, fraction, result.PointA, Vector2.Zero, false);
                }

                if (iteration == 0)
                {
                    var approach = Vector2.Dot(translation, normal);
                    if (approach >= -1e-5f * translation.Length())
                        return default;
                    return new CastResult(true, 0, point, normal, true);
                }

                return new CastResult(true, fraction, point, normal, false);
            }

            var denominator = Vector2.Dot(translation, result.Normal);
            if (denominator >= 0)
                return default;
            fraction += (target - result.Distance) / denominator;
            if (fraction >= maxFraction)
                return default;
        }

        return default;
    }

    /// <summary>The contact normal of cores that touch without overlapping by more than <paramref name="tolerance"/>, which GJK cannot give; zero otherwise.</summary>
    private static Vector2 TouchNormal(in Shape obstacle, in Shape mover, float tolerance)
    {
        Collide.Shapes(obstacle, mover, tolerance, tolerance, out var manifold);
        return manifold.PointCount > 0 && manifold.MinSeparation >= -tolerance ? manifold.Normal : Vector2.Zero;
    }

    private static SimplexVertex MakeVertex(in Shape a, int indexA, in Shape b, int indexB, Vector2 offsetB)
    {
        var wA = a.Points[indexA];
        var wB = b.Points[indexB] + offsetB;
        return new SimplexVertex { WA = wA, WB = wB, W = wB - wA, A = 1, IndexA = indexA, IndexB = indexB };
    }

    private struct SimplexVertex
    {
        public Vector2 WA;
        public Vector2 WB;
        public Vector2 W;
        public float A;
        public int IndexA;
        public int IndexB;
    }

    private struct Simplex
    {
        public SimplexVertex V1;
        public SimplexVertex V2;
        public SimplexVertex V3;
        public int Count;

        [System.Diagnostics.CodeAnalysis.UnscopedRef]
        public ref SimplexVertex Vertex(int index)
        {
            switch (index)
            {
                case 0:
                    return ref V1;
                case 1:
                    return ref V2;
                default:
                    return ref V3;
            }
        }

        public readonly Vector2 SearchDirection()
        {
            if (Count == 1)
                return -V1.W;
            var e12 = V2.W - V1.W;
            return Vec.Cross(e12, -V1.W) > 0 ? Vec.Cross(1f, e12) : Vec.Cross(e12, 1f);
        }

        public readonly void WitnessPoints(out Vector2 a, out Vector2 b)
        {
            switch (Count)
            {
                case 1:
                    a = V1.WA;
                    b = V1.WB;
                    return;
                case 2:
                    a = V1.A * V1.WA + V2.A * V2.WA;
                    b = V1.A * V1.WB + V2.A * V2.WB;
                    return;
                default:
                    a = V1.A * V1.WA + V2.A * V2.WA + V3.A * V3.WA;
                    b = a;
                    return;
            }
        }

        public void Solve2()
        {
            var w1 = V1.W;
            var w2 = V2.W;
            var e12 = w2 - w1;
            var d12n2 = -Vector2.Dot(w1, e12);
            if (d12n2 <= 0)
            {
                V1.A = 1;
                Count = 1;
                return;
            }

            var d12n1 = Vector2.Dot(w2, e12);
            if (d12n1 <= 0)
            {
                V2.A = 1;
                Count = 1;
                V1 = V2;
                return;
            }

            var inv = 1 / (d12n1 + d12n2);
            V1.A = d12n1 * inv;
            V2.A = d12n2 * inv;
            Count = 2;
        }

        public void Solve3()
        {
            var w1 = V1.W;
            var w2 = V2.W;
            var w3 = V3.W;

            var e12 = w2 - w1;
            var d12n1 = Vector2.Dot(w2, e12);
            var d12n2 = -Vector2.Dot(w1, e12);

            var e13 = w3 - w1;
            var d13n1 = Vector2.Dot(w3, e13);
            var d13n2 = -Vector2.Dot(w1, e13);

            var e23 = w3 - w2;
            var d23n1 = Vector2.Dot(w3, e23);
            var d23n2 = -Vector2.Dot(w2, e23);

            var n123 = Vec.Cross(e12, e13);
            var d123n1 = n123 * Vec.Cross(w2, w3);
            var d123n2 = n123 * Vec.Cross(w3, w1);
            var d123n3 = n123 * Vec.Cross(w1, w2);

            if (d12n2 <= 0 && d13n2 <= 0)
            {
                V1.A = 1;
                Count = 1;
                return;
            }

            if (d12n1 > 0 && d12n2 > 0 && d123n3 <= 0)
            {
                var inv = 1 / (d12n1 + d12n2);
                V1.A = d12n1 * inv;
                V2.A = d12n2 * inv;
                Count = 2;
                return;
            }

            if (d13n1 > 0 && d13n2 > 0 && d123n2 <= 0)
            {
                var inv = 1 / (d13n1 + d13n2);
                V1.A = d13n1 * inv;
                V3.A = d13n2 * inv;
                Count = 2;
                V2 = V3;
                return;
            }

            if (d12n1 <= 0 && d23n2 <= 0)
            {
                V2.A = 1;
                Count = 1;
                V1 = V2;
                return;
            }

            if (d13n1 <= 0 && d23n1 <= 0)
            {
                V3.A = 1;
                Count = 1;
                V1 = V3;
                return;
            }

            if (d23n1 > 0 && d23n2 > 0 && d123n1 <= 0)
            {
                var inv = 1 / (d23n1 + d23n2);
                V2.A = d23n1 * inv;
                V3.A = d23n2 * inv;
                Count = 2;
                V1 = V3;
                return;
            }

            var invTotal = 1 / (d123n1 + d123n2 + d123n3);
            V1.A = d123n1 * invTotal;
            V2.A = d123n2 * invTotal;
            V3.A = d123n3 * invTotal;
            Count = 3;
        }
    }
}
