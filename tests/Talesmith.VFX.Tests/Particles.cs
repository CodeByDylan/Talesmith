using System.Numerics;

namespace Talesmith.VFX.Tests;

/// <summary>Builds simple settings and runs simulations at a fixed frame rate.</summary>
internal static class Particles
{
    public const float Frame = 1f / 60;

    /// <summary>Settings that emit nothing on their own, with long-lived, motionless particles, so counts depend only on what a test adds.</summary>
    public static ParticleSettings Quiet(int seed = 1)
    {
        var settings = new ParticleSettings { Seed = seed, MaxParticles = 100_000, Duration = 1 };
        settings.Emission.RateOverTime = 0;
        settings.Initial.Lifetime = new MinMaxFloat(100);
        settings.Initial.Speed = new MinMaxFloat(0);
        return settings;
    }

    public static ParticleStepContext At(Vector2 position = default, ParticleBudget? budget = null) => new(position) { Budget = budget };

    /// <summary>Updates the simulation for <paramref name="seconds"/> in 60 Hz frames.</summary>
    public static void Run(ParticleSimulation simulation, ParticleSettings settings, float seconds, ParticleStepContext? context = null)
    {
        var frames = (int)MathF.Round(seconds / Frame);
        var step = context ?? At();
        for (var i = 0; i < frames; i++)
            simulation.Update(settings, step, Frame);
    }

    /// <summary>Counts particles emitted over <paramref name="seconds"/> with nothing dying.</summary>
    public static int Emitted(ParticleSettings settings, float seconds)
    {
        using var simulation = new ParticleSimulation();
        Run(simulation, settings, seconds);
        return simulation.AliveCount;
    }
}
