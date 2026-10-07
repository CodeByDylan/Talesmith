using System.Numerics;
using BenchmarkDotNet.Attributes;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.VFX;

namespace Talesmith.Benchmarks;

/// <summary>One 60 Hz frame of a single emitter holding a steady population of particles: simulation, then building its sprites.</summary>
[MemoryDiagnoser]
public class ParticleBenchmarks : IDisposable
{
    private const float Frame = 1f / 60;
    private const float Lifetime = 2;

    private readonly ParticleSimulation _simulation = new();
    private ParticleSettings _settings = null!;
    private SpriteInstance[] _instances = [];

    [Params(10_000, 100_000)]
    public int Particles { get; set; }

    /// <summary>Simple: gravity and drag. Full: adds turbulence, velocity and rotation over lifetime, color and size curves.</summary>
    [Params("Simple", "Full")]
    public string Emitter { get; set; } = "Simple";

    [GlobalSetup]
    public void Setup()
    {
        _settings = new ParticleSettings { Seed = 7, MaxParticles = Particles, Duration = Lifetime };
        _settings.Emission.RateOverTime = Particles / Lifetime;
        _settings.Shape.Kind = ParticleShapeKind.Circle;
        _settings.Shape.Radius = 200;
        _settings.Shape.Direction = ParticleDirectionMode.Random;
        _settings.Initial.Lifetime = new MinMaxFloat(Lifetime * 0.9f, Lifetime);
        _settings.Forces.Enabled = true;
        _settings.Forces.GravityScale = 0.2f;
        _settings.Drag.Enabled = true;
        if (Emitter == "Full")
        {
            _settings.Noise.Enabled = true;
            _settings.VelocityOverLifetime.Enabled = true;
            _settings.VelocityOverLifetime.Orbital = 0.5f;
            _settings.RotationOverLifetime.Enabled = true;
            _settings.ColorOverLifetime.Enabled = true;
            _settings.ColorOverLifetime.Color = Gradient.Between(Color.White, new Color(255, 80, 0, 0));
            _settings.SizeOverLifetime.Enabled = true;
            _settings.SizeOverLifetime.Size = Curve.Linear(1, 0);
        }

        _instances = new SpriteInstance[Particles];
        for (var i = 0; i < (int)(Lifetime / Frame) + 30; i++)
            _simulation.Update(_settings, new ParticleStepContext(Vector2.Zero), Frame);
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _simulation.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public int Simulate()
    {
        _simulation.Update(_settings, new ParticleStepContext(Vector2.Zero), Frame);
        return _simulation.AliveCount;
    }

    [Benchmark]
    public int BuildSprites() => _simulation.WriteInstances(_instances, new Rect2(0, 0, 64, 64), Matrix3x2.Identity);
}
