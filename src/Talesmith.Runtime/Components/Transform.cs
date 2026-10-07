using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Ecs;

namespace Talesmith.Runtime.Components;

/// <summary>Where an entity is in the world.</summary>
/// <remarks>For entities with a <see cref="Parent"/>, <c>TransformHierarchySystem</c> computes it from their <see cref="LocalTransform"/>; scenes save the local values.</remarks>
[Component(Category = "Core", Icon = "move", Description = "Position, rotation and scale; relative to the parent for child entities.")]
public struct Transform
{
    public Vector2 Position;

    /// <summary>Clockwise rotation in radians.</summary>
    [Angle]
    public float Rotation;

    public Vector2 Scale;

    public Transform()
    {
        Scale = Vector2.One;
    }

    public Transform(Vector2 position, float rotation = 0)
    {
        Position = position;
        Rotation = rotation;
        Scale = Vector2.One;
    }
}

/// <summary>The transform of a child entity relative to its <see cref="Parent"/>.</summary>
[Component(Category = "Core", Hidden = true)]
public struct LocalTransform
{
    public Vector2 Position;

    /// <summary>Clockwise rotation in radians, added to the parent's.</summary>
    [Angle]
    public float Rotation;

    public Vector2 Scale;

    public LocalTransform()
    {
        Scale = Vector2.One;
    }

    public LocalTransform(Vector2 position, float rotation = 0)
    {
        Position = position;
        Rotation = rotation;
        Scale = Vector2.One;
    }

    public LocalTransform(in Transform transform)
    {
        Position = transform.Position;
        Rotation = transform.Rotation;
        Scale = transform.Scale;
    }

    public readonly Transform ToTransform() => new(Position, Rotation) { Scale = Scale };
}

/// <summary>Makes an entity a child of another; its <see cref="Transform"/> follows the parent's through its <see cref="LocalTransform"/>.</summary>
[Component(Category = "Core", Hidden = true)]
public record struct Parent(Entity Value);

/// <summary>A readable name, used by tools, logs and lookups.</summary>
public readonly record struct Name(string Value)
{
    public override string ToString() => Value;
}
