using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Physics;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Scripting.Compiler.Tests;

/// <summary>A project folder in the temp directory whose scripts tests write and compile.</summary>
internal sealed class TempProject : IDisposable
{
    public TempProject(ScriptConfiguration configuration = ScriptConfiguration.Debug)
    {
        Directory = Path.Combine(Path.GetTempPath(), "talesmith-scripts-" + Guid.NewGuid().ToString("N")[..8], "Test Game");
        System.IO.Directory.CreateDirectory(Directory);
        Options = new ScriptCompilerOptions { ProjectDirectory = Directory, Configuration = configuration };
    }

    public string Directory { get; }

    public ScriptCompilerOptions Options { get; }

    public string Write(string relativePath, string source)
    {
        var path = Path.GetFullPath(Path.Combine(Options.SourceRoot, relativePath));
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return path;
    }

    public void Delete(string relativePath) => File.Delete(Path.Combine(Options.SourceRoot, relativePath));

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Path.GetDirectoryName(Directory)!, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>A headless game with physics and compiled scripts, playing one scene.</summary>
internal sealed class TestGame : IAsyncDisposable
{
    private TestGame(Game game) => Game = game;

    public Game Game { get; }

    public World World => Game.Scenes.Current!.World;

    public static async Task<TestGame> StartAsync(ScriptAssembly? scripts, SceneDocument? scene = null)
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings());
        builder.Services.AddTalesmithPhysics();
        builder.Services.AddTalesmithScripting(options => options.Assembly = scripts);
        var game = builder.Build();
        var request = game.Services.GetRequiredService<InMemorySceneDocuments>().Add(scene ?? SceneDocument.Create());
        await game.Scenes.LoadAsync(request, SceneTransition.Instant, TestContext.Current.CancellationToken);
        return new TestGame(game);
    }

    public T Service<T>() where T : notnull => Game.Services.GetRequiredService<T>();

    public void Tick(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
            Game.Tick(1 / 60d);
    }

    public ValueTask DisposeAsync() => Game.DisposeAsync();

    /// <summary>A scene with one entity that has a transform and the given scripts, each with its saved fields.</summary>
    public static SceneDocument SceneWith(params (string Type, JsonObject Fields)[] scripts)
    {
        var scene = SceneDocument.Create();
        var entries = new JsonArray();
        foreach (var (type, fields) in scripts)
            entries.Add(new JsonObject { ["type"] = type, ["enabled"] = true, ["fields"] = fields });
        scene.Entities.Add(new EntityDocument
        {
            Id = Guid.NewGuid(),
            Name = "Hero",
            Components = [new ComponentDocument("Transform", []), new ComponentDocument("ScriptComponent", new JsonObject { ["scripts"] = entries })]
        });
        return scene;
    }

    /// <summary>The first script of a type, found by its full name, on any entity.</summary>
    public Script Find(string typeName)
    {
        foreach (var archetype in World.Query<ScriptComponent>())
        {
            foreach (var component in archetype.GetSpan<ScriptComponent>())
            {
                foreach (var script in component.Scripts)
                {
                    if (script.GetType().FullName == typeName)
                        return script;
                }
            }
        }

        throw new InvalidOperationException($"No {typeName} in the scene.");
    }
}
