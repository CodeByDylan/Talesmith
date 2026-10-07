using System.Numerics;

namespace Talesmith.VFX;

/// <summary>Game-wide particle quality and limits; change them at any time, for example from a graphics settings menu.</summary>
public sealed class ParticleOptions
{
    /// <summary>The most particles alive in one scene across all emitters.</summary>
    public int MaxParticlesPerScene { get; set; } = 200_000;

    /// <summary>Multiplies every emitter's emission, for quality settings; 1 is full quality.</summary>
    public float EmissionScale { get; set; } = 1;

    /// <summary>Emitters whose particles appear smaller than this many pixels emit proportionally fewer.</summary>
    public float LodMinimumPixelSize { get; set; } = 2;

    /// <summary>The fraction of the scene budget above which every emitter emits progressively less, reaching zero at the cap.</summary>
    public float BudgetPressureThreshold { get; set; } = 0.8f;

    /// <summary>World units around the camera's view in which emitters still count as visible.</summary>
    public float CullingMargin { get; set; } = 64;

    /// <summary>The most collision events one emitter publishes per frame.</summary>
    public int MaxCollisionEventsPerEmitter { get; set; } = 16;
}

/// <summary>World-wide forces that particles respond to.</summary>
/// <remarks>The scene sets <see cref="Gravity"/> from its environment when it loads.</remarks>
public sealed class ParticleEnvironment
{
    /// <summary>Gravity in world units per second squared; Y points down.</summary>
    public Vector2 Gravity { get; set; } = new(0, 980);
}

/// <summary>The particles one scene may keep alive at once, shared by all of its emitters.</summary>
/// <remarks>
/// Emitters reserve room before emitting and release it as particles die, so the total never exceeds <see cref="Capacity"/>. Belongs to the
/// game thread.
/// </remarks>
public sealed class ParticleBudget
{
    public ParticleBudget(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        Capacity = capacity;
    }

    public ParticleBudget(ParticleOptions options) : this(options.MaxParticlesPerScene)
    {
    }

    public int Capacity { get; set; }

    /// <summary>Particles alive or reserved in the scene.</summary>
    public int Used { get; private set; }

    public int Remaining => Math.Max(0, Capacity - Used);

    /// <summary>How full the budget is, from 0 to 1.</summary>
    public float Pressure => Capacity == 0 ? 1 : Math.Min(1, Used / (float)Capacity);

    /// <summary>Reserves room for up to <paramref name="count"/> particles and returns how many may be emitted.</summary>
    public int Reserve(int count)
    {
        var granted = Math.Clamp(count, 0, Remaining);
        Used += granted;
        return granted;
    }

    public void Release(int count) => Used = Math.Max(0, Used - count);

    /// <summary>Starts a frame's accounting from the particles already alive.</summary>
    internal void Reset(int alive) => Used = alive;
}
