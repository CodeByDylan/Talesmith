using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.VFX.Simulation;

namespace Talesmith.VFX.Tests;

public sealed class SimulationTests
{
    [Fact]
    public void ParticlesDieWhenTheirLifetimeEnds()
    {
        var settings = Particles.Quiet();
        settings.Initial.Lifetime = new MinMaxFloat(1);
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(100)));
        settings.Looping = false;
        using var simulation = new ParticleSimulation();

        Particles.Run(simulation, settings, 0.5f);
        Assert.Equal(100, simulation.AliveCount);

        Particles.Run(simulation, settings, 0.6f);
        Assert.Equal(0, simulation.AliveCount);
        Assert.True(simulation.IsFinished);
    }

    [Fact]
    public void CompactionKeepsOnlyAliveParticlesIntact()
    {
        var settings = Particles.Quiet();
        settings.Initial.Lifetime = new MinMaxFloat(0.2f, 2);
        settings.Initial.Speed = new MinMaxFloat(10, 50);
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1000)));
        settings.Looping = false;
        using var simulation = new ParticleSimulation();
        simulation.Update(settings, Particles.At(), Particles.Frame);
        var lifetimes = simulation.Particles.AgeRate.ToArray().Select(r => 1 / r).ToArray();

        Particles.Run(simulation, settings, 1);

        var expected = lifetimes.Count(l => l > 1 + Particles.Frame + 1e-3f);
        Assert.InRange(simulation.AliveCount, expected, lifetimes.Count(l => l > 1 + Particles.Frame - 1e-3f));
        Assert.All(simulation.Particles.Age.ToArray(), age => Assert.InRange(age, 0, 1));
        foreach (var (rate, age) in simulation.Particles.AgeRate.ToArray().Zip(simulation.Particles.Age.ToArray()))
            Assert.Equal(1 + Particles.Frame, age / rate, 0.01f);
    }

    [Fact]
    public void SameSeedPlaysTheSame()
    {
        var first = Run(seed: 42);
        var second = Run(seed: 42);
        var other = Run(seed: 7);

        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void RestartReplaysTheSameParticles()
    {
        var settings = Lively(seed: 3);
        using var simulation = new ParticleSimulation();
        Particles.Run(simulation, settings, 1);
        var first = Snapshot(simulation);

        simulation.Restart();
        Particles.Run(simulation, settings, 1);

        Assert.Equal(first, Snapshot(simulation));
    }

    [Fact]
    public void LongFramesAreSplitIntoSubSteps()
    {
        var settings = Particles.Quiet();
        settings.Initial.Speed = new MinMaxFloat(0);
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 1;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var smooth = new ParticleSimulation();
        using var choppy = new ParticleSimulation();
        smooth.Update(settings, Particles.At(), 0);
        choppy.Update(settings, Particles.At(), 0);

        Particles.Run(smooth, settings, 0.5f);
        choppy.Update(settings, Particles.At(), 0.5f);

        var exact = 0.5f * 980 * 0.5f * 0.5f;
        Assert.Equal(exact, smooth.Particles.PositionY[0], exact * 0.1f);
        Assert.Equal(exact, choppy.Particles.PositionY[0], exact * 0.1f);
    }

    [Fact]
    public void GravityAndDragShapeTheMotion()
    {
        var settings = Particles.Quiet();
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 1;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var falling = new ParticleSimulation();
        Particles.Run(falling, settings, 1, Particles.At() with { Gravity = new Vector2(0, 100) });

        settings.Drag.Enabled = true;
        settings.Drag.Drag = 2;
        using var dragged = new ParticleSimulation();
        Particles.Run(dragged, settings, 1, Particles.At() with { Gravity = new Vector2(0, 100) });

        Assert.Equal(50, falling.Particles.PositionY[0], 2f);
        Assert.InRange(dragged.Particles.PositionY[0], 10, 45);
    }

    [Fact]
    public void LocalSpaceParticlesFollowTheEmitter()
    {
        var settings = Particles.Quiet();
        settings.SimulationSpace = ParticleSimulationSpace.Local;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var simulation = new ParticleSimulation();
        simulation.Update(settings, Particles.At(Vector2.Zero), Particles.Frame);
        simulation.Update(settings, Particles.At(new Vector2(500, 0)), Particles.Frame);

        var instances = new SpriteInstance[1];
        simulation.WriteInstances(instances, new Rect2(0, 0, 1, 1), Matrix3x2.CreateTranslation(500, 0));

        Assert.Equal(500, instances[0].Bounds.Center.X, 1f);
        Assert.InRange(simulation.Bounds.Center.X, 499, 501);
    }

    [Fact]
    public void SizeAndColorOverLifetimeAreAppliedWhenDrawn()
    {
        var settings = Particles.Quiet();
        settings.Initial.Lifetime = new MinMaxFloat(1);
        settings.Initial.Size = new MinMaxFloat(10);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = Curve.Linear(1, 3);
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = Gradient.Between(Color.White, new Color(255, 0, 0, 255));
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var simulation = new ParticleSimulation();
        simulation.Update(settings, Particles.At(), 0);
        Particles.Run(simulation, settings, 0.5f);

        var instances = new SpriteInstance[1];
        Assert.Equal(1, simulation.WriteInstances(instances, new Rect2(0, 0, 1, 1), Matrix3x2.Identity));

        Assert.Equal(20, instances[0].Bounds.Width, 0.5f);
        Assert.Equal(255, instances[0].Tint.R);
        Assert.InRange(instances[0].Tint.G, 120, 135);
    }

    [Fact]
    public void StretchedParticlesLengthenWithSpeed()
    {
        var settings = Particles.Quiet();
        settings.Initial.Size = new MinMaxFloat(4);
        settings.Initial.Speed = new MinMaxFloat(200);
        settings.Shape.Angle = 0;
        settings.Renderer.Alignment = ParticleAlignment.Stretch;
        settings.Renderer.StretchSpeedScale = 0.05f;
        settings.Renderer.StretchLengthScale = 1;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var simulation = new ParticleSimulation();
        simulation.Update(settings, Particles.At(), Particles.Frame);

        var instances = new SpriteInstance[1];
        simulation.WriteInstances(instances, new Rect2(0, 0, 1, 1), Matrix3x2.Identity);

        Assert.Equal(44, instances[0].Bounds.Width, 0.5f);
        Assert.Equal(4, instances[0].Bounds.Height, 0.5f);
    }

    [Fact]
    public void TextureSheetPicksFramesOverLifetime()
    {
        var settings = Particles.Quiet();
        settings.Initial.Lifetime = new MinMaxFloat(1);
        settings.TextureSheet.Enabled = true;
        settings.TextureSheet.Columns = 2;
        settings.TextureSheet.Rows = 2;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var simulation = new ParticleSimulation();
        simulation.Update(settings, Particles.At(), 0);
        Particles.Run(simulation, settings, 0.6f);

        var instances = new SpriteInstance[1];
        simulation.WriteInstances(instances, new Rect2(0, 0, 64, 64), Matrix3x2.Identity);

        Assert.Equal(new Rect2(0, 32, 32, 32), instances[0].Source);
    }

    [Fact]
    public void CurveTableMatchesTheCurve()
    {
        var curve = new Curve([new CurveKey(0, 0), new CurveKey(0.5f, 2), new CurveKey(1, 1)]);
        var table = new CurveTable();
        table.Update(curve);

        for (var t = 0f; t <= 1; t += 0.05f)
            Assert.Equal(curve.Evaluate(t), table.Values[CurveTable.Index(t)], 0.05f);
        Assert.Equal(0, table.Min, 0.001f);
        Assert.Equal(2, table.Max, 0.02f);
        Assert.False(table.IsOne);
    }

    [Fact]
    public void CurveTableRebakesOnlyForANewCurve()
    {
        var table = new CurveTable();
        var curve = Curve.Constant(2);
        table.Update(curve);
        var values = table.Values;

        table.Update(curve);
        Assert.Same(values, table.Values);
        table.Update(Curve.One);
        Assert.True(table.IsOne);
    }

    [Fact]
    public void GradientTableMatchesTheGradient()
    {
        var gradient = new Gradient([new GradientStop(0, Color.Black), new GradientStop(1, new Color(255, 128, 0, 0))]);
        var table = new GradientTable();
        table.Update(gradient);

        for (var t = 0f; t <= 1; t += 0.1f)
        {
            var expected = gradient.Evaluate(t).ToVector4() * 255;
            var actual = table.Values[CurveTable.Index(t)];
            Assert.Equal(expected.X, actual.X, 2f);
            Assert.Equal(expected.Y, actual.Y, 2f);
            Assert.Equal(expected.W, actual.W, 2f);
        }

        Assert.False(table.IsWhite);
    }

    [Fact]
    public void GroundCollisionBouncesParticles()
    {
        var settings = Particles.Quiet();
        settings.Initial.Speed = new MinMaxFloat(0);
        settings.Initial.Size = new MinMaxFloat(4);
        settings.Forces.Enabled = true;
        settings.Collision.Enabled = true;
        settings.Collision.GroundOffset = 50;
        settings.Collision.Bounce = 0.5f;
        settings.Collision.SendEvents = true;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1)));
        using var simulation = new ParticleSimulation();
        var hits = 0;

        for (var i = 0; i < 120; i++)
        {
            simulation.Update(settings, Particles.At() with { Gravity = new Vector2(0, 980) }, Particles.Frame);
            hits += simulation.CollisionEvents.Length;
            Assert.True(simulation.Particles.PositionY[0] <= 50, "The particle fell through the ground.");
        }

        Assert.True(hits > 0);
    }

    [Fact]
    public void KillOnCollisionRemovesParticles()
    {
        var settings = Particles.Quiet();
        settings.Shape.Angle = MathF.PI / 2;
        settings.Initial.Speed = new MinMaxFloat(600);
        settings.Collision.Enabled = true;
        settings.Collision.GroundOffset = 30;
        settings.Collision.KillOnCollision = true;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(20)));

        Assert.Equal(0, Particles.Emitted(settings, 0.5f));
    }

    [Fact]
    public void CollisionProvidersResolveHits()
    {
        var settings = Particles.Quiet();
        settings.Shape.Angle = 0;
        settings.Initial.Speed = new MinMaxFloat(300);
        settings.Collision.Enabled = true;
        settings.Collision.GroundPlane = false;
        settings.Collision.WorldColliders = true;
        settings.Collision.Bounce = 1;
        settings.Collision.Friction = 0;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(5)));
        using var simulation = new ParticleSimulation();

        Particles.Run(simulation, settings, 0.5f, Particles.At() with { CollisionProviders = [new Wall(40)] });

        Assert.All(simulation.Particles.PositionX.ToArray(), x => Assert.True(x <= 40));
        Assert.All(simulation.Particles.VelocityX.ToArray(), vx => Assert.True(vx < 0));
    }

    [Fact]
    public void PluginModulesUpdateParticles()
    {
        var settings = Particles.Quiet();
        settings.Initial.Size = new MinMaxFloat(8);
        settings.CustomModules.Add(new Shrink { Amount = 0.5f });
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(3)));
        using var simulation = new ParticleSimulation();

        Particles.Run(simulation, settings, 0.1f);

        Assert.All(simulation.Particles.Size.ToArray(), size => Assert.True(size < 8));
        Assert.All(simulation.Particles.Seed.ToArray(), seed => Assert.Equal(7, seed));
    }

    private static float[] Run(int seed)
    {
        using var simulation = new ParticleSimulation();
        Particles.Run(simulation, Lively(seed), 1);
        return Snapshot(simulation);
    }

    private static ParticleSettings Lively(int seed)
    {
        var settings = BuiltInSettings(seed);
        settings.Noise.Enabled = true;
        settings.VelocityOverLifetime.Enabled = true;
        settings.VelocityOverLifetime.Orbital = 1;
        return settings;
    }

    private static ParticleSettings BuiltInSettings(int seed)
    {
        var settings = Presets.BuiltInParticlePresets.MagicSparkle();
        settings.Seed = seed;
        settings.Prewarm = false;
        return settings;
    }

    private static float[] Snapshot(ParticleSimulation simulation) =>
        [.. simulation.Particles.PositionX.ToArray(), .. simulation.Particles.PositionY.ToArray(), .. simulation.Particles.Size.ToArray()];

    private sealed class Wall(float x) : IParticleCollisionProvider
    {
        public void Collide(ParticleCollisionContext context)
        {
            for (var i = 0; i < context.Count; i++)
            {
                var position = context.Position(i);
                if (position.X > x)
                    context.Hit(i, new Vector2(x, position.Y), -Vector2.UnitX);
            }
        }
    }

    private sealed class Shrink : IParticleModule
    {
        public bool Enabled { get; set; } = true;

        public float Amount;

        public void OnEmit(ParticleModuleContext context, int first, int count)
        {
            foreach (ref var seed in context.Particles.Seed.Slice(first, count))
                seed = 7;
        }

        public void Update(ParticleModuleContext context)
        {
            foreach (ref var size in context.Particles.Size)
                size *= 1 - Amount * context.DeltaTime;
        }
    }
}
