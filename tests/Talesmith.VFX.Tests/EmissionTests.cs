using System.Numerics;

namespace Talesmith.VFX.Tests;

public sealed class EmissionTests
{
    [Fact]
    public void RateOverTimeEmitsThatManyParticlesPerSecond()
    {
        var settings = Particles.Quiet();
        settings.Emission.RateOverTime = 30;

        Assert.InRange(Particles.Emitted(settings, 2), 59, 60);
    }

    [Fact]
    public void RateIsSpreadEvenlyAcrossFrames()
    {
        var settings = Particles.Quiet();
        settings.Emission.RateOverTime = 630;
        using var simulation = new ParticleSimulation();

        simulation.Update(settings, Particles.At(), Particles.Frame);

        Assert.Equal(10, simulation.AliveCount);
        var ages = simulation.Particles.Age.ToArray().Order().ToArray();
        Assert.True(ages[^1] - ages[0] > Particles.Frame / 100 * 0.8f, "Particles emitted within one frame should have different ages.");
    }

    [Fact]
    public void EmissionScaleReducesTheRate()
    {
        var settings = Particles.Quiet();
        settings.Emission.RateOverTime = 100;
        using var simulation = new ParticleSimulation();

        Particles.Run(simulation, settings, 1, Particles.At() with { EmissionScale = 0.25f });

        Assert.InRange(simulation.AliveCount, 24, 25);
    }

    [Fact]
    public void BurstFiresAtItsTime()
    {
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0.5f, new MinMaxFloat(25)));
        using var simulation = new ParticleSimulation();

        Particles.Run(simulation, settings, 0.4f);
        Assert.Equal(0, simulation.AliveCount);

        Particles.Run(simulation, settings, 0.2f);
        Assert.Equal(25, simulation.AliveCount);
    }

    [Fact]
    public void BurstRepeatsForItsCycles()
    {
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(5), cycles: 3, interval: 0.1f));

        Assert.Equal(15, Particles.Emitted(settings, 0.5f));
    }

    [Fact]
    public void BurstWithoutCycleLimitRepeatsUntilTheCycleEnds()
    {
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(1), cycles: 0, interval: 0.25f));

        Assert.Equal(8, Particles.Emitted(settings, 1.95f));
    }

    [Fact]
    public void LoopingRepeatsBurstsEveryCycle()
    {
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(10)));

        Assert.Equal(30, Particles.Emitted(settings, 2.5f));
    }

    [Fact]
    public void NonLoopingEmitterPlaysOneCycle()
    {
        var settings = Particles.Quiet();
        settings.Looping = false;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(10)));
        using var simulation = new ParticleSimulation();

        Particles.Run(simulation, settings, 3);

        Assert.Equal(10, simulation.AliveCount);
        Assert.False(simulation.IsEmitting);
    }

    [Fact]
    public void BurstWithZeroProbabilityNeverFires()
    {
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(10), probability: 0));

        Assert.Equal(0, Particles.Emitted(settings, 1));
    }

    [Fact]
    public void BurstCountRangePicksWithinTheRange()
    {
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(5, 9)));

        Assert.InRange(Particles.Emitted(settings, 0.1f), 5, 9);
    }

    [Fact]
    public void RateOverDistanceEmitsAsTheEmitterMoves()
    {
        var settings = Particles.Quiet();
        settings.Emission.RateOverDistance = 0.5f;
        using var simulation = new ParticleSimulation();

        for (var i = 0; i <= 50; i++)
            simulation.Update(settings, Particles.At(new Vector2(i * 2, 0)), Particles.Frame);

        Assert.Equal(50, simulation.AliveCount);
        var positions = simulation.Particles.PositionX.ToArray();
        Assert.True(positions.Min() < 10 && positions.Max() > 90, "Particles should be left along the emitter's path.");
    }

    [Fact]
    public void StartDelayHoldsEmissionBack()
    {
        var settings = Particles.Quiet();
        settings.Duration = 10;
        settings.StartDelay = 0.5f;
        settings.Emission.RateOverTime = 10;

        Assert.InRange(Particles.Emitted(settings, 1), 4, 5);
    }

    [Fact]
    public void EmitAddsParticlesWithoutPlaying()
    {
        var settings = Particles.Quiet();
        settings.PlayOnStart = false;
        settings.Emission.RateOverTime = 100;
        using var simulation = new ParticleSimulation();

        simulation.Emit(12);
        Particles.Run(simulation, settings, 0.5f);

        Assert.Equal(12, simulation.AliveCount);
        Assert.False(simulation.IsPlaying);
    }

    [Fact]
    public void StopEndsEmissionAndClearRemovesParticles()
    {
        var settings = Particles.Quiet();
        settings.Emission.RateOverTime = 100;
        using var simulation = new ParticleSimulation();
        Particles.Run(simulation, settings, 0.5f);

        simulation.Stop();
        var alive = simulation.AliveCount;
        Particles.Run(simulation, settings, 0.5f);
        Assert.Equal(alive, simulation.AliveCount);

        simulation.Stop(ParticleStopBehavior.StopEmittingAndClear);
        Assert.Equal(0, simulation.AliveCount);
    }

    [Fact]
    public void PauseFreezesParticles()
    {
        var settings = Particles.Quiet();
        settings.Initial.Speed = new MinMaxFloat(100);
        settings.Emission.RateOverTime = 50;
        using var simulation = new ParticleSimulation();
        Particles.Run(simulation, settings, 0.5f);

        simulation.Pause();
        var before = simulation.Particles.PositionY.ToArray();
        Particles.Run(simulation, settings, 0.5f);

        Assert.Equal(before, simulation.Particles.PositionY.ToArray());
        simulation.Play();
        Particles.Run(simulation, settings, 0.1f);
        Assert.NotEqual(before[0], simulation.Particles.PositionY[0]);
    }

    [Fact]
    public void PrewarmStartsWithACycleAlreadyPlayed()
    {
        var settings = Particles.Quiet();
        settings.Initial.Lifetime = new MinMaxFloat(2);
        settings.Duration = 2;
        settings.Prewarm = true;
        settings.Emission.RateOverTime = 50;
        using var simulation = new ParticleSimulation();

        simulation.Update(settings, Particles.At(), Particles.Frame);

        Assert.InRange(simulation.AliveCount, 95, 101);
    }
}
