using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Plugins;
using Talesmith.Runtime.Serialization;
using Talesmith.Screenshots.Capture;
using Talesmith.Scripting.Compiler;
using Talesmith.UI.Controls;
using Talesmith.UI.Docking;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>Projects for the plugins section: Hex Quest with its built plugins, and platformer projects with the Spinners plugin from the
/// tutorial, compiled from the same source the pages show.</summary>
internal static class PluginDocsProjects
{
    private static readonly Lazy<string> Root = new(() => Directory.CreateDirectory(Path.Combine(EditorFixture.Scratch, "plugins-docs")).FullName);
    private static readonly Lazy<string> HexQuestFolder = new(CreateHexQuest);
    private static readonly Lazy<string> SpinnersFolder = new(CreateSpinners);
    private static readonly Lazy<string> ProblemsFolder = new(CreateProblems);
    private static readonly Lazy<string> FreshFolder = new(() => CreatePlatformer("fresh"));
    private static readonly Lazy<(byte[] Runtime, byte[] Editor)> SpinnersBuild = new(BuildSpinners);

    /// <summary>A copy of Hex Quest with its gameplay and cutscene plugins installed.</summary>
    public static string HexQuest => HexQuestFolder.Value;

    /// <summary>Coral Cove with the Spinners plugin and its editor part installed, and settings saved for it.</summary>
    public static string Spinners => SpinnersFolder.Value;

    /// <summary>Coral Cove with plugins that fail, are skipped, are switched off, use an undeclared permission or have a failing editor part.</summary>
    public static string Problems => ProblemsFolder.Value;

    /// <summary>Coral Cove without plugins.</summary>
    public static string Fresh => FreshFolder.Value;

    public const string SpinnersId = "coral-cove.spinners";

    /// <summary>Installs the runtime part of Spinners into a project, as building the tutorial's project does.</summary>
    public static void InstallSpinners(string project, bool editorPart)
    {
        var folder = Directory.CreateDirectory(Path.Combine(project, "assets", "plugins", "spinners")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "Spinners.dll"), SpinnersBuild.Value.Runtime);
        var manifest = new JsonObject
        {
            ["id"] = SpinnersId,
            ["name"] = "Spinners",
            ["version"] = "1.0.0",
            ["description"] = "Turns entities with a Spinner component.",
            ["authors"] = new JsonArray("dylan"),
            ["assembly"] = "Spinners.dll",
            ["contractVersion"] = EngineInfo.ContractVersion,
            ["permissions"] = new JsonArray("runtimeScene"),
            ["extensions"] = new JsonArray("components", "systems")
        };
        if (editorPart)
        {
            File.WriteAllBytes(Path.Combine(folder, "Spinners.Editor.dll"), SpinnersBuild.Value.Editor);
            manifest["editorAssembly"] = "Spinners.Editor.dll";
            manifest["permissions"] = new JsonArray("runtimeScene", "editorUi");
            manifest["extensions"] = new JsonArray("components", "systems", "editor.panels", "editor.commands", "editor.tools");
        }

        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public static void RemoveSpinners(string project)
    {
        var folder = Path.Combine(project, "assets", "plugins", "spinners");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private static string CreatePlatformer(string parent)
    {
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        var folder = Directory.CreateDirectory(Path.Combine(Root.Value, parent)).FullName;
        return Task.Run(() => creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), folder, "Coral Cove")).GetAwaiter().GetResult();
    }

    private static string CreateSpinners()
    {
        var project = CreatePlatformer("spinners");
        InstallSpinners(project, editorPart: true);
        File.WriteAllText(Path.Combine(project, "assets", "config", "plugins.json"), """
            {
              "disabled": [],
              "settings": {
                "coral-cove.spinners": { "speedScale": 1.5, "reverseAll": false }
              }
            }
            """);
        return project;
    }

    private static string CreateProblems()
    {
        var project = CreatePlatformer("problems");
        var plugins = Path.Combine(project, "assets", "plugins");
        Install(plugins, "weather", "Weather.dll", Compile("Weather", [WeatherSource], []), """
            {
              "id": "acme.weather",
              "name": "Weather",
              "version": "2.1.0",
              "description": "Rain and snow that follow the time of day, read from forecast.json.",
              "authors": [ "Acme Games" ],
              "assembly": "Weather.dll",
              "contractVersion": 1,
              "permissions": [ "runtimeScene" ],
              "extensions": [ "systems" ]
            }
            """);
        Install(plugins, "questlog", "QuestLog.dll", Compile("QuestLog", [EmptyPlugin("QuestLog")], []), """
            {
              "id": "acme.quest-log",
              "name": "Quest log",
              "version": "1.0.0",
              "description": "Keeps track of quests and shows them in a journal.",
              "authors": [ "Acme Games" ],
              "assembly": "QuestLog.dll",
              "contractVersion": 1,
              "dependencies": [ { "id": "tools.dialogue", "version": "^2.0" } ],
              "extensions": [ "services", "overlays" ]
            }
            """);
        Install(plugins, "photomode", "PhotoMode.dll", Compile("PhotoMode", [EmptyPlugin("PhotoMode")], []), """
            {
              "id": "acme.photo-mode",
              "name": "Photo mode",
              "version": "1.3.0",
              "description": "Pauses the game and frees the camera for screenshots.",
              "authors": [ "Acme Games" ],
              "assembly": "PhotoMode.dll",
              "contractVersion": 1,
              "extensions": [ "services" ]
            }
            """);
        var notesRuntime = Compile("MapNotes", [EmptyPlugin("MapNotes")], []);
        Install(plugins, "mapnotes", "MapNotes.dll", notesRuntime, """
            {
              "id": "acme.map-notes",
              "name": "Map notes",
              "version": "0.4.0",
              "description": "Pins notes to places on the map while you edit.",
              "authors": [ "Acme Games" ],
              "assembly": "MapNotes.dll",
              "editorAssembly": "MapNotes.Editor.dll",
              "contractVersion": 1,
              "permissions": [ "editorUi" ],
              "extensions": [ "editor.commands" ]
            }
            """);
        File.WriteAllBytes(Path.Combine(plugins, "mapnotes", "MapNotes.Editor.dll"),
            Compile("MapNotes.Editor", [MapNotesEditorSource], [MetadataReference.CreateFromImage(notesRuntime)]));
        var minimap = Directory.CreateDirectory(Path.Combine(plugins, "minimap")).FullName;
        File.WriteAllText(Path.Combine(minimap, "plugin.json"), """{ "id": "Minimap", "versoin": "1.0", "assembly": "Minimap.exe", "contractVersion": 1 }""");
        File.WriteAllText(Path.Combine(project, "assets", "config", "plugins.json"), """{ "disabled": [ "acme.photo-mode" ] }""");
        return project;
    }

    private static void Install(string plugins, string folderName, string assembly, byte[] image, string manifest)
    {
        var folder = Directory.CreateDirectory(Path.Combine(plugins, folderName)).FullName;
        File.WriteAllBytes(Path.Combine(folder, assembly), image);
        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest);
    }

    private static string CreateHexQuest()
    {
        var samples = Path.Combine(Repository.Root, "samples");
        foreach (var plugin in new[] { "HexQuest", "Cutscenes" })
        {
            var installed = Path.Combine(samples, "HexQuest", "assets", "plugins", plugin.ToLowerInvariant(), $"Talesmith.Samples.{plugin}.dll");
            if (!File.Exists(installed))
                BuildSample(Path.Combine(samples, $"Talesmith.Samples.{plugin}", $"Talesmith.Samples.{plugin}.csproj"));
        }

        var target = Path.Combine(Root.Value, "HexQuest");
        Copy(Path.Combine(samples, "HexQuest"), target);
        return target;
    }

    private static void BuildSample(string project)
    {
        using var process = Process.Start(new ProcessStartInfo("dotnet", ["build", project, "-nologo", "-v:q"]) { RedirectStandardOutput = true })!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Building {project} failed.");
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            Copy(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    private static (byte[] Runtime, byte[] Editor) BuildSpinners()
    {
        var runtime = Compile("Spinners", [SpinnerSource, SpinSystemSource, SpinnersPluginSource], []);
        var editor = Compile("Spinners.Editor",
            [SpinnersEditorPluginSource, SpinnersPanelSource, SpinnerCommandsSource, SpinnerToolSource, SpinnerGizmoSource],
            [MetadataReference.CreateFromImage(runtime)]);
        return (runtime, editor);
    }

    private static byte[] Compile(string assemblyName, IEnumerable<string> sources, IEnumerable<MetadataReference> extra)
    {
        var trees = sources.Append(GlobalUsings).Select(source => CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)));
        var compilation = CSharpCompilation.Create(assemblyName, trees, References.Value.Concat(extra),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        if (!result.Success)
            throw new InvalidOperationException($"{assemblyName} did not compile:{Environment.NewLine}{string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))}");
        return image.ToArray();
    }

    private static readonly Lazy<IReadOnlyList<MetadataReference>> References = new(() =>
    [
        .. ScriptReferences.Framework()
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll").Where(IsLibrary))
            .DistinctBy(Path.GetFileName)
            .Select(path => MetadataReference.CreateFromFile(path))
    ]);

    private static bool IsLibrary(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        string[] prefixes = ["Talesmith.", "Avalonia", "CommunityToolkit", "Microsoft.Extensions.", "SkiaSharp", "HarfBuzzSharp"];
        return prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) && name != "Talesmith.Screenshots";
    }

    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        """;

    private static string EmptyPlugin(string name) => $$"""
        namespace {{name}};

        public sealed class {{name}}Plugin : Talesmith.Plugins.IPlugin
        {
            public void Configure(Talesmith.Plugins.IPluginBuilder builder)
            {
            }
        }
        """;

    private const string WeatherSource = """
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Plugins;
        using Talesmith.Systems;

        namespace Weather;

        public sealed class WeatherPlugin : IPlugin
        {
            public void Configure(IPluginBuilder builder)
            {
                var forecast = Path.Combine(builder.Plugin.Directory, "forecast.json");
                builder.Services.AddSingleton(new Forecast(File.Exists(forecast) ? File.ReadAllText(forecast) : "{}"));
                builder.Services.AddSystem<RainSystem>();
            }
        }

        public sealed record Forecast(string Json);

        public sealed class RainSystem : ISystem
        {
            public void Update(in SystemContext context)
            {
            }
        }
        """;

    private const string SpinnerSource = """
        using Talesmith.Authoring;

        namespace Spinners;

        [Component(Category = "Motion", Icon = "rotate-cw", Description = "Turns the entity at a constant speed.")]
        public struct Spinner
        {
            [Range(-720, 720)]
            [Tooltip("Degrees per second. Negative values turn counterclockwise.")]
            public float Speed;

            public Spinner()
            {
                Speed = 90;
            }
        }
        """;

    private const string SpinSystemSource = """
        using Talesmith.Ecs;
        using Talesmith.Runtime.Components;
        using Talesmith.Systems;

        namespace Spinners;

        [UpdateIn(SystemPhase.Update)]
        public sealed class SpinSystem : ISystem
        {
            private static readonly QueryDescription TopLevel = QueryDescription.With<Spinner>().And<Transform>().Without<LocalTransform>();
            private static readonly QueryDescription Children = QueryDescription.With<Spinner>().And<LocalTransform>();

            public void Update(in SystemContext context)
            {
                var radians = context.Time.DeltaTime * MathF.PI / 180;
                foreach (var archetype in context.World.Query(TopLevel))
                {
                    var spinners = archetype.GetSpan<Spinner>();
                    var transforms = archetype.GetSpan<Transform>();
                    for (var i = 0; i < archetype.Count; i++)
                        transforms[i].Rotation += spinners[i].Speed * radians;
                }

                foreach (var archetype in context.World.Query(Children))
                {
                    var spinners = archetype.GetSpan<Spinner>();
                    var locals = archetype.GetSpan<LocalTransform>();
                    for (var i = 0; i < archetype.Count; i++)
                        locals[i].Rotation += spinners[i].Speed * radians;
                }
            }
        }
        """;

    private const string SpinnersPluginSource = """
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Authoring;
        using Talesmith.Plugins;
        using Talesmith.Systems;

        namespace Spinners;

        public sealed class SpinnersPlugin : IPlugin
        {
            public void Configure(IPluginBuilder builder)
            {
                builder.Services.AddComponent<Spinner>();
                builder.Services.AddSystem<SpinSystem>();
            }
        }
        """;

    /// <summary>An editor part whose command takes the id of the editor's own Save command, so the editor rejects it.</summary>
    private const string MapNotesEditorSource = """
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Editor.Commands;
        using Talesmith.Editor.Plugins;

        namespace MapNotes.Editor;

        public sealed class MapNotesEditorPlugin : IEditorPlugin
        {
            public void ConfigureServices(IServiceCollection services) => services.AddEditorCommands<MapNoteCommands>();
        }

        public sealed class MapNoteCommands : IEditorCommandContributor
        {
            public void Contribute(CommandBuilder builder) => builder.Add("file.save", "Save notes", "Map notes", () => { });
        }
        """;

    private const string SpinnersEditorPluginSource = """
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Editor.Commands;
        using Talesmith.Editor.Panels;
        using Talesmith.Editor.Plugins;
        using Talesmith.Editor.Viewport.Gizmos;
        using Talesmith.Editor.Viewport.Tools;
        using Talesmith.UI;

        namespace Spinners.Editor;

        public sealed class SpinnersEditorPlugin : IEditorPlugin
        {
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddEditorPanel<SpinnersPanel>(new EditorPanelInfo("spinners", "Spinners", Icons.RotateCw, DockLocation.Right) { Order = 40 });
                services.AddEditorCommands<SpinnerCommands>();
                services.AddViewportTool<SpinnerTool>();
                services.AddGizmoProvider<SpinnerGizmo>();
            }
        }
        """;

    private const string SpinnersPanelSource = """
        using Avalonia;
        using Avalonia.Controls;
        using Avalonia.Layout;
        using Talesmith.Editor.Documents;
        using Talesmith.Editor.Inspector;
        using Talesmith.Editor.Panels;
        using Talesmith.Editor.Selection;

        namespace Spinners.Editor;

        public sealed class SpinnersPanel(ISceneDocumentService documents, ISelectionService selection) : IEditorPanel
        {
            private readonly StackPanel _rows = new() { Spacing = 2, Margin = new Thickness(8) };
            private SceneDocumentModel? _document;

            public Control CreateContent()
            {
                documents.ActiveChanged += (_, _) => Watch(documents.Active);
                Watch(documents.Active);
                return new ScrollViewer { Content = _rows };
            }

            private void Watch(SceneDocumentModel? document)
            {
                if (_document is not null)
                    _document.Changed -= OnDocumentChanged;
                _document = document;
                if (document is not null)
                    document.Changed += OnDocumentChanged;
                Refresh();
            }

            private void OnDocumentChanged(object? sender, SceneChangedEventArgs e) => Refresh();

            private void Refresh()
            {
                _rows.Children.Clear();
                foreach (var entity in _document?.Entities ?? [])
                {
                    if (entity.FindComponent(SpinnerCommands.ComponentType) is not { } spinner)
                        continue;
                    var id = entity.Id;
                    var speed = JsonValues.Number(spinner.Data["speed"]) ?? 90;
                    var row = new Button
                    {
                        Classes = { "subtle" },
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Content = $"{entity.Name}   {speed:0}°/s"
                    };
                    row.Click += (_, _) => selection.SelectEntity(id);
                    _rows.Children.Add(row);
                }

                if (_rows.Children.Count == 0)
                    _rows.Children.Add(new TextBlock { Text = "No spinners in this scene.", Classes = { "muted" } });
            }
        }
        """;

    private const string SpinnerCommandsSource = """
        using System.Text.Json.Nodes;
        using Talesmith.Editor.Commands;
        using Talesmith.Editor.Documents;
        using Talesmith.Editor.Selection;
        using Talesmith.Editor.Undo;
        using Talesmith.Runtime.Serialization;
        using Talesmith.UI;

        namespace Spinners.Editor;

        public sealed class SpinnerCommands(ISceneDocumentService documents, ISelectionService selection, IUndoService undo) : IEditorCommandContributor
        {
            public static string ComponentType { get; } = ComponentRegistry.GetTypeName(typeof(Spinner));

            public void Contribute(CommandBuilder builder)
            {
                builder.Add("spinners.add", "Add spinner", "Spinners", AddToSelection, CanAdd, "Ctrl+Alt+R", Icons.RotateCw,
                    "Adds a Spinner component to the selected entities.");
                builder.Menu(MenuPaths.Plugins, "spinners.add");
                builder.Menu(MenuPaths.Component, "spinners.add", "plugins");
                builder.Toolbar("spinners.add");
                builder.KeepOutOfText("spinners.add");
            }

            private bool CanAdd() => documents.Active is { } document && selection.Entities.Any(id => document.Find(id) is { } entity && entity.FindComponent(ComponentType) is null);

            private void AddToSelection()
            {
                if (documents.Active is not { } document)
                    return;
                using var transaction = undo.BeginTransaction("Add spinner");
                foreach (var id in selection.Entities)
                {
                    if (document.Find(id) is { } entity && entity.FindComponent(ComponentType) is null)
                        document.AddComponent(id, new ComponentDocument(ComponentType, new JsonObject { ["speed"] = 90 }));
                }
            }
        }
        """;

    private const string SpinnerToolSource = """
        using System.Text.Json.Nodes;
        using Avalonia;
        using Avalonia.Controls;
        using Avalonia.Input;
        using Avalonia.Media;
        using Avalonia.Media.Immutable;
        using Talesmith.Editor.Selection;
        using Talesmith.Editor.Viewport.Tools;
        using Talesmith.Runtime.Serialization;
        using Talesmith.UI;
        using Talesmith.UI.Controls;

        namespace Spinners.Editor;

        public sealed class SpinnerTool : IViewportTool
        {
            private static readonly Cursor Crosshair = new(StandardCursorType.Cross);
            private static readonly ImmutablePen PreviewPen = new(new ImmutableSolidColorBrush(Colors.Orange), 1.5, new ImmutableDashStyle([4, 3], 0));
            private readonly NumberField _speed = new() { Label = "Speed", Suffix = "°/s", Minimum = -720, Maximum = 720, Value = 90, Width = 130 };
            private Point? _pointer;

            public string Id => "spinners.place";

            public string Name => "Place spinner";

            public string Description => "Click to place an entity that spins. Set its speed in the tool options.";

            public Geometry Icon => Icons.RotateCw;

            public string? Shortcut => "J";

            public string Group => "Spinners";

            public int Order => 0;

            public Cursor? Cursor => Crosshair;

            public bool IsAvailable(ViewportToolContext context) => context.Document is not null;

            public void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e)
            {
                if (!e.IsLeftButton || context.Document is not { } document)
                    return;
                var entity = document.CreateEntity("Spinner", components:
                [
                    new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(MathF.Round(e.World.X), MathF.Round(e.World.Y)) }),
                    new ComponentDocument(SpinnerCommands.ComponentType, new JsonObject { ["speed"] = _speed.Value })
                ]);
                context.Selection.SelectEntity(entity.Id);
                e.Handled = true;
            }

            public void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e)
            {
                _pointer = e.Position;
                context.Invalidate();
            }

            public void PointerExited(ViewportToolContext context)
            {
                _pointer = null;
                context.Invalidate();
            }

            public void Render(ViewportToolContext context, DrawingContext drawing)
            {
                if (_pointer is { } at)
                    drawing.DrawEllipse(null, PreviewPen, at, 16, 16);
            }

            public Control? CreateOptionsView() => _speed;
        }
        """;

    private const string SpinnerGizmoSource = """
        using System.Numerics;
        using System.Text.Json.Nodes;
        using Talesmith.Editor.Viewport.Gizmos;
        using Talesmith.Runtime.Components;

        namespace Spinners.Editor;

        public sealed class SpinnerGizmo : IGizmoProvider
        {
            private const float UnitsPerDegree = 0.25f;

            public void Draw(GizmoContext context)
            {
                foreach (var archetype in context.World.Query<Transform, Spinner>())
                {
                    var transforms = archetype.GetSpan<Transform>();
                    var spinners = archetype.GetSpan<Spinner>();
                    for (var i = 0; i < archetype.Count; i++)
                    {
                        var entity = archetype.Entities[i];
                        var selected = context.IsSelected(entity);
                        if (!selected && !context.IsHovered(entity))
                            continue;
                        var speed = spinners[i].Speed;
                        var radius = MathF.Max(MathF.Abs(speed) * UnitsPerDegree, 12 * context.PixelSize);
                        context.Arc(transforms[i].Position, radius, transforms[i].Rotation, MathF.Sign(speed) * MathF.PI * 1.5f, context.Palette.Accent,
                            thickness: 2, opacity: selected ? 1 : GizmoContext.Faint);
                    }
                }
            }

            public void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
            {
                foreach (var archetype in context.World.Query<Transform, Spinner>())
                {
                    var transforms = archetype.GetSpan<Transform>();
                    var spinners = archetype.GetSpan<Spinner>();
                    for (var i = 0; i < archetype.Count; i++)
                    {
                        var entity = archetype.Entities[i];
                        var id = GizmoMath.DocumentId(context.World, entity);
                        if (!context.IsSelected(entity) || id == Guid.Empty)
                            continue;
                        var center = transforms[i].Position;
                        handles.Add(new GizmoHandle(id, SpinnerCommands.ComponentType, "speed", center + new Vector2(spinners[i].Speed * UnitsPerDegree, 0),
                            drag => JsonValue.Create(Math.Clamp(GizmoMath.Snap((drag.World.X - center.X) / UnitsPerDegree, 15, drag.Snap), -720, 720)))
                        {
                            Shape = GizmoHandleShape.Diamond,
                            Hint = "Drag left or right to change the speed"
                        });
                    }
                }
            }
        }
        """;
}

/// <summary>Helpers shared by the plugin scenes.</summary>
internal static class PluginDocsScene
{
    /// <summary>The dock group that shows a panel, to crop to.</summary>
    public static Control Group(Window window, string panelId) =>
        window.GetVisualDescendants().OfType<DockGroupView>().First(v => v.Group.Panels.Contains(panelId));

    public static PluginsPanel ShowPlugins(OpenEditor editor)
    {
        editor.Get<LayoutService>().ShowPanel(PanelIds.Plugins);
        RenderLoop.Settle(300);
        return editor.Get<PluginsPanel>();
    }

    public static void Expand(PluginsPanel panel, string id)
    {
        if (panel.Plugins.FirstOrDefault(p => p.Id == id) is { } card)
            card.IsExpanded = true;
        RenderLoop.Settle(300);
    }

    /// <summary>Gives entities of the open scene a Spinner, so the editor part has something to show.</summary>
    public static Guid AddSpinners(OpenEditor editor, int firstSpeed = 240)
    {
        var document = editor.Get<ISceneDocumentService>().Active!;
        var type = "Spinners.Spinner";
        Guid selected = default;
        foreach (var (name, speed) in new[] { ("Coin 9", firstSpeed), ("Coin 22", -180), ("Coin 38", 360) })
        {
            if (document.Entities.FirstOrDefault(e => e.Name == name) is not { } coin)
                continue;
            document.AddComponent(coin.Id, new ComponentDocument(type, new JsonObject { ["speed"] = speed }));
            if (selected == default)
                selected = coin.Id;
        }

        RenderLoop.Settle(300);
        return selected;
    }
}

/// <summary>The Plugins panel of Hex Quest, with the cutscene plugin's card expanded.</summary>
internal sealed class PluginsPanelDocsScene : EditorWindowScene
{
    public override string Name => "plugins-panel";

    protected override string Project => PluginDocsProjects.HexQuest;

    protected override void Customize(Window window)
    {
        var camera = Editor.Get<ViewportCamera>();
        camera.Set(camera.Position, 0.5f);
        PluginDocsScene.Expand(PluginDocsScene.ShowPlugins(Editor), "samples.cutscenes");
    }
}

/// <summary>The New plugin dialog with a name typed in.</summary>
internal sealed class NewPluginDialogScene : EditorWindowScene
{
    private TextInputDialogViewModel? _dialog;

    public override string Name => "new-plugin-dialog";

    protected override string Project => PluginDocsProjects.Fresh;

    protected override void Customize(Window window)
    {
        PluginDocsScene.ShowPlugins(Editor);
        _dialog = new TextInputDialogViewModel(Editor.Get<IDialogService>(), "New plugin",
            "Creates a plugin project with plugin.json, a project file and an IPlugin class. Building it installs the plugin into this game.",
            "Plugin name", "Spinners", "Create");
        _ = Editor.Get<IDialogService>().ShowAsync(_dialog);
        RenderLoop.Settle(500);
    }

    public override Control? Region(Window window) =>
        window.GetVisualDescendants().OfType<ContentPresenter>().LastOrDefault(p => p.Name == "PART_ContentPresenter" && p.Content == _dialog);
}

/// <summary>A newly built plugin after a rescan: installed, not loaded, and the panel asks to reload.</summary>
internal sealed class RestartRequiredScene : EditorWindowScene
{
    public override string Name => "restart-required";

    public override Size Size => new(1440, 560);

    protected override string Project => PluginDocsProjects.Fresh;

    public override Control Build()
    {
        PluginDocsProjects.RemoveSpinners(Project);
        return base.Build();
    }

    protected override void Customize(Window window)
    {
        var panel = PluginDocsScene.ShowPlugins(Editor);
        PluginDocsProjects.InstallSpinners(Project, editorPart: false);
        panel.RescanCommand.Execute(null);
        RenderLoop.Settle(400);
    }

    public override Control? Region(Window window) => PluginDocsScene.Group(window, PanelIds.Plugins);
}

/// <summary>The inspector of a coin with the tutorial's Spinner component.</summary>
internal sealed class SpinnerInspectorScene : EditorWindowScene
{
    public override string Name => "spinner-inspector";

    public override Size Size => new(1440, 600);

    protected override string Project => PluginDocsProjects.Spinners;

    protected override void Customize(Window window)
    {
        var coin = PluginDocsScene.AddSpinners(Editor, firstSpeed: 90);
        Editor.Get<ISelectionService>().SelectEntity(coin);
        Editor.Get<LayoutService>().ShowPanel(PanelIds.Inspector);
        RenderLoop.Settle(500);
        foreach (var group in window.GetVisualDescendants().OfType<PropertyGroup>())
            group.IsExpanded = group.Header is "Spinner";
        RenderLoop.Settle(400);
    }

    public override Control? Region(Window window) => PluginDocsScene.Group(window, PanelIds.Inspector);
}

/// <summary>Plugins that failed, were skipped, are switched off, use a permission they did not declare, or whose editor part failed.</summary>
internal sealed class PluginProblemsScene : EditorWindowScene
{
    public override string Name => "plugin-problems";

    public override Size Size => new(1440, 1280);

    protected override string Project => PluginDocsProjects.Problems;

    protected override void Customize(Window window)
    {
        PluginDocsScene.ShowPlugins(Editor);
        RenderLoop.Settle(300);
    }

    public override Control? Region(Window window) => PluginDocsScene.Group(window, PanelIds.Plugins);
}

/// <summary>The Spinners card expanded: its permissions, extension points and saved settings.</summary>
internal sealed class PluginDetailsScene : EditorWindowScene
{
    public override string Name => "plugin-details";

    public override Size Size => new(1440, 640);

    protected override string Project => PluginDocsProjects.Spinners;

    protected override void Customize(Window window) => PluginDocsScene.Expand(PluginDocsScene.ShowPlugins(Editor), PluginDocsProjects.SpinnersId);

    public override Control? Region(Window window) => PluginDocsScene.Group(window, PanelIds.Plugins);
}

/// <summary>The Spinners editor part at work: its panel, its tool active with options, the gizmo of a selected spinner and its app bar button.</summary>
internal sealed class EditorPluginScene : EditorWindowScene
{
    public override string Name => "editor-plugin";

    protected override string Project => PluginDocsProjects.Spinners;

    protected override void Customize(Window window)
    {
        var coin = PluginDocsScene.AddSpinners(Editor);
        Editor.Get<ISelectionService>().SelectEntity(coin);
        Editor.Get<ToolManager>().Select("spinners.place");
        Editor.Get<LayoutService>().ShowPanel("spinners");
        Editor.Get<ViewportCamera>().Set(new System.Numerics.Vector2(500, 40), 1.4f);
        RenderLoop.Settle(600);
    }
}

/// <summary>Hex Quest in play mode with the cutscene plugin's dialogue box asking the harbor question.</summary>
internal sealed class CutsceneDialogueScene : EditorWindowScene
{
    public override string Name => "cutscene-dialogue";

    protected override string Project => PluginDocsProjects.HexQuest;

    protected override void Customize(Window window)
    {
        var play = Editor.Get<IPlayModeService>();
        _ = play.PlayAsync();
        PlayWait.UntilSceneLoaded(play);
        RenderLoop.Settle(800);
        var assembly = Editor.Get<PluginManager>().Assemblies.First(a => a.Plugin.Id == "samples.cutscenes").Assembly;
        var state = assembly.GetType("Talesmith.Samples.Cutscenes.DialogueState")!;
        var choose = state.GetMethod("ChooseAsync", BindingFlags.Public | BindingFlags.Instance)!;
        var asked = play.TryInvokeAsync(game =>
        {
            choose.Invoke(game.Services.GetService(state), ["Harbormaster Wren", "Until then, Lady Elara up at Highgarden is looking for help. Will you go?",
                new List<string> { "Of course. Which way?", "I'd rather rest by the water." }]);
            return true;
        }, false);
        RenderLoop.Wait(() => asked.IsCompleted, 10000);
        RenderLoop.Settle(3500);
    }

    public override Control? Region(Window window) => PluginDocsScene.Group(window, PanelIds.Game);
}
