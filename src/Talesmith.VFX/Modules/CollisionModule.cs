using Talesmith.Authoring;

namespace Talesmith.VFX;

/// <summary>Makes particles bounce off a ground plane and, through <see cref="IParticleCollisionProvider"/>s, off the world.</summary>
public sealed class CollisionModule
{
    public bool Enabled;

    [Label("Ground plane")]
    [Tooltip("Collides with an infinite line below the emitter.")]
    public bool GroundPlane = true;

    [Label("Ground offset")]
    [Tooltip("How far below the emitter the ground is, in world units.")]
    public float GroundOffset = 64;

    [Label("Ground angle")]
    [Tooltip("Tilts the ground, clockwise.")]
    [Angle]
    public float GroundAngle;

    [Label("World colliders")]
    [Tooltip("Also collides with everything the registered collision providers know about, such as physics colliders and tiles.")]
    public bool WorldColliders;

    [Label("Collides with")]
    [Tooltip("The collision layers world colliders must be on, as a bit mask.")]
    [LayerMask]
    public int LayerMask = -1;

    [Tooltip("The fraction of speed kept when bouncing off a surface.")]
    [Range(0, 1)]
    public float Bounce = 0.4f;

    [Tooltip("The fraction of speed along the surface lost on each hit.")]
    [Range(0, 1)]
    public float Friction = 0.2f;

    [Label("Lifetime loss")]
    [Tooltip("The fraction of a particle's lifetime taken away on each hit.")]
    [Range(0, 1)]
    public float LifetimeLoss;

    [Label("Kill on collision")]
    [Tooltip("Removes particles when they hit something, for rain and bullets.")]
    public bool KillOnCollision;

    [Label("Radius scale")]
    [Tooltip("The collision radius as a fraction of the particle's size.")]
    [Range(0, 1)]
    public float RadiusScale = 0.25f;

    [Label("Send events")]
    [Tooltip("Publishes a ParticleCollision event for hits, up to a limit per frame, for sounds and splashes.")]
    public bool SendEvents;
}
