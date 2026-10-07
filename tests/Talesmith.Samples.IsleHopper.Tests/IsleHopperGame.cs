using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Input;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Samples.IsleHopper.Tests;

/// <summary>Starts the Isle Hopper sample headless and finds its hero.</summary>
internal static class IsleHopperGame
{
    /// <summary>Starts the sample with its gameplay plugin, waits for the level, and lets the hero settle on the spawn point.</summary>
    public static async Task<Game> StartAsync()
    {
        var builder = GameBuilder.Create(AssetRoot());
        new IsleHopperPlugin().Configure(new PluginBuilder(builder.Services));
        var game = builder.Build();
        game.Viewport.Size = new System.Numerics.Vector2(1280, 800);
        game.Services.GetRequiredService<IInputSink>().FocusChanged(true);
        game.Start();

        var clock = Stopwatch.StartNew();
        while (game.Scenes.Current is null || game.Scenes.IsLoading || game.Scenes.Current.World.Query<Hero>().IsEmpty)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The level did not load.");
            game.Tick(1.0 / 60);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < 30; i++)
            game.Tick(1.0 / 60);
        return game;
    }

    public static Hero Hero(Game game) => World(game).Get<Hero>(HeroEntity(game));

    public static World World(Game game) => game.Scenes.Current!.World;

    public static Entity HeroEntity(Game game) =>
        World(game).Query<Hero>().TryGetSingle(out var hero) ? hero : throw new InvalidOperationException("The hero was not spawned.");

    private static string AssetRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Talesmith.slnx")))
                return Path.Combine(folder.FullName, "samples", "IsleHopper", "assets");
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private sealed class PluginBuilder(IServiceCollection services) : IPluginBuilder
    {
        public IServiceCollection Services { get; } = services;

        public PluginInfo Plugin { get; } = new("samples.islehopper", "Isle Hopper gameplay", new Version(1, 0), AppContext.BaseDirectory);

        public IPluginSettings Settings { get; } = new PluginSettings("samples.islehopper");
    }
}
