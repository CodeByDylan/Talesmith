using Talesmith.Diagnostics;

namespace Talesmith.VFX;

/// <summary>Particle numbers of the active scene for the last frame, for performance readouts.</summary>
/// <remarks>The same numbers are recorded as <see cref="ParticleCounters"/> in the game profiler.</remarks>
public sealed class ParticleStats
{
    public int AliveParticles { get; private set; }

    /// <summary>Particles emitted per second across all emitters, smoothed over recent frames.</summary>
    public float EmittedPerSecond { get; private set; }

    public double SimulationMilliseconds { get; private set; }

    public int Emitters => VisibleEmitters + CulledEmitters;

    public int VisibleEmitters { get; private set; }

    /// <summary>Emitters outside the camera's view, which may be paused.</summary>
    public int CulledEmitters { get; private set; }

    internal void Begin()
    {
        AliveParticles = 0;
        EmittedPerSecond = 0;
        SimulationMilliseconds = 0;
        VisibleEmitters = 0;
        CulledEmitters = 0;
    }

    internal void Add(int alive, float emittedPerSecond, double simulationMilliseconds, bool visible)
    {
        AliveParticles += alive;
        EmittedPerSecond += emittedPerSecond;
        SimulationMilliseconds += simulationMilliseconds;
        if (visible)
            VisibleEmitters++;
        else
            CulledEmitters++;
    }
}

/// <summary>Markers and counters recorded by the particle systems.</summary>
public static class ParticleCounters
{
    public static readonly ProfilerMarker Simulate = ProfilerMarker.Get("Particles/Simulate", "Particles");
    public static readonly ProfilerMarker Draw = ProfilerMarker.Get("Particles/Draw", "Particles");

    public static readonly ProfilerCounter Alive = ProfilerCounter.Get("Particles alive", CounterKind.Gauge, "particles");
    public static readonly ProfilerCounter Emitted = ProfilerCounter.Get("Particles emitted", CounterKind.PerFrame, "particles");
    public static readonly ProfilerCounter Drawn = ProfilerCounter.Get("Particles drawn", CounterKind.PerFrame, "particles");
    public static readonly ProfilerCounter VisibleEmitters = ProfilerCounter.Get("Particle emitters visible", CounterKind.Gauge, "emitters");
    public static readonly ProfilerCounter CulledEmitters = ProfilerCounter.Get("Particle emitters culled", CounterKind.Gauge, "emitters");
}
