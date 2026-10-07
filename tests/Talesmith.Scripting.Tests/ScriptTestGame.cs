using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Physics;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.Systems;

namespace Talesmith.Scripting.Tests;

/// <summary>A headless game with physics and scripting running a scene document.</summary>
internal sealed class ScriptTestGame : IAsyncDisposable
{
    public const double FrameSeconds = 1 / 60d;

    private ScriptTestGame(Game game) => Game = game;

    public Game Game { get; }

    public World World => Game.Scenes.Current!.World;

    public Scene Scene => Game.Scenes.Current!;

    public static async Task<ScriptTestGame> StartAsync(Action<IServiceCollection>? configure = null, SceneDocument? scene = null,
        ExecutionModes mode = ExecutionModes.Play, ScriptAssembly? scripts = null, GameSettings? settings = null)
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, settings ?? new GameSettings());
        builder.Services.AddTalesmithPhysics();
        configure?.Invoke(builder.Services);
        builder.Services.AddTalesmithScripting(options => options.Assembly = scripts);
        var game = builder.Build();
        game.Mode = mode;
        var request = game.Services.GetRequiredService<InMemorySceneDocuments>().Add(scene ?? SceneDocument.Create());
        await game.Scenes.LoadAsync(request, SceneTransition.Instant, TestContext.Current.CancellationToken);
        return new ScriptTestGame(game);
    }

    public T Service<T>() where T : notnull => Scene.Services.GetRequiredService<T>();

    public void Tick(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
            Game.Tick(FrameSeconds);
    }

    public Task UnloadAsync() => Game.Scenes.LoadAsync(Game.Services.GetRequiredService<InMemorySceneDocuments>().Add(SceneDocument.Create()), SceneTransition.Instant,
        TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => Game.DisposeAsync();
}
