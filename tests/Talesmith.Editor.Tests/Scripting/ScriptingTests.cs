using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Tests.Scripting;

public sealed class ScriptingTests
{
    private const string Broken = """
        namespace Fixture;

        public sealed class Mover : Script
        {
            protected override void Update() => Position += Speed;
        }
        """;

    private const string Fixed = """
        namespace Fixture;

        public sealed class Mover : Script
        {
            public float Speed = 4;

            protected override void Update() => Position += new Vector2(Speed, 0);
        }
        """;

    private const string Spin = """
        using Talesmith.Runtime.Scenes;

        namespace Fixture;

        [Component(Category = "Gameplay")]
        public struct Spin
        {
            public float Speed;
        }

        [UpdateIn(SystemPhase.Update)]
        public sealed class SpinSystem : ISystem
        {
            public void Update(in SystemContext context)
            {
            }
        }

        public sealed class SpinListener : ISceneListener
        {
            public void OnSceneStarted(Scene scene)
            {
            }
        }
        """;

    [Fact]
    public void ScriptsThatDeclareSystemsAndComponentsReachTheSceneView() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder =>
        {
            WriteScript(folder, Fixed);
            WriteScript(folder, Spin, "Spin.cs");
        });
        var scripts = editor.Get<IScriptService>();
        _ = editor.Get<EditSessionScripts>();
        var project = editor.Get<IProjectService>();
        var viewport = editor.Get<ViewportService>();
        await WaitIdleAsync(editor, scripts);
        await WaitForEditGameAsync(editor, game => FindSpin(game) is not null);

        var entity = editor.Document.Entities[0].Id;
        editor.Document.AddComponent(entity, new ComponentDocument(ComponentRegistry.GetTypeName(typeof(ScriptComponent)), new JsonObject
        {
            ["scripts"] = new JsonArray(new JsonObject { ["type"] = "Fixture.Mover", ["enabled"] = true, ["fields"] = new JsonObject() })
        }));
        editor.Get<ISelectionService>().SelectEntities([entity]);
        var inspector = new InspectorView(editor.Get<InspectorServices>());
        Assert.Contains("Speed", Labels(inspector));
        Assert.DoesNotContain(Labels(inspector), l => l.StartsWith("Missing script", StringComparison.Ordinal));

        var services = project.EditSession!.Game.Services;
        var menu = new AddComponentMenu(editor.Get<ISceneDocumentService>(), editor.Get<IUndoService>(), editor.Get<ProjectState>(),
            services.GetRequiredService<ComponentRegistry>, services.GetRequiredService<ScriptTypeRegistry>);
        var item = Assert.Single(menu.Items("Spin", [entity]), i => i.Value is IComponentDefinition { TypeName: "Fixture.Spin" });
        menu.Add(item, [entity]);
        await editor.World.WhenIdle;
        Assert.True(HasComponent(editor, entity, "Fixture.Spin"));

        var before = project.EditSession;
        viewport.Camera.Set(new Vector2(120, -40), 2);
        WriteScript(project.Project.Folder, Spin.Replace("public float Speed;", "public float Speed;\n    public float Radius;", StringComparison.Ordinal), "Spin.cs");
        Assert.True((await scripts.CompileAsync()).Success);
        await WaitIdleAsync(editor, scripts);
        await WaitForEditGameAsync(editor, game => FindSpin(game)?.Properties.Any(p => p.Name == "radius") == true);

        Assert.NotSame(before, project.EditSession);
        Assert.Equal([entity], editor.Get<ISelectionService>().Entities);
        Assert.Equal(new Vector2(120, -40), viewport.Camera.Position);
        Assert.Equal(viewport.Camera.ToCamera2D(), project.EditSession!.Game.CameraOverride);
        Assert.True(HasComponent(editor, entity, "Fixture.Spin"));
        Assert.Contains("Speed", Labels(inspector));
        Assert.DoesNotContain(Labels(inspector), l => l.StartsWith("Missing script", StringComparison.Ordinal));
    });

    [Fact]
    public void LanternGroveOpensWithEveryScriptKnown() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync("empty", folder =>
        {
            Directory.Delete(Path.Combine(folder, "assets"), recursive: true);
            CopyFolder(Path.Combine(RepositoryRoot(), "samples", "LanternGrove", "assets"), Path.Combine(folder, "assets"));
        });
        var scripts = editor.Get<IScriptService>();
        _ = editor.Get<EditSessionScripts>();
        await WaitIdleAsync(editor, scripts);
        await WaitForEditGameAsync(editor, game => game.Services.GetRequiredService<ScriptTypeRegistry>().Find("LanternGrove.PlayerController") is not null);

        var registry = editor.Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ScriptTypeRegistry>();
        Assert.Empty(registry.MissingTypes);
        var player = editor.Document.Entities.Single(e => e.Name == "Player").Id;
        editor.Get<ISelectionService>().SelectEntities([player]);
        var inspector = new InspectorView(editor.Get<InspectorServices>());
        Assert.Contains("Player Controller", Labels(inspector));
        Assert.DoesNotContain(Labels(inspector), l => l.StartsWith("Missing script", StringComparison.Ordinal));
    });

    [Fact]
    public void CompileErrorsReachTheConsoleWithTheirFileAndLine() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => WriteScript(folder, Broken));
        var scripts = editor.Get<IScriptService>();
        _ = editor.Get<EditSessionScripts>();
        await WaitIdleAsync(editor, scripts);

        Assert.True(scripts.HasErrors);
        var error = Assert.Single(editor.Console.Entries, e => e is { Source: ConsoleSource.Script, Severity: ConsoleSeverity.Error, Target: FileTarget });
        var target = Assert.IsType<FileTarget>(error.Target);
        Assert.EndsWith(Path.Combine("scripts", "Mover.cs"), target.Path, StringComparison.Ordinal);
        Assert.Equal(5, target.Line);
        Assert.Equal("CS0103", error.Category);
        Assert.StartsWith("Mover.cs(5,", error.Message, StringComparison.Ordinal);
        Assert.Contains(editor.Console.Entries, e => e.Message == "The scripts have 1 error; play mode starts once they compile");

        var status = editor.Get<StatusBarViewModel>().Items;
        _ = editor.Services.GetServices<IEditorCommandContributor>().OfType<ScriptCommands>().Single();
        var item = Assert.Single(status, i => i.Id == ScriptCommands.StatusItemId);
        Assert.Equal(StatusKind.Error, item.Kind);
        Assert.Equal("1 script error", item.Text);
        Assert.NotNull(item.Command);
    });

    [Fact]
    public void InfoDiagnosticsReachTheConsoleAsInfo() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => WriteScript(folder, Fixed.Replace(
            "Position += new Vector2(Speed, 0);", "Log.Debug(\"Speed \" + Speed);", StringComparison.Ordinal)));
        var scripts = editor.Get<IScriptService>();
        await WaitIdleAsync(editor, scripts);

        Assert.True(scripts.LastResult!.Success);
        var info = Assert.Single(editor.Console.Entries, e => e.Category == "TS1003");
        Assert.Equal(ConsoleSeverity.Info, info.Severity);
        Assert.Equal(ConsoleSource.Script, info.Source);
        Assert.IsType<FileTarget>(info.Target);
    });

    [Fact]
    public void PlayWaitsForFixedScriptsAndRunsTheLatestCompilation() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => WriteScript(folder, Broken));
        var scripts = editor.Get<IScriptService>();
        _ = editor.Get<EditSessionScripts>();
        var play = editor.Get<IPlayModeService>();
        await WaitIdleAsync(editor, scripts);

        await play.PlayAsync();

        Assert.Equal(PlayState.Stopped, play.State);
        Assert.Null(play.Session);
        Assert.Contains(editor.Console.Entries, e => e.Severity == ConsoleSeverity.Warning && e.Message.StartsWith("Play mode did not start", StringComparison.Ordinal));

        WriteScript(editor.Get<IProjectService>().Project.Folder, Fixed);
        var result = await scripts.CompileAsync();
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        await WaitIdleAsync(editor, scripts);
        await play.PlayAsync();

        Assert.Equal(PlayState.Playing, play.State);
        var running = play.Game!.Services.GetRequiredService<ScriptingOptions>().Assembly;
        Assert.Same(scripts.Assembly, running);
        Assert.Contains(running!.ScriptTypes, t => t.FullName == "Fixture.Mover");
        var registry = editor.Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ScriptTypeRegistry>();
        Assert.NotNull(registry.Find("Fixture.Mover"));
        await play.StopAsync();
    });

    [Fact]
    public void ChangedScriptsAreSwappedIntoTheRunningGame() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => WriteScript(folder, Fixed));
        var scripts = editor.Get<IScriptService>();
        _ = editor.Get<EditSessionScripts>();
        var reloader = editor.Get<ScriptHotReloader>();
        var play = editor.Get<IPlayModeService>();
        await WaitIdleAsync(editor, scripts);
        await play.PlayAsync();
        var first = scripts.Assembly;

        WriteScript(editor.Get<IProjectService>().Project.Folder, Fixed.Replace("Speed = 4", "Speed = 8", StringComparison.Ordinal));
        await scripts.CompileAsync();
        await editor.WaitAsync(() => reloader.LastResult is not null);

        Assert.NotSame(first, scripts.Assembly);
        Assert.False(reloader.LastResult!.RestartRequired);
        Assert.False(reloader.RestartPending);
        Assert.Same(scripts.Assembly, play.Game!.Services.GetRequiredService<ScriptingOptions>().Assembly);
        await play.StopAsync();
    });

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Talesmith.slnx")))
                return folder.FullName;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (Path.GetFileName(directory) is not ("bin" or "obj" or ".talesmith"))
                CopyFolder(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static IComponentDefinition? FindSpin(Game game) => game.Services.GetRequiredService<ComponentRegistry>().Find("Fixture.Spin");

    private static bool HasComponent(EditorFixture editor, Guid entity, string typeName)
    {
        if (editor.World.World is not { } world || !editor.World.TryGetEntity(entity, out var runtime))
            return false;
        foreach (var id in world.GetComponentIds(runtime))
        {
            if (ComponentType.FromId(id).Type.FullName == typeName)
                return true;
        }

        return false;
    }

    /// <summary>Waits until the scene view shows the open scene in the current edit game and that game satisfies the condition.</summary>
    private static async Task WaitForEditGameAsync(EditorFixture editor, Func<Game, bool> condition)
    {
        var project = editor.Get<IProjectService>();
        try
        {
            await editor.WaitAsync(() => project.EditSession?.Game is { } game && ReferenceEquals(editor.World.Game, game) && editor.World.World is not null
                                         && !editor.World.IsBusy && condition(game), 60000);
        }
        catch (TimeoutException ex)
        {
            var met = project.EditSession?.Game is { } game && condition(game);
            throw new TimeoutException($"{ex.Message} The current edit game {(met ? "meets" : "does not meet")} the condition.", ex);
        }
    }

    private static List<string> Labels(InspectorView view)
    {
        Dispatcher.UIThread.RunJobs();
        var shown = view.Shown?.GetSelfAndLogicalDescendants().ToList() ?? [];
        return
        [
            .. shown.OfType<PropertyGroup>().Select(g => g.Header?.ToString() ?? ""),
            .. shown.OfType<PropertyRow>().Select(r => r.Label ?? "")
        ];
    }

    private static void WriteScript(string projectFolder, string source, string file = "Mover.cs")
    {
        var path = Path.Combine(projectFolder, "assets", "scripts", file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private static async Task WaitIdleAsync(EditorFixture editor, IScriptService scripts)
    {
        await editor.WaitAsync(() => scripts.WhenIdle.IsCompleted && scripts.LastResult is not null, 60000);
        await scripts.WhenIdle;
    }
}
