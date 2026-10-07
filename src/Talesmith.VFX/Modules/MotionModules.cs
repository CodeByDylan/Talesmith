using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Mathematics;

namespace Talesmith.VFX;

/// <summary>Movement added over each particle's lifetime: a constant drift, swirling around the emitter and pushing away from it.</summary>
public sealed class VelocityOverLifetimeModule
{
    public bool Enabled;

    [Tooltip("A velocity added to every particle, in world units per second.")]
    public Vector2 Linear;

    [Label("Linear over lifetime")]
    [Tooltip("Scales the linear velocity over each particle's lifetime.")]
    public Curve LinearOverLifetime = Curve.One;

    [Tooltip("Circles particles around the emitter, in radians per second; positive is clockwise.")]
    [Angle]
    public float Orbital;

    [Tooltip("Moves particles away from the emitter in world units per second; negative pulls them in.")]
    public float Radial;

    [Label("Speed multiplier")]
    [Tooltip("Scales each particle's own velocity over its lifetime.")]
    public Curve SpeedMultiplier = Curve.One;
}

/// <summary>Constant acceleration and the scene's gravity.</summary>
public sealed class ForceModule
{
    public bool Enabled;

    [Tooltip("Acceleration in world units per second squared.")]
    public Vector2 Acceleration;

    [Label("Gravity scale")]
    [Tooltip("How strongly the scene's gravity pulls particles; 1 is full gravity, negative makes them rise.")]
    [Range(-10, 10, Step = 0.05)]
    public float GravityScale = 1;
}

/// <summary>Slows particles down, like air resistance.</summary>
public sealed class DragModule
{
    public bool Enabled;

    [Tooltip("The fraction of speed lost per second; 1 roughly halves the speed in 0.7 seconds.")]
    [Range(0, 50, Step = 0.05)]
    public float Drag = 1;
}

/// <summary>Tints particles over their lifetime, multiplied by their starting color.</summary>
public sealed class ColorOverLifetimeModule
{
    public bool Enabled;

    [Tooltip("The tint from birth (left) to death (right), including transparency.")]
    public Gradient Color = Gradient.White;
}

/// <summary>Scales particles over their lifetime, multiplied by their starting size.</summary>
public sealed class SizeOverLifetimeModule
{
    public bool Enabled;

    [Tooltip("The size multiplier from birth (0) to death (1).")]
    public Curve Size = Curve.One;
}

/// <summary>Spins particles over their lifetime, in addition to their starting angular velocity.</summary>
public sealed class RotationOverLifetimeModule
{
    public bool Enabled;

    [Label("Angular velocity")]
    [Tooltip("Rotation per second, clockwise.")]
    [Angle]
    public float AngularVelocity = MathF.PI;

    [Label("Over lifetime")]
    [Tooltip("Scales the angular velocity over each particle's lifetime.")]
    public Curve OverLifetime = Curve.One;
}

/// <summary>Pushes particles through a smooth swirling flow field, for smoke, magic and embers.</summary>
public sealed class NoiseModule
{
    public bool Enabled;

    [Tooltip("The push in world units per second squared.")]
    [Range(0, 10000)]
    public float Strength = 60;

    [Tooltip("The size of the swirls in world units; larger values make wider, calmer swirls.")]
    [Range(1, 10000)]
    public float Scale = 96;

    [Label("Scroll speed")]
    [Tooltip("How fast the flow changes over time.")]
    [Range(0, 10)]
    public float ScrollSpeed = 0.3f;

    [Label("Strength over lifetime")]
    [Tooltip("Scales the push over each particle's lifetime.")]
    public Curve StrengthOverLifetime = Curve.One;
}
