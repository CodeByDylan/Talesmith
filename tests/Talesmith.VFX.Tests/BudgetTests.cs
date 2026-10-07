namespace Talesmith.VFX.Tests;

public sealed class BudgetTests
{
    [Fact]
    public void EmitterStopsAtItsMaxParticles()
    {
        var settings = Particles.Quiet();
        settings.MaxParticles = 40;
        settings.Emission.RateOverTime = 1000;

        Assert.Equal(40, Particles.Emitted(settings, 1));
    }

    [Fact]
    public void EmittersShareTheSceneBudget()
    {
        var budget = new ParticleBudget(100);
        var settings = Particles.Quiet();
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(80)));
        using var first = new ParticleSimulation();
        using var second = new ParticleSimulation();

        first.Update(settings, Particles.At(budget: budget), Particles.Frame);
        second.Update(settings, Particles.At(budget: budget), Particles.Frame);

        Assert.Equal(80, first.AliveCount);
        Assert.Equal(20, second.AliveCount);
        Assert.Equal(100, budget.Used);
        Assert.Equal(0, budget.Remaining);
    }

    [Fact]
    public void DyingParticlesReturnTheirRoom()
    {
        var budget = new ParticleBudget(50);
        var settings = Particles.Quiet();
        settings.Initial.Lifetime = new MinMaxFloat(0.5f);
        settings.Emission.RateOverTime = 1000;
        using var simulation = new ParticleSimulation();

        for (var i = 0; i < 120; i++)
        {
            simulation.Update(settings, Particles.At(budget: budget), Particles.Frame);
            Assert.True(budget.Used <= 50);
            Assert.Equal(simulation.AliveCount, budget.Used);
        }

        Assert.Equal(50, simulation.AliveCount);
    }

    [Fact]
    public void ReserveGrantsOnlyWhatRemains()
    {
        var budget = new ParticleBudget(10);

        Assert.Equal(6, budget.Reserve(6));
        Assert.Equal(4, budget.Reserve(6));
        Assert.Equal(0, budget.Reserve(1));
        Assert.Equal(1, budget.Pressure);

        budget.Release(5);
        Assert.Equal(5, budget.Remaining);
    }
}
