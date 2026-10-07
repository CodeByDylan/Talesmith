using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.VFX;

/// <summary>What a <see cref="ParticleSimulation"/> needs from the world for one update.</summary>
/// <param name="Position">The emitter's world position.</param>
/// <param name="Rotation">The emitter's clockwise rotation in radians.</param>
public readonly record struct ParticleStepContext(Vector2 Position, float Rotation = 0)
{
    /// <summary>The emitter's scale, applied to the shape and particle sizes.</summary>
    public Vector2 Scale { get; init; } = Vector2.One;

    /// <summary>The scene's gravity, scaled by <see cref="ForceModule.GravityScale"/>.</summary>
    public Vector2 Gravity { get; init; } = new(0, 980);

    /// <summary>Multiplies emission for level of detail and budget pressure, from 0 to 1.</summary>
    public float EmissionScale { get; init; } = 1;

    /// <summary>The scene's shared particle cap, or null for no cap beyond the emitter's own.</summary>
    public ParticleBudget? Budget { get; init; }

    public IReadOnlyList<IParticleCollisionProvider> CollisionProviders { get; init; } = [];

    /// <summary>The most collision events kept per update.</summary>
    public int MaxCollisionEvents { get; init; } = 16;

    public Entity Entity { get; init; }

    public World? World { get; init; }

    /// <summary>The transform from the emitter's local space to world space.</summary>
    public Matrix3x2 LocalToWorld => Matrix3x2.CreateScale(Scale) * Matrix3x2.CreateRotation(Rotation) * Matrix3x2.CreateTranslation(Position);
}
