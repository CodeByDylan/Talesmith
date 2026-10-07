using System.Numerics;
using Talesmith.Authoring;

namespace Talesmith.Physics;

/// <summary>How a <see cref="Rigidbody2D"/> moves.</summary>
public enum BodyType
{
    /// <summary>Moved by gravity, forces and collisions.</summary>
    Dynamic,

    /// <summary>Moved only by its velocity; pushes dynamic bodies but is never pushed. Use it for moving platforms.</summary>
    Kinematic,

    /// <summary>Never moves on its own; the same as a collider without a rigid body.</summary>
    Static
}

/// <summary>How a body's drawn position relates to its simulated one between fixed steps.</summary>
public enum RigidbodyInterpolation
{
    /// <summary>Shows the position of the last fixed step.</summary>
    None,

    /// <summary>Blends between the last two fixed steps, which looks smooth on displays faster than the fixed rate, one step behind.</summary>
    Interpolate
}

/// <summary>How hard the simulation tries to keep fast bodies from passing through others.</summary>
public enum CollisionDetection
{
    /// <summary>Sweeps only against static geometry and only when the body moves more than half its size in one step.</summary>
    Discrete,

    /// <summary>Sweeps every step against static, kinematic and dynamic bodies, for bullets and other very fast bodies.</summary>
    Continuous
}

/// <summary>Makes an entity move under gravity, forces and collisions.</summary>
/// <remarks>
/// The simulation reads <see cref="Velocity"/> and <see cref="AngularVelocity"/> before each fixed step and writes them back after it, so
/// game code can set them directly. The entity's <see cref="Collider2D"/> gives the body its shape; without one the body still moves but
/// touches nothing.
/// </remarks>
[Component("Rigidbody 2D", Category = "Physics", Icon = "weight", Description = "Moves the entity with gravity, forces and collisions.")]
public struct Rigidbody2D
{
    public BodyType Type = BodyType.Dynamic;

    [Header("Mass")]
    [Tooltip("Computes the mass from the collider's area and density instead of using Mass.")]
    public bool AutoMass;

    [Range(0.0001)]
    public float Mass = 1;

    [Tooltip("Multiplies the world's gravity for this body; 0 makes it float.")]
    public float GravityScale = 1;

    [Tooltip("Slows movement down over time, like air resistance.")]
    [Range(0)]
    public float LinearDrag;

    [Tooltip("Slows rotation down over time.")]
    [Range(0)]
    public float AngularDrag = 0.05f;

    [Tooltip("Keeps the body upright, as characters usually are.")]
    public bool FixedRotation;

    [Header("Motion")]
    [Tooltip("World units per second.")]
    public Vector2 Velocity;

    [Tooltip("Radians per second, clockwise.")]
    [Angle]
    public float AngularVelocity;

    public RigidbodyInterpolation Interpolation = RigidbodyInterpolation.None;

    public CollisionDetection CollisionDetection = CollisionDetection.Discrete;

    [Tooltip("Lets the body stop simulating while it rests, which saves time; it wakes when touched or pushed.")]
    public bool CanSleep = true;

    public Rigidbody2D()
    {
    }

    public Rigidbody2D(BodyType type) => Type = type;
}
