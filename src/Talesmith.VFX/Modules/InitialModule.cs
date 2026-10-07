using Talesmith.Authoring;

namespace Talesmith.VFX;

/// <summary>The values each particle starts with; a range picks a random value per particle.</summary>
public sealed class InitialModule
{
    [Tooltip("Seconds each particle lives.")]
    public MinMaxFloat Lifetime = new(1, 1.5f);

    [Tooltip("Starting speed in world units per second, along the shape's direction.")]
    public MinMaxFloat Speed = new(50, 100);

    [Tooltip("Starting width and height in world units.")]
    public MinMaxFloat Size = new(8, 16);

    [Tooltip("Starting rotation, clockwise.")]
    [Angle]
    public MinMaxFloat Rotation;

    [Label("Angular velocity")]
    [Tooltip("Rotation per second, clockwise.")]
    [Angle]
    public MinMaxFloat AngularVelocity;

    [Tooltip("Starting tint, multiplied by color over lifetime.")]
    public MinMaxColor Color = new(Mathematics.Color.White);

    [Label("Align to direction")]
    [Tooltip("Adds the direction a particle starts moving in to its rotation, for sprites that point forward.")]
    public bool AlignToDirection;

    [Label("Inherit velocity")]
    [Tooltip("How much of the emitter's own movement particles carry along, from 0 to 1.")]
    [Range(0, 1)]
    public float InheritVelocity;
}
