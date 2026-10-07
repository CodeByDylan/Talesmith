using System.Numerics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Broadphase;

/// <summary>The enlarged bounds broadphases store, shared so every broadphase finds the same pairs at the same time.</summary>
internal static class FatBounds
{
    private const float DisplacementMultiplier = 4;

    /// <summary>The bounds enlarged by <paramref name="margin"/> and stretched along the predicted motion.</summary>
    public static Aabb Compute(in Aabb aabb, Vector2 displacement, float margin)
    {
        var fat = aabb.Inflate(margin);
        var d = DisplacementMultiplier * displacement;
        if (d.X < 0)
            fat.Min.X += d.X;
        else
            fat.Max.X += d.X;
        if (d.Y < 0)
            fat.Min.Y += d.Y;
        else
            fat.Max.Y += d.Y;
        return fat;
    }

    /// <summary>Whether stored bounds still contain the shape and are not much larger than needed, as after fast motion stopped.</summary>
    public static bool StillFits(in Aabb stored, in Aabb aabb, in Aabb fat, float margin) =>
        stored.Contains(aabb) && fat.Inflate(4 * margin).Contains(stored);
}
