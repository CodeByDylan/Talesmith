using System.Numerics;

namespace Talesmith.Samples.IsleHopper;

/// <summary>The hero: its velocity, whether it stands on something, and the timers that make jumping forgiving.</summary>
/// <remarks>The entity's <c>Transform</c> position is the middle of the hero's feet.</remarks>
public struct Hero
{
    public Vector2 Velocity;

    public bool OnGround;

    /// <summary>Seconds left in which a jump still works after running off a ledge.</summary>
    public float CoyoteTime;

    /// <summary>Seconds left in which a jump pressed just before landing still happens.</summary>
    public float JumpBuffer;

    /// <summary>Seconds left during which the hero cannot be hurt again, shown by blinking.</summary>
    public float Invulnerable;

    /// <summary>Seconds since the hero last stood on something.</summary>
    public float AirTime;

    public float StepTimer;

    public bool FacingLeft;

    /// <summary>Where the hero returns after getting hurt: the spawn point or the last checkpoint.</summary>
    public Vector2 Respawn;
}

/// <summary>A crab walking back and forth on the ground until the hero stomps it.</summary>
/// <remarks>The entity's <c>Transform</c> position is the center of its 64 × 48 image, whose bottom edge is the ground.</remarks>
public struct Crab
{
    public float Speed;

    /// <summary>-1 walking left, 1 walking right.</summary>
    public float Direction;

    public float AnimationTime;

    /// <summary>Seconds since it was stomped, or a negative value while it is alive.</summary>
    public float Stomped;
}

/// <summary>Makes a pickup float up and down around where it was placed.</summary>
public struct Bob
{
    public float BaseY;

    public float Phase;
}

/// <summary>A flag that becomes the respawn point once the hero touches it.</summary>
public struct Checkpoint
{
    public bool Reached;
}

/// <summary>What the player asks the hero to do, read once per frame and used by the fixed-step movement.</summary>
/// <remarks>
/// A frame can run no fixed steps at high frame rates, or several at low ones, so a jump press is kept until a step uses it instead
/// of only lasting for the frame it happened in.
/// </remarks>
public struct HeroControls
{
    /// <summary>-1 runs left, 1 runs right.</summary>
    public float Run;

    public bool JumpHeld;

    /// <summary>Set when the jump button goes down, cleared by the movement step that sees it.</summary>
    public bool JumpPressed;
}

/// <summary>Draws an entity moved by fixed steps between its last two simulated positions, so it moves smoothly at any frame rate.</summary>
/// <remarks>
/// Fixed-step systems move the entity's <c>Transform</c> as usual; <see cref="SmoothMotionSystem"/> restores the simulated position before
/// each step and shows a blend of the last two afterwards. A move longer than <see cref="SmoothMotionSystem.TeleportDistance"/> in one
/// step, such as a respawn, is shown at once instead of blended. Code that moves such an entity outside a fixed step must set
/// <see cref="Current"/> and <see cref="Previous"/> too, or the next step puts it back.
/// </remarks>
public struct SmoothMotion(Vector2 position)
{
    /// <summary>The simulated position before the last fixed step.</summary>
    public Vector2 Previous = position;

    /// <summary>The simulated position after the last fixed step.</summary>
    public Vector2 Current = position;
}
