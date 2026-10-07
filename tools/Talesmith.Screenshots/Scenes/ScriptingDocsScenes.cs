using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Console;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Screenshots.Capture;
using Talesmith.Scripting;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The projects the scripting screenshots open, copied or created in a temporary folder.</summary>
internal static class ScriptingDocsFixture
{
    private static readonly Lazy<string> Folder = new(Create);

    /// <summary>A copy of the Lantern Grove sample.</summary>
    public static string LanternGrove => Path.Combine(Folder.Value, "LanternGrove");

    /// <summary>A platformer with one script attached to its hero, for hot reload.</summary>
    public static string Starfall => Path.Combine(Folder.Value, "Starfall");

    /// <summary>A platformer whose script trips the game-loop analyzers.</summary>
    public static string GullPoint => Path.Combine(Folder.Value, "Gull Point");

    public const string BobberSource = """
        namespace Starfall;

        /// <summary>Bobs the entity up and down around where it started.</summary>
        public sealed class Bobber : Script
        {
            [Range(0, 64)]
            public float Height = 6;

            [Range(0, 10)]
            public float Speed = SPEED;

            private Vector2 _home;

            protected override void OnStart() => _home = Position;

            protected override void Update() =>
                Position = _home + new Vector2(0, MathF.Sin((float)Time.TotalTime * Speed) * Height);
        }
        """;

    private static string Create()
    {
        var folder = Path.Combine(Path.GetTempPath(), "talesmith-scripting-shots", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        };

        Copy(Path.Combine(Repository.Root, "samples", "LanternGrove"), Path.Combine(folder, "LanternGrove"));
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        Task.Run(async () =>
        {
            await creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), folder, "Starfall");
            await creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), folder, "Gull Point");
        }).GetAwaiter().GetResult();

        var starfall = Directory.CreateDirectory(Path.Combine(folder, "Starfall", "assets", "scripts")).FullName;
        File.WriteAllText(Path.Combine(starfall, "Bobber.cs"), BobberSource.Replace("SPEED", "2", StringComparison.Ordinal));

        var gull = Directory.CreateDirectory(Path.Combine(folder, "Gull Point", "assets", "scripts")).FullName;
        File.WriteAllText(Path.Combine(gull, "Radar.cs"), """
            using System.IO;

            namespace GullPoint;

            /// <summary>Finds the nearest enemy every frame and saves a report when asked.</summary>
            public sealed class Radar : Script
            {
                public float Range = 400;

                protected override void Update()
                {
                    var nearby = new List<Entity>();
                    FindAllWithTag("enemy", nearby);
                    var closest = nearby.OrderBy(e => Vector2.Distance(Position, GetComponent<Transform>(e).Position)).FirstOrDefault();
                    if (Input.WasPressed(Key.F5))
                        File.WriteAllText("radar.txt", $"Closest: {closest}");
                }

                private async void Ping()
                {
                    await Wait(1);
                    Log.Info("Ping");
                }
            }
            """);
        return folder;
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name is "bin" or "obj" or ".talesmith")
                continue;
            Copy(directory, Path.Combine(target, name));
        }
    }
}

/// <summary>An editor window on one of the scripting projects, waiting until its scripts compiled.</summary>
internal abstract class ScriptingEditorScene : ScreenshotScene
{
    private OpenEditor? _editor;

    public override Size Size => new(1440, 880);

    protected abstract string Project { get; }

    protected OpenEditor Editor => _editor!;

    public override Control Build()
    {
        _editor = EditorFixture.Open(Project);
        return _editor.Root;
    }

    public override void Prepare(Window window)
    {
        Editor.Attach(window);
        var project = Editor.Get<IProjectService>();
        RenderLoop.Wait(() => project.ScanProgress is null, 30000);
        var scripts = Editor.Get<IScriptService>();
        RenderLoop.Wait(() => scripts.LastResult is not null && !scripts.IsCompiling, 120000);
        RenderLoop.Settle(300);
        Customize(window);
    }

    public override void Release()
    {
        _editor?.Close();
        _editor = null;
    }

    protected abstract void Customize(Window window);

    protected T Get<T>()
        where T : notnull => Editor.Get<T>();

    protected void Select(string entityName)
    {
        if (Get<ISceneDocumentService>().Active?.Entities.FirstOrDefault(e => e.Name == entityName) is { } entity)
            Get<ISelectionService>().SelectEntity(entity.Id);
        RenderLoop.Settle(300);
    }

    protected AddComponentMenu AddMenu()
    {
        var services = Get<IProjectService>().EditSession!.Game.Services;
        return new AddComponentMenu(Get<ISceneDocumentService>(), Get<Talesmith.Editor.Undo.IUndoService>(), Get<ProjectState>(),
            () => services.GetService<Talesmith.Runtime.Serialization.ComponentRegistry>(), () => services.GetService<ScriptTypeRegistry>());
    }

    /// <summary>Adds a script to the selected entity, found by its display name.</summary>
    protected void AttachScript(string displayName)
    {
        var menu = AddMenu();
        var targets = Get<ISelectionService>().Entities;
        if (menu.Items(displayName, targets).FirstOrDefault(i => i.Value is ScriptTypeInfo info && info.DisplayName == displayName) is { } item)
            menu.Add(item, targets);
        RenderLoop.Settle(400);
    }

    protected void Play()
    {
        var play = Get<IPlayModeService>();
        _ = play.PlayAsync();
        PlayWait.UntilSceneLoaded(play);
        RenderLoop.Settle(800);
    }
}

/// <summary>The Create script dialog naming a new Coin script.</summary>
internal sealed class CreateScriptScene : ScriptingEditorScene
{
    public override string Name => "scripting-create-script";

    protected override string Project => ScriptingDocsFixture.Starfall;

    protected override void Customize(Window window)
    {
        var dialog = new CreateScriptDialogViewModel(Get<IDialogService>(), Get<IScriptService>().Options.SourceRoot) { ClassName = "Coin" };
        _ = Get<IDialogService>().ShowAsync(dialog);
        RenderLoop.Settle(400);
    }
}

/// <summary>The inspector on Lantern Grove's player, showing the fields of its Player Controller script.</summary>
internal sealed class ScriptFieldsScene : ScriptingEditorScene
{
    private Control? _inspector;

    public override string Name => "scripting-script-fields";

    public override Size Size => new(1440, 965);

    protected override string Project => ScriptingDocsFixture.LanternGrove;

    protected override void Customize(Window window)
    {
        Select("Player");
        foreach (var group in window.GetVisualDescendants().OfType<PropertyGroup>())
            group.IsExpanded = group.Header is "Player Controller";
        RenderLoop.Settle(400);
        _inspector = window.GetVisualDescendants().OfType<InspectorView>().FirstOrDefault();
    }

    public override Control? Region(Window window) => _inspector;
}

/// <summary>Warnings of the game-loop analyzers in the console, with the status bar counting them.</summary>
internal sealed class AnalyzerWarningsScene : ScriptingEditorScene
{
    public override string Name => "scripting-analyzer-warnings";

    protected override string Project => ScriptingDocsFixture.GullPoint;

    protected override void Customize(Window window)
    {
        var layout = Get<LayoutService>();
        layout.ShowPanel(PanelIds.Console);
        var console = Get<ConsoleViewModel>();
        console.SourceIndex = (int)ConsoleSource.Script + 1;
        RenderLoop.Settle(200);
        console.SelectedRow = console.Rows.FirstOrDefault(r => r.Entry.Severity == ConsoleSeverity.Warning && r.Entry.Target is FileTarget);
        RenderLoop.Settle(300);
    }
}

/// <summary>Play mode just after a script changed: the running game took the new code and the toast says so.</summary>
internal sealed class HotReloadScene : ScriptingEditorScene
{
    private static int _edits;

    public override string Name => "scripting-hot-reload";

    protected override string Project => ScriptingDocsFixture.Starfall;

    protected override void Customize(Window window)
    {
        Select("Hero");
        AttachScript("Bobber");
        foreach (var group in window.GetVisualDescendants().OfType<PropertyGroup>())
            group.IsExpanded = group.Header is "Bobber";

        Play();
        var reloader = Get<ScriptHotReloader>();
        var path = Path.Combine(Get<IScriptService>().Options.SourceRoot, "Bobber.cs");
        File.WriteAllText(path, ScriptingDocsFixture.BobberSource.Replace("SPEED", (3 + ++_edits).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        RenderLoop.Wait(() => reloader.LastResult is not null, 120000);
        RenderLoop.Settle(800);
    }
}

/// <summary>Lantern Grove playing after a script changed: its scripts declare components and systems, so play mode has to restart.</summary>
internal sealed class HotReloadRestartScene : ScriptingEditorScene
{
    private static int _edits;

    public override string Name => "scripting-hot-reload-restart";

    protected override string Project => ScriptingDocsFixture.LanternGrove;

    protected override void Customize(Window window)
    {
        Play();
        var reloader = Get<ScriptHotReloader>();
        var path = Path.Combine(Get<IScriptService>().Options.SourceRoot, "Lantern.cs");
        File.AppendAllText(path, $"{Environment.NewLine}// {++_edits}{Environment.NewLine}");
        RenderLoop.Wait(() => reloader.LastResult is not null, 120000);
        RenderLoop.Settle(800);
    }
}

/// <summary>Lantern Grove running in the Game panel.</summary>
internal sealed class LanternGrovePlayScene : ScriptingEditorScene
{
    public override string Name => "scripting-lantern-grove-play";

    protected override string Project => ScriptingDocsFixture.LanternGrove;

    protected override void Customize(Window window)
    {
        Play();
        Select("Player");
        RenderLoop.Settle(800);
        foreach (var group in window.GetVisualDescendants().OfType<PropertyGroup>())
            group.IsExpanded = group.Header is "Player Controller";
        RenderLoop.Settle(1000);
    }
}
