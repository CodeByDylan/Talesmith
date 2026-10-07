using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Authoring;
using Talesmith.Diagnostics;
using Talesmith.Ecs;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;
using Talesmith.VFX.Presets;

namespace Talesmith.VFX.Tests;

public sealed class ParticleSystemTests
{
    [Fact]
    public async Task EmittersSimulateAndDrawAsOneBatchEach()
    {
        await using var game = await StartAsync(ExecutionModes.Play);
        Tick(game, 30);

        var emitters = Emitters(game);
        Assert.All(emitters, e => Assert.True(e.AliveCount > 0));
        var frame = game.Frames.BeginRead()!;
        try
        {
            var particleBatches = frame.Batches.ToArray().Where(b => b.Layer == RenderLayers.Effects).ToArray();
            Assert.Equal(emitters.Count, particleBatches.Length);
            Assert.Contains(particleBatches, b => b.Material == Material.Additive);
            Assert.InRange(particleBatches.Sum(b => b.InstanceCount), emitters.Count, emitters.Sum(e => e.AliveCount));
        }
        finally
        {
            game.Frames.EndRead(frame);
        }

        var stats = game.Services.GetRequiredService<ParticleStats>();
        Assert.Equal(emitters.Sum(e => e.AliveCount), stats.AliveParticles);
        Assert.Equal(emitters.Count, stats.Emitters);
        Assert.Equal(emitters.Count, stats.VisibleEmitters);
        Assert.True(stats.EmittedPerSecond > 0);
        Assert.Equal(stats.AliveParticles, game.Profilers.Game.LastFrame!.CounterValue(ParticleCounters.Alive));
    }

    [Fact]
    public async Task EditModeDoesNotSimulateButPreviewDoes()
    {
        await using var game = await StartAsync(ExecutionModes.Edit);
        Tick(game, 30);
        Assert.All(Emitters(game), e => Assert.Equal(0, e.AliveCount));

        game.Mode = ExecutionModes.Preview;
        Tick(game, 30);
        Assert.All(Emitters(game), e => Assert.True(e.AliveCount > 0));
    }

    [Fact]
    public async Task OffscreenLoopingEmittersPause()
    {
        await using var game = await StartAsync(ExecutionModes.Play);
        var world = game.Scenes.Current!.World;
        var far = world.Create();
        world.Set(far, new Transform(new Vector2(100_000, 0)));
        world.Set(far, new ParticleEmitter(BuiltInParticlePresets.Smoke()));
        Tick(game, 30);

        Assert.Equal(0, world.Get<ParticleEmitter>(far).AliveCount);
        Assert.False(world.Get<ParticleEmitter>(far).Simulation.IsVisible);
        Assert.Equal(1, game.Services.GetRequiredService<ParticleStats>().CulledEmitters);
    }

    [Fact]
    public async Task SceneBudgetCapsEveryEmitter()
    {
        await using var game = await StartAsync(ExecutionModes.Play, options => options.MaxParticlesPerScene = 50);
        Tick(game, 60);

        Assert.InRange(Emitters(game).Sum(e => e.AliveCount), 1, 50);
    }

    [Fact]
    public async Task SteadyStateDoesNotAllocate()
    {
        await using var game = await StartAsync(ExecutionModes.Play);
        Tick(game, 120);
        var simulate = ProfilerMarker.Get("Systems/ParticleSimulationSystem", "Systems");
        var draw = ProfilerMarker.Get("Systems/ParticleRenderSystem", "Systems");

        // Steady-state allocations show in every round; a one-off, such as the runtime's own work, in only one.
        var fewest = long.MaxValue;
        for (var round = 0; round < 5 && fewest > 0; round++)
        {
            var allocated = 0L;
            for (var i = 0; i < 60; i++)
            {
                game.Tick(1.0 / 60);
                var sample = game.Profilers.Game.LastFrame!;
                allocated += sample.MarkerAllocatedBytes(simulate) + sample.MarkerAllocatedBytes(draw);
            }

            fewest = Math.Min(fewest, allocated);
        }

        Assert.Equal(0, fewest);
    }

    [Fact]
    public void RegistrationAddsTheComponentAndIsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddTalesmithParticles();
        services.AddTalesmithParticles();

        Assert.Single(services, d => d.ImplementationInstance is ComponentRegistration { Type: var t } && t == typeof(ParticleEmitter));
        Assert.Single(services, d => d.ImplementationInstance is SystemDescriptor { Type: var t } && t == typeof(Systems.ParticleSimulationSystem));
    }

    private static List<ParticleEmitter> Emitters(Game game)
    {
        var emitters = new List<ParticleEmitter>();
        game.Scenes.Current!.World.Query<ParticleEmitter>().ForEach((Entity _, ref ParticleEmitter emitter) => emitters.Add(emitter));
        return emitters;
    }

    private static void Tick(Game game, int frames)
    {
        for (var i = 0; i < frames; i++)
            game.Tick(1.0 / 60);
    }

    private static async Task<Game> StartAsync(ExecutionModes mode, Action<ParticleOptions>? configure = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "talesmith-particle-tests");
        Directory.CreateDirectory(folder);
        var builder = GameBuilder.Create(folder, new GameSettings { StartScene = new SceneRequest(EffectsScene.Name) });
        builder.Services.AddTalesmithParticles();
        builder.Services.AddScene<EffectsScene>(EffectsScene.Name);
        var game = builder.Build();
        configure?.Invoke(game.Services.GetRequiredService<ParticleOptions>());
        game.Mode = mode;
        game.Start();

        var clock = Stopwatch.StartNew();
        while (game.Scenes.Current is null || game.Scenes.IsLoading)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(10))
                throw new TimeoutException("The scene did not load.");
            game.Tick(1.0 / 60);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        game.Mode = mode;
        return game;
    }

    private sealed class EffectsScene : Scene
    {
        public const string Name = "effects";

        protected override Task LoadAsync(CancellationToken cancellationToken)
        {
            Add(new Vector2(0, 0), BuiltInParticlePresets.Fire());
            Add(new Vector2(-100, 0), BuiltInParticlePresets.Smoke());
            Add(new Vector2(100, 0), BuiltInParticlePresets.Sparks());
            var camera = World.Create();
            World.Set(camera, new Camera(Vector2.Zero));
            return Task.CompletedTask;
        }

        private void Add(Vector2 position, ParticleSettings settings)
        {
            var entity = World.Create();
            World.Set(entity, new Transform(position));
            World.Set(entity, new ParticleEmitter(settings));
        }
    }
}
