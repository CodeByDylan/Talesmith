using Talesmith.Authoring;

namespace Talesmith.VFX;

/// <summary>How many particles are emitted: continuously over time, over distance moved, and in bursts.</summary>
public sealed class EmissionModule
{
    [Tooltip("Emits particles; when off, only Emit calls from code add particles.")]
    public bool Enabled = true;

    [Label("Rate over time")]
    [Tooltip("Particles emitted per second.")]
    [Range(0, 10000, Step = 1)]
    public float RateOverTime = 10;

    [Label("Rate over distance")]
    [Tooltip("Particles emitted per world unit the emitter moves, for trails.")]
    [Range(0, 100, Step = 0.1)]
    public float RateOverDistance;

    [Tooltip("Groups of particles emitted at once at set times in each cycle.")]
    public List<ParticleBurst> Bursts = [];
}

/// <summary>Particles emitted at once at a time in each cycle, optionally repeating.</summary>
public sealed class ParticleBurst
{
    [Tooltip("Seconds from the start of the cycle to the first burst.")]
    [Range(0)]
    public float Time;

    [Tooltip("Particles in each burst; a range picks a random count each time.")]
    public MinMaxFloat Count = new(10);

    [Tooltip("How many times the burst fires; 0 repeats until the cycle ends.")]
    [Range(0, 1000, Step = 1)]
    public int Cycles = 1;

    [Tooltip("Seconds between repeated bursts.")]
    [Range(0.01)]
    public float Interval = 0.1f;

    [Tooltip("The chance that each burst fires, from 0 to 1.")]
    [Range(0, 1)]
    public float Probability = 1;

    public ParticleBurst()
    {
    }

    public ParticleBurst(float time, MinMaxFloat count, int cycles = 1, float interval = 0.1f, float probability = 1)
    {
        Time = time;
        Count = count;
        Cycles = cycles;
        Interval = interval;
        Probability = probability;
    }
}
