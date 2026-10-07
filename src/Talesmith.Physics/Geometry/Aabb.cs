using System.Numerics;
using System.Runtime.CompilerServices;
using Talesmith.Mathematics;

namespace Talesmith.Physics.Geometry;

/// <summary>An axis-aligned bounding box given by its corners.</summary>
internal struct Aabb(Vector2 min, Vector2 max)
{
    public Vector2 Min = min;
    public Vector2 Max = max;

    public static Aabb Empty => new(new Vector2(float.MaxValue), new Vector2(float.MinValue));

    public readonly Vector2 Center => (Min + Max) * 0.5f;

    public readonly Vector2 Extents => (Max - Min) * 0.5f;

    public readonly float Perimeter => 2 * (Max.X - Min.X + Max.Y - Min.Y);

    public readonly Rect2 ToRect() => Rect2.FromEdges(Min.X, Min.Y, Max.X, Max.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aabb Union(in Aabb a, in Aabb b) => new(Vector2.Min(a.Min, b.Min), Vector2.Max(a.Max, b.Max));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Overlaps(in Aabb other) =>
        Min.X <= other.Max.X && other.Min.X <= Max.X && Min.Y <= other.Max.Y && other.Min.Y <= Max.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Contains(in Aabb other) =>
        Min.X <= other.Min.X && Min.Y <= other.Min.Y && other.Max.X <= Max.X && other.Max.Y <= Max.Y;

    public readonly bool Contains(Vector2 point) => point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

    public readonly Aabb Inflate(float amount) => new(Min - new Vector2(amount), Max + new Vector2(amount));

    public readonly Aabb Offset(Vector2 amount) => new(Min + amount, Max + amount);

    /// <summary>The box swept along <paramref name="translation"/>.</summary>
    public readonly Aabb Sweep(Vector2 translation) => Union(this, Offset(translation));

    /// <summary>Clips a ray against the box with the slab test.</summary>
    /// <returns>Whether the ray enters the box before <paramref name="maxFraction"/>.</returns>
    public readonly bool RayOverlaps(Vector2 origin, Vector2 translation, float maxFraction)
    {
        var tMin = 0f;
        var tMax = maxFraction;
        for (var axis = 0; axis < 2; axis++)
        {
            var o = axis == 0 ? origin.X : origin.Y;
            var d = axis == 0 ? translation.X : translation.Y;
            var lo = axis == 0 ? Min.X : Min.Y;
            var hi = axis == 0 ? Max.X : Max.Y;
            if (MathF.Abs(d) < 1e-12f)
            {
                if (o < lo || o > hi)
                    return false;
                continue;
            }

            var inv = 1 / d;
            var t1 = (lo - o) * inv;
            var t2 = (hi - o) * inv;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax)
                return false;
        }

        return true;
    }
}
