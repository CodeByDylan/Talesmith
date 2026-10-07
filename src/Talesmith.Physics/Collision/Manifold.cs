using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talesmith.Physics.Collision;

/// <summary>One contact point between two shapes.</summary>
internal struct ManifoldPoint
{
    /// <summary>The world point midway between the two surfaces.</summary>
    public Vector2 Point;

    /// <summary>Negative when the shapes overlap.</summary>
    public float Separation;

    /// <summary>Identifies the features that produced the point, so impulses carry over between steps.</summary>
    public ushort Id;

    /// <summary>The impulses of the last solver substep, which warm start the next step.</summary>
    public float NormalImpulse;
    public float TangentImpulse;

    /// <summary>The normal impulse applied over the whole last step.</summary>
    public float TotalNormalImpulse;

    /// <summary>Whether the impulses were carried over from the previous step.</summary>
    public bool Persisted;
}

[InlineArray(2)]
internal struct ManifoldPoints
{
    private ManifoldPoint _element;
}

/// <summary>The contact points of two shapes and the normal pointing from the first to the second.</summary>
internal struct Manifold
{
    public Vector2 Normal;
    public int PointCount;
    public ManifoldPoints Points;

    public static ushort MakeId(int a, int b) => (ushort)((a & 0xFF) << 8 | (b & 0xFF));

    public void Add(Vector2 point, float separation, ushort id)
    {
        ref var p = ref Points[PointCount++];
        p.Point = point;
        p.Separation = separation;
        p.Id = id;
        p.NormalImpulse = 0;
        p.TangentImpulse = 0;
        p.TotalNormalImpulse = 0;
        p.Persisted = false;
    }

    public readonly float MinSeparation
    {
        get
        {
            var min = float.MaxValue;
            for (var i = 0; i < PointCount; i++)
                min = MathF.Min(min, Points[i].Separation);
            return min;
        }
    }

    /// <summary>The manifold seen from the other shape: the normal flips and feature ids swap.</summary>
    public void Flip()
    {
        Normal = -Normal;
        for (var i = 0; i < PointCount; i++)
        {
            ref var p = ref Points[i];
            p.Id = (ushort)((p.Id >> 8) | ((p.Id & 0xFF) << 8));
        }
    }
}
