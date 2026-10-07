using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Talesmith.Screenshots.Capture;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Talesmith.UI.Docking;

namespace Talesmith.Screenshots.Scenes;

/// <summary>An editor-like workspace: the app bar, a dock host with sample panels and the status bar.</summary>
internal class DockWorkspaceScene : ScreenshotScene
{
    public override string Name => "dock-workspace";

    public override Size Size => new(1440, 880);

    public static DockLayout DefaultLayout() => new(
        new DockSplit("root", DockOrientation.Horizontal,
            new DockGroup("left", "hierarchy") { Size = 0.18 },
            new DockSplit("middle", DockOrientation.Vertical,
                new DockGroup("center", "scene", "game") { Size = 0.68 },
                new DockGroup("bottom", "assets", "console", "animation", "tilemap", "particles", "lighting") { Size = 0.32 })
            { Size = 0.58 },
            new DockGroup("right", "inspector", "project") { Size = 0.24 }));

    public override Control Build()
    {
        var sprites = Samples.LoadSprites();
        var accent = Samples.Resource("AccentBrush") is ISolidColorBrush solid ? solid.Color : Colors.SlateBlue;

        var viewportActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { SmallIcon(Icons.Grid, "Grid"), SmallIcon(Icons.Magnet, "Snap to grid"), SmallIcon(Icons.Eye, "Gizmos") }
        };
        var assetActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { SmallIcon(Icons.Plus, "Create"), SmallIcon(Icons.Refresh, "Refresh") } };

        var provider = new DockContentProvider
        {
            new DockablePanel("hierarchy", "Hierarchy", Samples.Hierarchy) { Icon = Icons.ListTree, CanClose = false },
            new DockablePanel("scene", "Scene", () => new ViewportMock(sprites.Count > 0 ? sprites[0] : null, accent)) { Icon = Icons.Clapperboard, CanClose = false, HeaderActions = viewportActions },
            new DockablePanel("game", "Game", () => new EmptyState { Icon = Icons.Play, Title = "Game view", Hint = "Press Play to run the scene in the game view." }) { Icon = Icons.Monitor },
            new DockablePanel("inspector", "Inspector", Samples.Inspector) { Icon = Icons.Sliders },
            new DockablePanel("project", "Project", () => new EmptyState { Icon = Icons.Settings, Title = "Project settings" }) { Icon = Icons.Settings },
            new DockablePanel("assets", "Assets", () => Samples.Assets(sprites)) { Icon = Icons.FolderOpen, HeaderActions = assetActions },
            new DockablePanel("console", "Console", Samples.Console) { Icon = Icons.Terminal },
            new DockablePanel("animation", "Animation", () => new EmptyState { Icon = Icons.Activity, Title = "No animation selected" }) { Icon = Icons.Activity },
            new DockablePanel("tilemap", "Tile Map", () => new EmptyState { Icon = Icons.Map, Title = "No tile map selected", Hint = "Select a tile map entity to paint tiles." }) { Icon = Icons.Map },
            new DockablePanel("particles", "Particles", () => new EmptyState { Icon = Icons.Sparkles, Title = "No emitter selected" }) { Icon = Icons.Sparkles },
            new DockablePanel("lighting", "Lighting", () => new EmptyState { Icon = Icons.Lightbulb, Title = "Lighting" }) { Icon = Icons.Lightbulb },
        };

        var layout = DefaultLayout();
        layout.Fallback = DefaultLayout();
        layout.ActivatePanel("scene");
        var host = new DockHost { Layout = layout, ContentProvider = provider };

        var appBar = Samples.AppBar("emerald-coast.tscene");
        var status = Samples.StatusBar();
        var root = new DockPanel { Background = Samples.Resource("BackgroundBrush") };
        DockPanel.SetDock(appBar, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(appBar);
        root.Children.Add(status);
        root.Children.Add(host);
        return root;
    }

    protected static DockHost Host(Window window) => window.GetVisualDescendants().OfType<DockHost>().Single();

    protected static DockTab Tab(Window window, string title) =>
        window.GetVisualDescendants().OfType<DockTab>().First(t => t.Title == title);

    protected static Point Center(Window window, Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window) ?? default;

    private static Button SmallIcon(Geometry icon, string tip)
    {
        var button = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 14 } };
        ToolTip.SetTip(button, tip);
        return button;
    }
}

/// <summary>The workspace mid-drag: the Console tab is dragged onto the guide that docks it on the left of the inspector.</summary>
internal sealed class DockDragScene : DockWorkspaceScene
{
    public override string Name => "dock-drag";

    public override void Prepare(Window window)
    {
        var tab = Tab(window, "Console");
        var start = Center(window, tab);
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        RenderLoop.Pump();

        var inspector = window.GetVisualDescendants().OfType<DockGroupView>().First(v => v.Group.Id == "right");
        var area = new Rect(inspector.TranslatePoint(default, window) ?? default, inspector.Bounds.Size);
        Drag(window, start, area.Center);
        var guide = window.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("dock-guide") && b.IsVisible)
            .Select(b => new Rect(b.TranslatePoint(default, window) ?? default, b.Bounds.Size))
            .Where(bounds => area.Contains(bounds))
            .MinBy(bounds => bounds.X);
        Drag(window, area.Center, guide.Center);
    }

    private static void Drag(Window window, Point from, Point to)
    {
        for (var i = 1; i <= 6; i++)
        {
            window.MouseMove(from + (to - from) * (i / 6.0), RawInputModifiers.LeftMouseButton);
            RenderLoop.Pump(1);
        }
    }
}

/// <summary>The workspace after rearranging: Console docked beside the inspector, the bottom group maximized off and a collapsed left region.</summary>
internal sealed class DockRearrangedScene : DockWorkspaceScene
{
    public override string Name => "dock-rearranged";

    public override void Prepare(Window window)
    {
        var host = Host(window);
        var layout = host.Layout!;
        layout.DockPanel("console", "right", DockEdge.Bottom, 0.4);
        layout.DockPanel("particles", "center", DockEdge.Right, 0.3);
        layout.ActivatePanel("assets");
        RenderLoop.Pump();
        var hover = Tab(window, "Particles");
        window.MouseMove(Center(window, hover), RawInputModifiers.None);
    }
}
