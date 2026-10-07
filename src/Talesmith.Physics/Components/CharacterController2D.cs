using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Ecs;

namespace Talesmith.Physics;

/// <summary>The sides on which a character touched something during its last move.</summary>
[Flags]
public enum CharacterCollisions
{
    None = 0,

    /// <summary>Standing on ground no steeper than the slope limit.</summary>
    Below = 1,

    /// <summary>Touching a wall or a slope too steep to stand on.</summary>
    Sides = 2,

    /// <summary>Touching a ceiling.</summary>
    Above = 4
}

/// <summary>Moves an entity with its <see cref="Collider2D"/> by sliding along whatever it touches, for platformer and top-down characters.</summary>
/// <remarks>
/// Call <see cref="IPhysicsWorld.MoveCharacter"/> from a fixed-step system with the distance to move; the character slides along walls,
/// walks up slopes up to <see cref="SlopeLimit"/>, climbs steps up to <see cref="StepOffset"/> and stays on the ground when walking down
/// slopes. The character is kinematic: it pushes dynamic bodies, is never pushed by them, and enters triggers.
/// </remarks>
[Component("Character Controller 2D", Category = "Physics", Icon = "person-standing", Description = "Moves the entity by sliding along walls and floors.")]
public struct CharacterController2D
{
    [Tooltip("The steepest slope the character can stand on and walk up.")]
    [Angle]
    public float SlopeLimit = 50 * MathF.PI / 180;

    [Tooltip("The highest step the character climbs without jumping, in world units.")]
    [Range(0)]
    public float StepOffset;

    [Tooltip("The gap kept between the character and what it touches, in world units; avoids getting stuck in surfaces.")]
    [Range(0.01)]
    public float SkinWidth = 0.5f;

    [Tooltip("How far the character is pulled down to stay on the ground when walking down slopes or over small drops.")]
    [Range(0)]
    public float GroundSnapDistance = 4;

    [Tooltip("The direction of up, which decides what is ground, wall and ceiling.")]
    public Vector2 Up = new(0, -1);

    [Tooltip("Draws the character between its last two fixed-step positions, for smooth motion on fast displays.")]
    public bool Interpolate;

    [Transient]
    public CharacterCollisions Collisions;

    /// <summary>The normal of the ground stood on, pointing away from it.</summary>
    [Transient]
    public Vector2 GroundNormal;

    /// <summary>The entity stood on, such as a platform or a tile map.</summary>
    [Transient]
    public Entity Ground;

    /// <summary>The distance moved by the last <see cref="IPhysicsWorld.MoveCharacter"/>.</summary>
    [Transient]
    public Vector2 LastMotion;

    public CharacterController2D()
    {
    }

    [Transient]
    public readonly bool IsGrounded => (Collisions & CharacterCollisions.Below) != 0;
}
