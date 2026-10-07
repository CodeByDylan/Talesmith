using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.Physics;

/// <summary>Chooses which colliders a query sees.</summary>
/// <param name="LayerMask">The layers to include; <see cref="PhysicsLayers.All"/> includes every layer.</param>
/// <param name="IncludeTriggers">Whether trigger colliders are reported.</param>
/// <param name="Ignore">An entity to skip, such as the one asking.</param>
public readonly record struct QueryFilter(int LayerMask, bool IncludeTriggers = false, Entity Ignore = default)
{
    /// <summary>Every solid collider on every layer.</summary>
    public static QueryFilter Default => new(PhysicsLayers.All);
}

/// <summary>Where a ray or a swept shape hit a collider.</summary>
/// <param name="Entity">The entity hit; for tile maps, the map's entity.</param>
/// <param name="Point">The world point of the hit on the collider's surface.</param>
/// <param name="Normal">The collider's surface normal at the hit, pointing back toward the ray.</param>
/// <param name="Distance">How far along the ray or sweep the hit is, in world units.</param>
/// <param name="Fraction">How far along the ray or sweep the hit is, from 0 to 1.</param>
public readonly record struct RaycastHit(Entity Entity, Vector2 Point, Vector2 Normal, float Distance, float Fraction, bool IsTrigger);

/// <summary>A shape for overlap queries and shape casts, centered on the position it is queried at.</summary>
public readonly record struct QueryShape
{
    private QueryShape(ColliderShape kind, Vector2 size, float radius, Vector2[]? points)
    {
        Kind = kind;
        Size = size;
        Radius = radius;
        Points = points;
    }

    public ColliderShape Kind { get; }

    public Vector2 Size { get; }

    public float Radius { get; }

    public Vector2[]? Points { get; }

    public static QueryShape Box(Vector2 size) => new(ColliderShape.Box, size, 0, null);

    public static QueryShape Circle(float radius) => new(ColliderShape.Circle, Vector2.Zero, radius, null);

    public static QueryShape Capsule(Vector2 size) => new(ColliderShape.Capsule, size, 0, null);

    /// <summary>A convex polygon of at most eight points.</summary>
    public static QueryShape Polygon(params Vector2[] points) => new(ColliderShape.Polygon, Vector2.Zero, 0, points);
}
