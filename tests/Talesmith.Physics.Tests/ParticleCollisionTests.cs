using System.Numerics;
using Talesmith.VFX;

namespace Talesmith.Physics.Tests;

public sealed class ParticleCollisionTests
{
    private const float GroundTop = 100;

    [Fact]
    public void FallingParticlesLandOnColliders()
    {
        var world = new PhysicsTestWorld();
        world.Ground(GroundTop);
        world.Step();
        using var simulation = new ParticleSimulation();

        Run(simulation, FallingParticles(), world);

        Assert.Equal(20, simulation.AliveCount);
        Assert.All(simulation.Particles.PositionY.ToArray(), y => Assert.InRange(y, GroundTop - 10, GroundTop));
    }

    [Fact]
    public void CollidersOnOtherLayersAreIgnored()
    {
        var world = new PhysicsTestWorld();
        var ground = world.Ground(GroundTop);
        world.World.Get<Collider2D>(ground).Layer = 3;
        world.Step();
        var settings = FallingParticles();
        settings.Collision.LayerMask = 1 << 0;
        using var simulation = new ParticleSimulation();

        Run(simulation, settings, world);

        Assert.All(simulation.Particles.PositionY.ToArray(), y => Assert.True(y > GroundTop + 100));
    }

    [Fact]
    public void ParticlesCanDieOnCollision()
    {
        var world = new PhysicsTestWorld();
        world.Ground(GroundTop);
        world.Step();
        var settings = FallingParticles();
        settings.Collision.KillOnCollision = true;
        using var simulation = new ParticleSimulation();

        Run(simulation, settings, world);

        Assert.Equal(0, simulation.AliveCount);
    }

    private static ParticleSettings FallingParticles()
    {
        var settings = new ParticleSettings { Seed = 1, MaxParticles = 100, Duration = 1, Looping = false };
        settings.Emission.RateOverTime = 0;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(20)));
        settings.Initial.Lifetime = new MinMaxFloat(100);
        settings.Initial.Speed = new MinMaxFloat(0);
        settings.Initial.Size = new MinMaxFloat(8);
        settings.Forces.Enabled = true;
        settings.Collision.Enabled = true;
        settings.Collision.GroundPlane = false;
        settings.Collision.WorldColliders = true;
        settings.Collision.Bounce = 0;
        return settings;
    }

    private static void Run(ParticleSimulation simulation, ParticleSettings settings, PhysicsTestWorld world)
    {
        var context = new ParticleStepContext(Vector2.Zero) { CollisionProviders = [new PhysicsParticleCollisions(world.Physics)] };
        for (var frame = 0; frame < 90; frame++)
            simulation.Update(settings, context, PhysicsTestWorld.Dt);
    }
}
