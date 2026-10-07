using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Ecs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Hub;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Lighting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.Physics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;
using Talesmith.Screenshots.Capture;
using Talesmith.Scripting;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The Coin Run project of the guide's first-game tutorial, made from the platformer template, with the tutorial's scripts.</summary>
internal static class TutorialProject
{
    public const string HeroController = """
        namespace CoinRun;

        /// <summary>Runs with the Run action and jumps with Jump.</summary>
        public sealed class HeroController : Script
        {
            [Range(0, 600)]
            public float RunSpeed = 220;

            [Range(0, 1500)]
            public float JumpSpeed = 680;

            [Range(0, 4000)]
            public float Gravity = 1600;

            private Vector2 _velocity;
            private Vector2 _start;
            private bool _jumpPressed;

            protected override void OnStart() => _start = Position;

            protected override void Update()
            {
                if (Input.WasPressed("Jump"))
                    _jumpPressed = true;
            }

            protected override void FixedUpdate()
            {
                var dt = Time.DeltaTime;
                var grounded = GetComponent<CharacterController2D>().IsGrounded;

                _velocity.X = Input.Axis("Run") * RunSpeed;
                _velocity.Y += Gravity * dt;
                if (_jumpPressed && grounded)
                    _velocity.Y = -JumpSpeed;
                _jumpPressed = false;

                var hits = Physics.MoveCharacter(Entity, _velocity * dt);
                if ((hits & (CharacterCollisions.Below | CharacterCollisions.Above)) != 0)
                    _velocity.Y = 0;

                if (_velocity.X != 0)
                    GetComponent<Sprite>().FlipX = _velocity.X < 0;

                if (Position.Y > 600)
                {
                    Position = _start;
                    _velocity = Vector2.Zero;
                }
            }
        }
        """;

    public const string Coin = """
        namespace CoinRun;

        /// <summary>Disappears when the hero touches it.</summary>
        public sealed class Coin : Script
        {
            public int Value = 1;

            protected override void OnTriggerEnter(in ContactInfo contact)
            {
                if (GetScript<HeroController>(contact.Other) is null)
                    return;
                Log.Info($"Picked up a coin worth {Value}");
                Destroy();
            }
        }
        """;

    private static int _count;

    /// <summary>Creates a fresh copy of the project, so one scene's edits never show in another.</summary>
    public static string Create(bool withScripts)
    {
        var parent = Path.Combine(EditorFixture.Scratch, "tutorial-" + (++_count).ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(parent);
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        var folder = Task.Run(() => creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), parent, "Coin Run")).GetAwaiter().GetResult();
        if (withScripts)
        {
            var scripts = Path.Combine(folder, "assets", "scripts");
            File.WriteAllText(Path.Combine(scripts, "HeroController.cs"), HeroController);
            File.WriteAllText(Path.Combine(scripts, "Coin.cs"), Coin);
        }

        return folder;
    }
}

/// <summary>The editor on a project, without the extra selection and console message of <see cref="EditorWindowScene"/>.</summary>
internal abstract class GuideScene(string name) : EditorWindowScene
{
    private static readonly Pointer Mouse = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);

    public override string Name => name;

    public T Get<T>()
        where T : notnull => Editor.Get<T>();

    public SceneDocumentModel Model => Get<ISceneDocumentService>().Active!;

    public override void Prepare(Window window)
    {
        Editor.Attach(window);
        var project = Get<IProjectService>();
        RenderLoop.Wait(() => project.ScanProgress is null, 30000);
        RenderLoop.Settle(300);
        Customize(window);
        RenderLoop.Settle(400);
    }

    /// <summary>Whether to crop to the dialog shown last.</summary>
    public bool CropToDialog { get; init; }

    /// <summary>The logical window size, when not the default.</summary>
    public Size? WindowSize { get; init; }

    public override Size Size => WindowSize ?? base.Size;

    /// <summary>The type of a panel's view to crop to, with its tabs.</summary>
    public Type? CropToPanel { get; init; }

    public override Control? Region(Window window)
    {
        if (CropToDialog)
        {
            return window.GetVisualDescendants().OfType<Talesmith.UI.Controls.DialogLayer>().LastOrDefault()?.GetVisualDescendants()
                .OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => p.Name == "PART_ContentPresenter");
        }

        if (CropToPanel is { } type && window.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => type.IsInstanceOfType(c) && c.IsEffectivelyVisible) is { } view)
            return view.FindAncestorOfType<Talesmith.UI.Docking.DockGroupView>() ?? view;
        return null;
    }

    /// <summary>Shows a dialog view model the way the editor does.</summary>
    public void ShowDialog(object viewModel)
    {
        _ = Get<IDialogService>().ShowAsync(viewModel);
        RenderLoop.Settle(400);
    }

    /// <summary>The editor, for the helpers of other scenes that take one.</summary>
    public OpenEditor Open => Editor;

    public Guid Entity(string entityName) => Model.Entities.First(e => e.Name == entityName).Id;

    public void Select(params Guid[] ids)
    {
        Get<ISelectionService>().SelectEntities(ids);
        RenderLoop.Settle(200);
    }

    public void Layout(string preset)
    {
        Editor.Shell.SelectedPreset = preset;
        RenderLoop.Settle(200);
    }

    public void ShowPanel(string panel)
    {
        Get<LayoutService>().ShowPanel(panel);
        RenderLoop.Settle(150);
    }

    public void Frame(Vector2 center, float zoom)
    {
        Get<ViewportCamera>().Set(center, zoom);
        RenderLoop.Settle(150);
    }

    public ComponentRegistry Components => Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ComponentRegistry>();

    public void AddComponent<T>(Guid entity, Action<JsonObject>? configure = null)
    {
        var definition = Components.Find(typeof(T))!;
        var data = definition.CreateDefault();
        configure?.Invoke(data);
        Model.AddComponent(entity, new ComponentDocument(definition.TypeName, data));
    }

    /// <summary>Adds a component or script the way the Add component popup does, by its title.</summary>
    public void AddFromPopup(string title, params Guid[] targets)
    {
        var services = Get<IProjectService>().EditSession!.Game.Services;
        var menu = new AddComponentMenu(Get<ISceneDocumentService>(), Get<Talesmith.Editor.Undo.IUndoService>(), Get<ProjectState>(),
            () => services.GetService<ComponentRegistry>(), () => services.GetService<ScriptTypeRegistry>());
        var items = menu.Items(title, targets).ToList();
        var item = items.FirstOrDefault(i => !i.IsHeading && i.Title == title)
                   ?? throw new InvalidOperationException($"No '{title}' in the Add component popup: {string.Join(", ", items.Select(i => i.Title))}. " +
                                                          $"Scripts: {Get<IScriptService>().LastResult}");
        menu.Add(item, targets);
        RenderLoop.Settle(200);
    }

    public void WaitForScripts()
    {
        var scripts = Get<IScriptService>();
        RenderLoop.Wait(() => scripts.LastResult is not null && !scripts.IsCompiling, 120000);
        RenderLoop.Settle(300);
    }

    public static void ExpandInspector(Window window, params string[] collapsed)
    {
        foreach (var group in window.GetVisualDescendants().OfType<Talesmith.UI.Controls.PropertyGroup>())
            group.IsExpanded = !collapsed.Contains(group.Header as string);
        RenderLoop.Settle(200);
    }

    public TileMapEditor Map => Get<TileMapEditor>();

    public ToolManager Tools => Get<ToolManager>();

    public void EditMap(string entityName)
    {
        Select(Entity(entityName));
        RenderLoop.Wait(() => Map.Map is not null, 30000);
        RenderLoop.Settle(200);
    }

    public void SetRole(string layerName, LayerRole role)
    {
        var layer = Map.Map!.Layers.First(l => l.Name == layerName);
        Map.Execute($"Make \"{layer.Name}\" a {role.ToString().ToLowerInvariant()} layer", MapEdits.ChangeLayer(layer, LayerSettings.Of(layer) with { Role = role }));
        RenderLoop.Settle(150);
    }

    public void PaintCells(IEnumerable<GridCoord> cells, int tile)
    {
        var map = Map.Map!;
        var layer = map.TileLayers[0];
        var tileset = map.Tilesets[0];
        var edit = Map.BeginEdit();
        var set = new CellSet();
        foreach (var cell in cells)
            set.Add(cell);
        edit.Fill(layer, set, new TileCell(tileset.Id, tile));
        Map.Commit(edit, "Paint tiles");
        RenderLoop.Settle(150);
    }

    public void Hover(GridCoord cell) => Tools.ActiveTool.PointerMoved(Get<ViewportToolContext>(),
        Args(cell, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other)));

    public void Press(GridCoord cell) => Tools.ActiveTool.PointerPressed(Get<ViewportToolContext>(),
        Args(cell, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), 1));

    public void Drag(GridCoord cell) => Tools.ActiveTool.PointerMoved(Get<ViewportToolContext>(),
        Args(cell, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other)));

    public void Release(GridCoord cell) => Tools.ActiveTool.PointerReleased(Get<ViewportToolContext>(),
        Args(cell, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased)));

    private ViewportPointerEventArgs Args(GridCoord cell, PointerPointProperties properties, int clicks = 0)
    {
        var context = Get<ViewportToolContext>();
        var world = Map.CellCenter(cell);
        var screen = context.ToScreen(world);
        var source = new PointerEventArgs(InputElement.PointerMovedEvent, null, Mouse, null, screen, 0, properties, KeyModifiers.None);
        return new ViewportPointerEventArgs(source, screen, world, properties, clicks);
    }
}

/// <summary>The steps of the first-game tutorial, applied the way the reader does them.</summary>
internal enum TutorialStep
{
    Start,
    Collision,
    Paint,
    Physics,
    Script,
    Coins,
    Campfire
}

/// <summary>Coin Run after the tutorial's steps up to <see cref="Step"/>, then whatever the screenshot shows.</summary>
internal sealed class TutorialScene(string name, TutorialStep step, Action<TutorialScene, Window>? show = null) : GuideScene(name)
{
    private string? _project;

    public TutorialStep Step { get; } = step;

    protected override string Project => _project ??= TutorialProject.Create(Step >= TutorialStep.Script);

    public Guid Hero => Entity("Hero");

    public IReadOnlyList<Guid> Coins => [.. Model.Entities.Where(e => e.Name.StartsWith("Coin ", StringComparison.Ordinal)).Select(e => e.Id)];

    protected override void Customize(Window window)
    {
        Apply();
        Get<Talesmith.Editor.Undo.IUndoService>().MarkSaved(Model);
        show?.Invoke(this, window);
    }

    private void Apply()
    {
        if (Step >= TutorialStep.Collision)
        {
            EditMap("Level");
            SetRole("Ground", LayerRole.Collision);
        }

        if (Step >= TutorialStep.Paint)
        {
            PaintCells([new GridCoord(17, 6), new GridCoord(18, 6)], 0);
            PaintCells(Enumerable.Range(7, 4).SelectMany(y => new[] { new GridCoord(17, y), new GridCoord(18, y) }), 1);
        }

        if (Step >= TutorialStep.Physics)
        {
            AddComponent<Collider2D>(Hero, c =>
            {
                c["size"] = new JsonArray(20, 28);
                c["offset"] = new JsonArray(0, -14);
            });
            AddComponent<CharacterController2D>(Hero);
        }

        if (Step >= TutorialStep.Script)
        {
            WaitForScripts();
            AddFromPopup("Hero Controller", Hero);
        }

        if (Step >= TutorialStep.Coins)
        {
            var coins = Coins.ToArray();
            AddFromPopup("Collider 2D", coins);
            using (Get<Talesmith.Editor.Undo.IUndoService>().BeginTransaction($"Change Is Trigger of {coins.Length} entities"))
            {
                foreach (var coin in coins)
                    Model.SetProperty(coin, Components.Find(typeof(Collider2D))!.TypeName, "isTrigger", true);
            }
            AddFromPopup("Coin", coins);
        }

        if (Step >= TutorialStep.Campfire)
        {
            var hierarchy = Get<HierarchyViewModel>();
            var campfire = hierarchy.Create("particles.fire", null, name: "Campfire")!.Value;
            Model.SetProperty(campfire, "Transform", "position", new JsonArray(64, 176)); var glow = hierarchy.Create("light.point", campfire, name: "Glow")!.Value;
            var light = Model.Get(glow).Components[1].Type;
            Model.SetProperty(glow, light, "color", "#FFB45C");
            Model.SetProperty(glow, light, "radius", 220);
            Model.SetProperty(glow, light, "intensity", 1.4);
            Model.SetProperty(glow, light, "animation", "flicker");
            var dusk = LightingPreset.BuiltIn.First(p => p.Name == "Dusk");
            Model.SetEnvironment(dusk.ApplyTo(Model.Document.Environment), "Apply Dusk lighting");
            hierarchy.Tree.SetExpandedRecursive(campfire, true);
        }

        Get<ISelectionService>().Clear();
        RenderLoop.Settle(300);
    }

}

/// <summary>The hub's New project page with the platformer template chosen and the tutorial's name typed.</summary>
internal sealed class TutorialHubScene : HubScene
{
    public override string Name => "first-game-new-project";

    protected override void Configure(HubViewModel hub)
    {
        hub.Page = 1;
        hub.SelectTemplateCommand.Execute(hub.Templates.Single(t => t.Template.Id == "platformer"));
        hub.ProjectName = "Coin Run";
        hub.Location = Path.Combine(Path.GetTempPath(), "Talesmith Projects");
    }
}

/// <summary>The tutorial's scenes, in the order of the tutorial.</summary>
internal static class TutorialShots
{
    public static TutorialScene Starter() => new("first-game-starter", TutorialStep.Start, (s, w) =>
    {
        s.Get<HierarchyViewModel>().Tree.SetExpandedRecursive(s.Model.Entities.First(e => e.Name == "Coins").Id, true);
        s.Select(s.Hero);
        s.Frame(new Vector2(560, 120), 1.1f);
    });

    public static TutorialScene Collision() => new("first-game-collision", TutorialStep.Collision, (s, w) =>
    {
        s.Layout(LayoutService.TileMappingPreset);
        s.EditMap("Level");
        s.Frame(new Vector2(560, 150), 1.1f);
    });

    public static TutorialScene Paint() => new("first-game-paint", TutorialStep.Collision, (s, w) =>
    {
        s.Layout(LayoutService.TileMappingPreset);
        s.EditMap("Level");
        var map = s.Map.Map!;
        s.PaintCells([new GridCoord(17, 6), new GridCoord(18, 6)], 0);
        s.Map.Brush.Pick(new TileCell(map.Tilesets[0].Id, 1));
        s.Tools.Select("tile.rectangle");
        s.Frame(new Vector2(560, 250), 1.9f);
        s.Press(new GridCoord(17, 7));
        s.Drag(new GridCoord(18, 9));
        s.Drag(new GridCoord(18, 10));
    });

    public static TutorialScene Physics() => new("first-game-physics", TutorialStep.Physics, (s, w) =>
    {
        s.Select(s.Hero);
        s.Frame(new Vector2(128, 150), 5f);
        GuideScene.ExpandInspector(w, "Transform", "Sprite", "Sprite Animator");
    });

    public static TutorialScene Script() => new("first-game-script", TutorialStep.Script, (s, w) =>
    {
        s.Select(s.Hero);
        s.Frame(new Vector2(128, 150), 4f);
        GuideScene.ExpandInspector(w, "Transform", "Sprite", "Sprite Animator", "Collider 2D", "Character Controller 2D");
    });

    public static TutorialScene ScriptDialog() => new("first-game-script-dialog", TutorialStep.Physics, (s, w) =>
        s.ShowDialog(new CreateScriptDialogViewModel(s.Get<IDialogService>(), s.Get<IScriptService>().Options.SourceRoot) { ClassName = "HeroController" }))
    {
        CropToDialog = true
    };

    public static TutorialScene Build() => new("first-game-build", TutorialStep.Campfire, (s, w) =>
    {
        var dialog = new Talesmith.Editor.Build.BuildDialogViewModel(s.Get<Talesmith.Editor.Build.IBuildService>(), s.Get<IProjectService>(),
            s.Get<ISceneDocumentService>(), s.Get<IDialogService>(), s.Get<IFileDialogService>(), s.Get<ICodeEditor>());
        dialog.SelectTargetCommand.Execute(dialog.Targets.Single(t => t.IsThisComputer));
        dialog.ProfileIndex = (int)Talesmith.Build.BuildProfileKind.Release;
        s.ShowDialog(dialog);
    })
    {
        CropToDialog = true
    };

    public static TutorialScene Coins() => new("first-game-coins", TutorialStep.Coins, (s, w) =>
    {
        s.Select([.. s.Coins]);
        s.Frame(new Vector2(560, 60), 1.6f);
        GuideScene.ExpandInspector(w, "Transform", "Sprite", "Sprite Animator");
    });

    public static TutorialScene Campfire() => new("first-game-campfire", TutorialStep.Campfire, (s, w) =>
    {
        s.Select(s.Model.Entities.First(e => e.Name == "Glow").Id);
        GuideScene.ExpandInspector(w, "Transform");
        s.Get<ViewportOptions>().IsPreviewing = true;
        s.Frame(new Vector2(120, 120), 2.6f);
        RenderLoop.Settle(1500);
    });

    public static TutorialScene Play() => new("first-game-play", TutorialStep.Campfire, (s, w) =>
    {
        var save = s.Get<TileMapDocuments>().SaveAllAsync();
        RenderLoop.Wait(() => save.IsCompleted, 30000);
        RenderLoop.Settle(1000);
        var play = s.Get<IPlayModeService>();
        _ = play.PlayAsync();
        PlayWait.UntilSceneLoaded(play);
        RenderLoop.Settle(2000);
        Report(play, "after 2 s");
        TouchCoin(play);
        RenderLoop.Settle(800);
        Report(play, "after touching a coin");
    });

    private static void Report(IPlayModeService play, string when)
    {
        var probe = play.TryInvokeAsync(game =>
        {
            var world = game.Scenes.Current!.World;
            var hero = "";
            var coins = 0;
            world.Query<Name, Transform>().ForEach((Entity _, ref Name name, ref Transform transform) =>
            {
                if (name.Value == "Hero")
                    hero = transform.Position.ToString();
                else if (name.Value.StartsWith("Coin ", StringComparison.Ordinal))
                    coins++;
            });
            return $"hero at {hero}, {coins} coins";
        }, "no game");
        RenderLoop.Wait(() => probe.IsCompleted, 10000);
        Console.WriteLine($"  Tutorial check {when}: {probe.Result}");
    }

    private static void TouchCoin(IPlayModeService play)
    {
        var move = play.TryInvokeAsync(game =>
        {
            var world = game.Scenes.Current!.World;
            var target = Vector2.Zero;
            world.Query<Name, Transform>().ForEach((Entity _, ref Name name, ref Transform transform) =>
            {
                if (name.Value == "Coin 9")
                    target = transform.Position;
            });
            world.Query<Name, Transform>().ForEach((Entity _, ref Name name, ref Transform transform) =>
            {
                if (name.Value == "Hero")
                    transform.Position = target + new Vector2(0, 12);
            });
            return true;
        }, false);
        RenderLoop.Wait(() => move.IsCompleted, 10000);
    }
}

/// <summary>Copies of samples and templates for the guide, made once per run in the fixture's temporary folder.</summary>
internal static class GuideProjects
{
    private static readonly Lazy<string> LanternGroveCopy = new(() => Copy("LanternGrove"));

    /// <summary>A copy of the Lantern Grove sample.</summary>
    public static string LanternGrove => LanternGroveCopy.Value;

    /// <summary>A fresh hex adventure project, for scenes that change it.</summary>
    public static string HexAdventure()
    {
        var parent = Directory.CreateDirectory(Path.Combine(EditorFixture.Scratch, "hex-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        return Task.Run(() => creator.CreateAsync(creator.Templates.Single(t => t.Id == "hex-adventure"), parent, "Ember Isles")).GetAwaiter().GetResult();
    }

    private static string Copy(string sample)
    {
        var target = Path.Combine(EditorFixture.Scratch, sample);
        CopyFolder(Path.Combine(Repository.Root, "samples", sample), target);
        return target;
    }

    /// <summary>Copies a project, leaving out the editor state of whoever opened it last.</summary>
    public static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (Path.GetFileName(directory) != ".talesmith")
                CopyFolder(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}

/// <summary>The editor on any project, then whatever the screenshot shows.</summary>
internal sealed class ProjectScene(string name, Func<string> project, Action<ProjectScene, Window> show) : GuideScene(name)
{
    private string? _project;

    protected override string Project => _project ??= project();

    protected override void Customize(Window window) => show(this, window);

    /// <summary>Starts editing the map of the named entity, or of the scene's last map.</summary>
    public void EditSceneMap(string? entityName = null)
    {
        var entity = Model.Entities.Last(e => e.FindComponent("TileMapRenderer") is not null && (entityName is null || e.Name == entityName));
        Select(entity.Id);
        RenderLoop.Wait(() => Map.Map is not null, 30000);
        RenderLoop.Settle(300);
    }

    public void FrameSelection(float zoom)
    {
        Get<ViewportService>().FrameSelection(animate: false);
        RenderLoop.Settle(150);
        Get<ViewportCamera>().ZoomTo(zoom, animate: false);
        RenderLoop.Settle(150);
    }

    public GridCoord CenterCell() => Map.CellAt(Get<ViewportCamera>().Position);

    public GridCoord Offset(GridCoord cell, int columns, int rows)
    {
        var topology = Map.Map!.Layout.Topology;
        return topology.FromOffset(topology.ToOffset(cell) + new GridCoord(columns, rows));
    }
}

/// <summary>Scenes of the guide's play mode, workflow and settings pages.</summary>
internal static class WorkflowShots
{
    public static TutorialScene Paused() => new("play-paused", TutorialStep.Campfire, (s, w) =>
    {
        var save = s.Get<TileMapDocuments>().SaveAllAsync();
        RenderLoop.Wait(() => save.IsCompleted, 30000);
        var play = s.Get<IPlayModeService>();
        _ = play.PlayAsync();
        PlayWait.UntilSceneLoaded(play);
        RenderLoop.Settle(1500);
        var hierarchy = s.Get<HierarchyViewModel>();
        RenderLoop.Wait(() => hierarchy.Rows.Count > 0, 10000);
        if (hierarchy.Rows.FirstOrDefault(r => r.Name == "Hero") is { } hero)
            s.Get<ISelectionService>().SelectEntity(hero.Id);
        play.Pause();
        RenderLoop.Settle(1200);
        GuideScene.ExpandInspector(w, "Transform", "Sprite", "Sprite Animator");
    });

    public static ProjectScene Blocked() => new("play-blocked", () => EditorFixture.BrokenScripts, (s, w) =>
    {
        s.WaitForScripts();
        _ = s.Get<IPlayModeService>().PlayAsync();
        RenderLoop.Settle(800);
    });

    public static TutorialScene History() => new("history-panel", TutorialStep.Coins, (s, w) =>
    {
        s.ShowPanel(PanelIds.History);
        RenderLoop.Settle(300);
    })
    {
        CropToPanel = typeof(Talesmith.Editor.Undo.HistoryView),
        WindowSize = new Size(1320, 760)
    };

    public static TutorialScene EffectsLayout() => new("layout-effects", TutorialStep.Campfire, (s, w) =>
    {
        s.Layout(LayoutService.EffectsPreset);
        s.Select(s.Entity("Campfire"));
        GuideScene.ExpandInspector(w, "Transform");
        s.Get<ViewportOptions>().IsPreviewing = true;
        s.Frame(new Vector2(100, 120), 2f);
        RenderLoop.Settle(1500);
    });

    public static ProjectScene ProjectGame() => new("project-game", () => EditorFixture.IsleHopper, (s, w) =>
    {
        EditorInputSettingsScene.Open(s.Open, 0);
        RenderLoop.Settle(400);
        ScrollToHeading(w, "View");
        RenderLoop.Settle(300);
    })
    {
        CropToDialog = true
    };

    public static ProjectScene ProjectLoadingScreen() => new("project-loading-screen", () => EditorFixture.HexQuest, (s, w) =>
    {
        EditorInputSettingsScene.Open(s.Open, 0);
        RenderLoop.Settle(400);
        ScrollToHeading(w, "Loading screen");
        RenderLoop.Settle(600);
    })
    {
        CropToDialog = true
    };

    /// <summary>Scrolls the scroll viewer holding a section heading so the heading is at its top.</summary>
    private static void ScrollToHeading(Window window, string heading)
    {
        var text = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == heading && t.IsEffectivelyVisible);
        var viewer = text.FindAncestorOfType<ScrollViewer>()!;
        var top = text.TranslatePoint(default, (Visual)viewer.Content!)!.Value.Y;
        viewer.Offset = new global::Avalonia.Vector(0, Math.Max(0, top - 8));
    }

    public static ProjectScene SettingsAppearance() => new("settings-appearance", () => EditorFixture.Platformer, (s, w) =>
    {
        s.ShowDialog(new Talesmith.Editor.Dialogs.SettingsDialogViewModel(s.Get<IDialogService>(), s.Get<Talesmith.Editor.Settings.ISettingsService>(),
            s.Get<Talesmith.UI.Theming.IThemeManager>(), s.Open.Shell.Commands)
        { SelectedSectionIndex = 0 });
    })
    {
        CropToDialog = true
    };

    public static ProjectScene SettingsKeyboard() => new("settings-keyboard", () => EditorFixture.Platformer, (s, w) =>
    {
        var settings = new Talesmith.Editor.Dialogs.SettingsDialogViewModel(s.Get<IDialogService>(), s.Get<Talesmith.Editor.Settings.ISettingsService>(),
            s.Get<Talesmith.UI.Theming.IThemeManager>(), s.Open.Shell.Commands)
        { SelectedSectionIndex = 4, BindingQuery = "play" };
        s.ShowDialog(settings);
        if (settings.Bindings.FirstOrDefault(b => b.Title == "Play from start scene") is { } binding)
            binding.IsRecording = true;
        RenderLoop.Settle(300);
    })
    {
        CropToDialog = true
    };

    public static ProjectScene Shortcuts() => new("shortcuts-dialog", () => EditorFixture.Platformer, (s, w) =>
    {
        s.ShowDialog(new Talesmith.Editor.Dialogs.ShortcutsDialogViewModel(s.Get<IDialogService>(), s.Open.Shell.Commands));
    })
    {
        CropToDialog = true
    };
}

/// <summary>Scenes of the guide's asset pages, in the asset showcase project.</summary>
internal static class AssetShots
{
    public static ProjectScene Health() => new("asset-health", BrokenShowcase, (s, w) =>
    {
        var project = s.Get<IProjectService>();
        RenderLoop.Wait(() => project.ScanProgress is null, 30000);
        _ = s.Get<Talesmith.Editor.Assets.Health.AssetHealthLauncher>().ShowAsync();
        RenderLoop.Settle(2500);
    })
    {
        CropToDialog = true
    };

    public static ProjectScene Search() => new("assets-search", () => AssetShowcase.Project, (s, w) =>
    {
        var layout = s.Get<LayoutService>();
        layout.ShowPanel(PanelIds.Assets);
        layout.Layout.Resize("middle", 0, 0.52, 0.48);
        var browser = s.Get<Talesmith.Editor.Assets.Browser.AssetBrowserViewModel>();
        browser.IsGrid = true;
        browser.Search = "kind:texture";
        RenderLoop.Settle(2500);
    });

    /// <summary>A copy of the showcase with a material whose shader is gone and a .meta file whose asset was deleted outside the editor.</summary>
    private static string BrokenShowcase()
    {
        var folder = Path.Combine(EditorFixture.Scratch, "broken-showcase");
        GuideProjects.CopyFolder(AssetShowcase.Project, folder);
        var assets = Path.Combine(folder, "assets");
        Directory.CreateDirectory(Path.Combine(assets, "materials"));
        File.WriteAllText(Path.Combine(assets, "materials", "glow.tmaterial"),
            """{ "version": 1, "blend": "additive", "shader": "5e0c4a1b2d3f4e5a6b7c8d9e0f1a2b3c", "parameters": [] }""");
        File.WriteAllText(Path.Combine(assets, "sprites", "old-chest.png.meta"),
            """{ "version": 1, "guid": "0f1e2d3c4b5a69788796a5b4c3d2e1f0", "importer": "texture", "importerVersion": 1 }""");
        return folder;
    }
}

/// <summary>Scenes of the guide's effects pages.</summary>
internal static class EffectsShots
{
    /// <summary>Lantern Grove's night with Preview on and a lantern's light selected, showing its radius and the shadows of the ground.</summary>
    public static ProjectScene NightScene() => new("lighting-night", () => GuideProjects.LanternGrove, (s, w) =>
    {
        s.Layout(LayoutService.DefaultPreset);
        var lights = s.Model.Entities.Where(e => e.FindComponent(LightingNames.Light) is not null).ToList();
        var light = lights.FirstOrDefault(e => e.Name.Contains("Campfire", StringComparison.OrdinalIgnoreCase)) ?? lights.FirstOrDefault();
        if (light is not null)
            s.Select(light.Id);
        GuideScene.ExpandInspector(w, "Transform", "Sprite");
        s.Get<ViewportOptions>().IsPreviewing = true;
        s.Get<ViewportService>().FrameSelection(animate: false);
        RenderLoop.Settle(200);
        s.Get<ViewportCamera>().ZoomTo(0.8f, animate: false);
        RenderLoop.Settle(2000);
    });
}

/// <summary>Scenes of the guide's tile map pages.</summary>
internal static class TileMapShots
{
    public static ProjectScene NewMapDialog() => new("tilemap-new-dialog", () => EditorFixture.Platformer, (s, w) =>
    {
        var viewModel = new Talesmith.Editor.TileMaps.Dialogs.NewMapDialogViewModel(s.Get<IProjectService>());
        _ = s.Get<Talesmith.Editor.TileMaps.Dialogs.TileMapDialogs>().ShowAsync(new Talesmith.Editor.TileMaps.Dialogs.NewMapDialogView(), viewModel);
        RenderLoop.Settle(400);
    })
    {
        CropToDialog = true
    };

    public static ProjectScene Layers() => new("tilemap-layers", () => GuideProjects.LanternGrove, (s, w) =>
    {
        s.Layout(LayoutService.TileMappingPreset);
        s.EditSceneMap();
        s.ShowPanel(PanelIds.TileMap);
        RenderLoop.Settle(400);
    })
    {
        CropToPanel = typeof(Talesmith.Editor.TileMaps.Panel.TileMapPanelView),
        WindowSize = new Size(1440, 1000)
    };

    public static ProjectScene Collision() => new("tilemap-collision", GuideProjects.HexAdventure, (s, w) =>
    {
        s.Layout(LayoutService.TileMappingPreset);
        s.EditSceneMap();
        GuideScene.ExpandInspector(w, "Transform");
        s.FrameSelection(0.9f);
        s.Tools.Select("tile.collision");
        var center = s.CenterCell();
        s.Press(s.Offset(center, -3, -1));
        for (var i = -2; i <= 3; i++)
            s.Drag(s.Offset(center, i, i % 2 == 0 ? -1 : 0));
        s.Release(s.Offset(center, 3, 0));
        RenderLoop.Settle(300);
        s.Press(s.Offset(center, -2, 3));
        s.Drag(s.Offset(center, 0, 3));
        s.Hover(s.Offset(center, 0, 3));
    });

    public static ProjectScene Select() => new("tilemap-select", () => EditorFixture.HexQuest, (s, w) =>
    {
        s.Layout(LayoutService.TileMappingPreset);
        s.EditSceneMap();
        GuideScene.ExpandInspector(w, "Transform");
        s.FrameSelection(0.5f);
        s.Map.ActiveLayer = s.Map.Map!.TileLayers[^1];
        s.Tools.Select("tile.select");
        var center = s.CenterCell();
        s.Press(s.Offset(center, -3, -2));
        s.Drag(s.Offset(center, 2, 2));
        s.Release(s.Offset(center, 2, 2));
        s.Hover(s.Offset(center, 4, 3));
    });

    public static ProjectScene Objects() => new("tilemap-objects", () => EditorFixture.HexQuest, (s, w) =>
    {
        s.Layout(LayoutService.TileMappingPreset);
        s.EditSceneMap();
        GuideScene.ExpandInspector(w, "Transform");
        var layer = s.Map.Map!.ObjectLayers[0];
        s.Map.ActiveLayer = layer;
        s.Tools.Select("tile.objects");
        var target = layer.Objects.FirstOrDefault(o => o.Name.Contains("harbor", StringComparison.OrdinalIgnoreCase)) ?? layer.Objects[0];
        s.Map.SelectedObject = new MapObjectRef(layer, target.Id);
        RenderLoop.Settle(200);
        if (w.GetVisualDescendants().OfType<Talesmith.Editor.TileMaps.Panel.TileMapPanelView>().FirstOrDefault()?.DataContext is
            Talesmith.Editor.TileMaps.Panel.TileMapPanelViewModel panel)
            panel.Tilesets.TabIndex = 2;
        s.Frame(s.Map.CellCenter(target.Cell), 0.6f);
        RenderLoop.Settle(400);
    });

    public static ProjectScene TileProperties() => new("tile-properties", () => GuideProjects.LanternGrove, (s, w) =>
    {
        s.EditSceneMap();
        var map = s.Map.Map!;
        var (tileset, tileId) = map.Tilesets.SelectMany(t => Enumerable.Range(0, t.TileCount).Select(i => (Tileset: t, Id: i)))
            .FirstOrDefault(p => p.Tileset.Find(p.Id)?.Properties.GetBool("oneWay") == true);
        tileset ??= map.Tilesets.First(t => t.ImageFile is not null);
        var viewModel = new Talesmith.Editor.TileMaps.Dialogs.TilePropertiesDialogViewModel(tileset, tileId, map.Layout) { SectionIndex = 1 };
        if (viewModel.Collision.Shapes.Count == 0)
        {
            var (w2, h2) = (tileset.TileWidth / 2f, tileset.TileHeight / 2f);
            foreach (var point in new[] { new Vector2(-w2 + 6, -h2 + 1), new Vector2(w2, -h2 + 1), new Vector2(w2, -h2 + 12), new Vector2(-w2 + 6, -h2 + 12) })
                viewModel.Collision.AddVertex(point);
            viewModel.Collision.Finish();
        }
        _ = s.Get<Talesmith.Editor.TileMaps.Dialogs.TileMapDialogs>().ShowAsync(new Talesmith.Editor.TileMaps.Dialogs.TilePropertiesDialogView(), viewModel);
        RenderLoop.Settle(400);
    })
    {
        CropToDialog = true
    };
}

/// <summary>Scenes of the guide's pages on scenes, the viewport, the hierarchy, the inspector and prefabs, in the finished Coin Run.</summary>
internal static class SceneShots
{
    public static TutorialScene SceneSettings() => new("scene-settings", TutorialStep.Campfire, (s, w) =>
    {
        GuideScene.ExpandInspector(w);
    })
    {
        CropToPanel = typeof(InspectorView),
        WindowSize = new Size(1320, 640)
    };

    public static TutorialScene HierarchyFilter() => new("hierarchy-filter", TutorialStep.Campfire, (s, w) =>
    {
        s.Get<HierarchyViewModel>().Filter = "2";
        RenderLoop.Settle(300);
    })
    {
        CropToPanel = typeof(HierarchyView),
        WindowSize = new Size(1500, 700)
    };

    public static TutorialScene Camera() => new("viewport-camera", TutorialStep.Campfire, (s, w) =>
    {
        s.Select(s.Entity("Main Camera"));
        s.Frame(new Vector2(256, 96), 0.95f);
    });

    public static TutorialScene Rotate() => new("viewport-rotate", TutorialStep.Campfire, (s, w) =>
    {
        s.Select(s.Hero);
        s.Tools.Select("rotate");
        s.Frame(new Vector2(128, 150), 4f);
    });

    public static TutorialScene PrefabInstance() => new("prefab-instance", TutorialStep.Coins, (s, w) =>
    {
        var first = s.Entity("Coin 9");
        s.Model.Rename(first, "Coin");
        var prefabs = s.Get<Talesmith.Editor.Prefabs.PrefabWorkflow>();
        var create = prefabs.CreatePrefabAsync(first);
        RenderLoop.Wait(() => create.IsCompleted, 30000);
        var prefab = create.Result!.Value;
        RenderLoop.Settle(300);
        var coins = s.Entity("Coins");
        var second = prefabs.Instantiate(prefab, coins, new Vector2(400, 40))!.Value;
        prefabs.Instantiate(prefab, coins, new Vector2(464, 40));
        RenderLoop.Settle(300);
        s.Get<Talesmith.Editor.Prefabs.EntityDataService>().Set(second, "SpriteAnimator", "speed", 2);
        s.Get<HierarchyViewModel>().Tree.SetExpandedRecursive(coins, true);
        s.Select(second);
        s.Frame(new Vector2(400, 60), 1.8f);
        GuideScene.ExpandInspector(w, "Sprite", "Collider 2D");
    });
}
