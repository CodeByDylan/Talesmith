using System.Numerics;
using Talesmith.Authoring;

namespace Talesmith.Physics;

/// <summary>The outline of a <see cref="Collider2D"/>.</summary>
public enum ColliderShape
{
    /// <summary>A rectangle of <see cref="Collider2D.Size"/>.</summary>
    Box,

    /// <summary>A circle of <see cref="Collider2D.Radius"/>.</summary>
    Circle,

    /// <summary>A rectangle of <see cref="Collider2D.Size"/> with fully rounded ends along its longer side.</summary>
    Capsule,

    /// <summary>The outline in <see cref="Collider2D.Points"/>; concave outlines are split into convex pieces.</summary>
    Polygon
}

/// <summary>The solid shape of an entity, or an area that reports overlaps when it is a trigger.</summary>
/// <remarks>
/// Without a <see cref="Rigidbody2D"/> the collider is static: it follows its <see cref="Runtime.Components.Transform"/> when moved but
/// never pushes anything. The shape is placed at the transform's position plus <see cref="Offset"/>, turned by the transform's rotation
/// plus <see cref="Rotation"/>, and scaled by the transform's scale.
/// </remarks>
[Component("Collider 2D", Category = "Physics", Icon = "shapes", Description = "A solid shape for collisions, or a trigger area.")]
public struct Collider2D
{
    public ColliderShape Shape = ColliderShape.Box;

    [Tooltip("Width and height of a box or capsule, in world units.")]
    [Range(0)]
    public Vector2 Size = new(32, 32);

    [Tooltip("Radius of a circle, in world units.")]
    [Range(0)]
    public float Radius = 16;

    [Tooltip("Outline of a polygon relative to the entity, in order around its edge.")]
    public Vector2[]? Points;

    [Tooltip("Where the shape's center is relative to the entity, before rotation.")]
    public Vector2 Offset;

    [Tooltip("Turns the shape relative to the entity.")]
    [Angle]
    public float Rotation;

    [Header("Behavior")]
    [Tooltip("Reports overlaps through trigger events instead of blocking.")]
    public bool IsTrigger;

    [Tooltip("Blocks only from the side facing the entity's up direction (negative Y), like a platform that can be jumped through.")]
    public bool OneWay;

    [Header("Material")]
    [Range(0, 1, Step = 0.05)]
    public float Friction = 0.4f;

    [Tooltip("Bounciness: 0 stops dead, 1 bounces back at full speed.")]
    [Range(0, 1, Step = 0.05)]
    public float Restitution;

    [Tooltip("Mass per square world unit, used when the rigid body computes its mass from its colliders.")]
    [Range(0)]
    public float Density = 1;

    [Header("Filtering")]
    [Tooltip("The collision layer this collider is on.")]
    [Range(0, PhysicsLayers.Count - 1, Step = 1)]
    [Layer]
    public int Layer;

    [Tooltip("Bit mask of the layers this collider collides with; -1 collides with every layer.")]
    [LayerMask]
    public int CollisionMask = PhysicsLayers.All;

    public Collider2D()
    {
    }

    public static Collider2D Box(Vector2 size) => new() { Shape = ColliderShape.Box, Size = size };

    public static Collider2D Circle(float radius) => new() { Shape = ColliderShape.Circle, Radius = radius };

    /// <param name="size">The capsule's bounding size; its ends are rounded along the longer side.</param>
    public static Collider2D Capsule(Vector2 size) => new() { Shape = ColliderShape.Capsule, Size = size };

    public static Collider2D Polygon(params Vector2[] points) => new() { Shape = ColliderShape.Polygon, Points = points };
}

/// <summary>Helpers for the 32 collision layers.</summary>
public static class PhysicsLayers
{
    public const int Count = 32;

    /// <summary>A mask containing every layer.</summary>
    public const int All = -1;

    /// <summary>A mask containing only <paramref name="layer"/>.</summary>
    public static int Mask(int layer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(layer);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(layer, Count);
        return 1 << layer;
    }

    /// <summary>A mask containing every listed layer.</summary>
    public static int Mask(params ReadOnlySpan<int> layers)
    {
        var mask = 0;
        foreach (var layer in layers)
            mask |= Mask(layer);
        return mask;
    }

    public static bool Contains(int mask, int layer) => (mask & (1 << (layer & 31))) != 0;
}
