using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Talesmith.UI.Controls;
using Talesmith.UI.Docking;

namespace Talesmith.UI.Tests.Docking;

public sealed class DockHostTests
{
    private static (Window Window, DockHost Host, DockLayout Layout) Show()
    {
        var provider = new DockContentProvider
        {
            new DockablePanel("hierarchy", "Hierarchy", () => new TextBox { Text = "tree" }),
            new DockablePanel("scene", "Scene", () => new Border()) { CanClose = false },
            new DockablePanel("console", "Console", () => new TextBox { Text = "log" }),
        };
        var layout = new DockLayout(new DockSplit("root", DockOrientation.Horizontal,
            new DockGroup("left", "hierarchy"),
            new DockGroup("center", "scene", "console")));
        var host = new DockHost { Layout = layout, ContentProvider = provider };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        window.Show();
        window.UpdateLayout();
        return (window, host, layout);
    }

    private static DockGroupView? GroupOf(Control content) => content.FindAncestorOfType<DockGroupView>();

    private static Point Center(Window window, Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), window)!.Value;

    private static Rect BoundsIn(Window window, Visual visual) => new(visual.TranslatePoint(default, window)!.Value, visual.Bounds.Size);

    /// <summary>Presses on a panel's tab, ready to drag it; returns where the pointer is.</summary>
    private static Point PressTab(Window window, string title)
    {
        var position = Center(window, window.GetVisualDescendants().OfType<DockTab>().First(t => t.Title == title));
        window.MouseMove(position, RawInputModifiers.None);
        window.MouseDown(position, MouseButton.Left, RawInputModifiers.None);
        return position;
    }

    /// <summary>Moves the pressed pointer in a few steps, as a mouse would; returns where it is.</summary>
    private static Point DragTo(Window window, Point from, Point to)
    {
        for (var i = 1; i <= 4; i++)
            window.MouseMove(from + (to - from) * (i / 4.0), RawInputModifiers.LeftMouseButton);
        window.UpdateLayout();
        return to;
    }

    private static void Drop(Window window, Point point)
    {
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static Border Guide(Window window, DockEdge edge, bool isWorkspace) => window.GetVisualDescendants().OfType<DockGuideGlyph>()
        .Single(g => g.IsEffectivelyVisible && g.Edge == edge && g.IsWorkspace == isWorkspace).FindAncestorOfType<Border>()!;

    private static string? DropLabel(Window window) => window.GetVisualDescendants().OfType<TextBlock>()
        .Single(t => t.Classes.Contains("dock-drag-target")) is { IsEffectivelyVisible: true } label ? label.Text : null;

    [Fact]
    public void MovingAPanelKeepsItsContentInstance()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var console = host.GetContent("console")!;
            ((TextBox)console).Text = "typed";

            layout.DockPanel("console", "left", DockEdge.Bottom);
            window.UpdateLayout();

            Assert.Same(console, host.GetContent("console"));
            Assert.Equal("typed", ((TextBox)console).Text);
            Assert.Equal(layout.FindPanel("console"), GroupOf(console)?.Group);
            Assert.True(console.IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public void OnlyTheActivePanelOfAGroupIsVisible()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();

            layout.ActivatePanel("console");
            window.UpdateLayout();

            Assert.True(host.GetContent("console")!.IsEffectivelyVisible);
            Assert.False(host.GetContent("scene")!.IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public void ClosingRespectsCanCloseAndReopeningRestoresTheSameContent()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var console = host.GetContent("console");

            Assert.False(host.ClosePanel("scene"));
            Assert.True(host.ClosePanel("console"));
            window.UpdateLayout();
            Assert.Null(GroupOf(console!));

            layout.EnsureVisible("console");
            window.UpdateLayout();
            Assert.Same(console, host.GetContent("console"));
            Assert.Equal("center", GroupOf(console!)?.Group.Id);
            window.Close();
        });
    }

    [Fact]
    public void ShowingAndReopeningATabLeaveTheOtherPanelsInPlace()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var detached = 0;
            host.GetContent("hierarchy")!.DetachedFromVisualTree += (_, _) => detached++;
            host.GetContent("scene")!.DetachedFromVisualTree += (_, _) => detached++;
            var console = host.GetContent("console")!;

            layout.EnsureVisible("console");
            window.UpdateLayout();
            Assert.True(console.IsEffectivelyVisible);

            Assert.True(host.ClosePanel("console"));
            layout.EnsureVisible("console");
            window.UpdateLayout();

            Assert.True(console.IsEffectivelyVisible);
            Assert.Equal("center", GroupOf(console)?.Group.Id);
            Assert.Equal(0, detached);
            window.Close();
        });
    }

    [Fact]
    public void CollapsingAGroupHidesItsSplitter()
    {
        Headless.Run(() =>
        {
            var (window, _, layout) = Show();
            int VisibleSplitters() => window.GetVisualDescendants().OfType<DockSplitter>().Count(s => s.IsVisible);
            Assert.Equal(1, VisibleSplitters());

            layout.SetCollapsed("left", true);
            window.UpdateLayout();
            Assert.Equal(0, VisibleSplitters());

            layout.SetCollapsed("left", false);
            window.UpdateLayout();
            Assert.Equal(1, VisibleSplitters());
            window.Close();
        });
    }

    [Fact]
    public void ClosingEveryPanelShowsTheEmptyState()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();

            layout.ClosePanel("hierarchy");
            layout.ClosePanel("console");
            layout.ClosePanel("scene");
            window.UpdateLayout();
            Assert.Contains(window.GetVisualDescendants().OfType<EmptyState>(), e => e.Title == "No panels open");

            layout.EnsureVisible("scene");
            window.UpdateLayout();
            Assert.True(host.GetContent("scene")!.IsEffectivelyVisible);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<EmptyState>(), e => e.Title == "No panels open");
            window.Close();
        });
    }

    [Fact]
    public void RearrangingTheLayoutLeavesTheOtherPanelsInPlace()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var detached = 0;
            host.GetContent("hierarchy")!.DetachedFromVisualTree += (_, _) => detached++;
            host.GetContent("scene")!.DetachedFromVisualTree += (_, _) => detached++;

            layout.DockPanel("console", "center", DockEdge.Bottom);
            window.UpdateLayout();
            Assert.True(host.GetContent("console")!.IsEffectivelyVisible);
            Assert.True(host.GetContent("scene")!.IsEffectivelyVisible);
            Assert.True(GroupOf(host.GetContent("console")!)!.Bounds.Top > GroupOf(host.GetContent("scene")!)!.Bounds.Top);

            layout.ToggleMaximize("left");
            layout.Restore();
            layout.SetCollapsed("left", true);
            layout.SetCollapsed("left", false);
            window.UpdateLayout();
            Assert.Equal(0, detached);
            window.Close();
        });
    }

    [Fact]
    public void GroupsKeepTheirMinimumSizeWhileThereIsRoom()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();

            layout.Resize("root", 0, 0.01, 0.99);
            window.UpdateLayout();

            Assert.Equal(DockWorkspace.MinimumGroupSize.Width, GroupOf(host.GetContent("hierarchy")!)!.Bounds.Width);
            window.Close();
        });
    }

    [Fact]
    public void MaximizingShowsOnlyThatGroup()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();

            layout.ToggleMaximize("left");
            window.UpdateLayout();

            var views = window.GetVisualDescendants().OfType<DockGroupView>().Where(v => v.IsEffectivelyVisible).ToList();
            Assert.Equal(["left"], views.Select(v => v.Group.Id));
            Assert.True(host.GetContent("hierarchy")!.IsEffectivelyVisible);
            Assert.Equal(window.Bounds.Size, views[0].Bounds.Size);
            Assert.Same(window, TopLevel.GetTopLevel(host.GetContent("scene")));

            layout.Restore();
            window.UpdateLayout();
            Assert.Equal(2, window.GetVisualDescendants().OfType<DockGroupView>().Count(v => v.IsEffectivelyVisible));
            window.Close();
        });
    }

    [Fact]
    public void DroppingATabOnAGroupsGuideDocksItOnThatSide()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var hierarchy = GroupOf(host.GetContent("hierarchy")!)!;

            var pointer = DragTo(window, PressTab(window, "Console"), Center(window, hierarchy));
            var guide = DragTo(window, pointer, Center(window, Guide(window, DockEdge.Bottom, isWorkspace: false)));
            Assert.Contains("active", Guide(window, DockEdge.Bottom, isWorkspace: false).Classes);
            Assert.Equal("Below Hierarchy", DropLabel(window));
            Drop(window, guide);

            var console = GroupOf(host.GetContent("console")!)!;
            Assert.NotSame(hierarchy, console);
            Assert.Equal(hierarchy.Bounds.Left, console.Bounds.Left);
            Assert.Equal(hierarchy.Bounds.Bottom + DockWorkspace.Gap, console.Bounds.Top);
            Assert.Same(layout.FindPanel("scene"), GroupOf(host.GetContent("scene")!)!.Group);
            window.Close();
        });
    }

    [Fact]
    public void DroppingATabOnAWorkspaceGuideDocksItAlongTheWholeEdge()
    {
        Headless.Run(() =>
        {
            var (window, host, _) = Show();

            var pointer = DragTo(window, PressTab(window, "Console"), Center(window, GroupOf(host.GetContent("scene")!)!));
            var guide = DragTo(window, pointer, Center(window, Guide(window, DockEdge.Right, isWorkspace: true)));
            Assert.Equal("Right edge of the window", DropLabel(window));
            Drop(window, guide);

            var console = BoundsIn(window, GroupOf(host.GetContent("console")!)!);
            Assert.Equal(window.Bounds.Width, console.Right);
            Assert.Equal(window.Bounds.Height, console.Height);
            window.Close();
        });
    }

    [Fact]
    public void TheDraggedTabNeverCoversAGuide()
    {
        Headless.Run(() =>
        {
            var (window, host, _) = Show();
            var hierarchy = GroupOf(host.GetContent("hierarchy")!)!;
            var pointer = DragTo(window, PressTab(window, "Console"), Center(window, hierarchy));

            foreach (var edge in new[] { DockEdge.Left, DockEdge.Top, DockEdge.Right, DockEdge.Bottom })
            {
                pointer = DragTo(window, pointer, Center(window, Guide(window, edge, isWorkspace: false)));
                var ghost = BoundsIn(window, window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("dock-drag-ghost")));
                var guides = window.GetVisualDescendants().OfType<DockGuideGlyph>().Where(g => g.IsEffectivelyVisible).ToList();
                Assert.NotEmpty(guides);
                Assert.All(guides, g => Assert.False(ghost.Intersects(BoundsIn(window, g))));
                Assert.True(new Rect(window.Bounds.Size).Contains(ghost));
            }

            window.Close();
        });
    }

    [Fact]
    public void MovingATabAlongItsOwnTabsShowsOnlyWhereItGoes()
    {
        Headless.Run(() =>
        {
            var (window, _, layout) = Show();
            var scene = BoundsIn(window, window.GetVisualDescendants().OfType<DockTab>().First(t => t.Title == "Scene"));

            var before = DragTo(window, PressTab(window, "Console"), new Point(scene.X + 4, scene.Center.Y));
            Assert.False(window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("dock-drop-indicator")).IsVisible);
            Assert.True(window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("dock-drop-caret")).IsVisible);
            Assert.Null(DropLabel(window));
            Drop(window, before);

            Assert.Equal(["console", "scene"], layout.FindPanel("console")!.Panels);
            window.Close();
        });
    }

    [Fact]
    public void TheTabMenuShowsItsCommandsEachTimeItOpens()
    {
        Headless.Run(() =>
        {
            var (window, _, layout) = Show();
            var tab = window.GetVisualDescendants().OfType<DockTab>().First(t => t.Title == "Console");
            var menu = Assert.IsType<MenuFlyout>(tab.ContextFlyout);

            menu.ShowAt(tab);
            var presenter = Assert.IsAssignableFrom<ItemsControl>(menu.Popup.Child);
            presenter.UpdateLayout();
            Assert.Contains(presenter.GetRealizedContainers(), c => c is MenuItem { Header: "Move to new window", IsEnabled: true });
            menu.Hide();

            layout.ToggleMaximize("center");
            menu.ShowAt(tab);
            presenter.UpdateLayout();
            Assert.Contains(presenter.GetRealizedContainers(), c => c is MenuItem { Header: "Restore layout" });
            window.Close();
        });
    }

    [Fact]
    public void AFloatingPanelOpensInAWindowOfItsOwnWithItsContent()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var opened = new List<Window>();
            host.WindowOpened += (_, e) => opened.Add(e.Window);
            var console = host.GetContent("console")!;

            layout.FloatPanel("console", new PixelPoint(900, 40), new Size(420, 300));

            var floating = Assert.Single(opened);
            Assert.Equal([floating], host.Windows);
            Assert.True(floating.IsVisible);
            Assert.Equal("Console", floating.Title);
            Assert.Same(floating, TopLevel.GetTopLevel(console));
            Assert.True(console.IsEffectivelyVisible);
            Assert.Same(console, host.GetContent("console"));
            window.Close();
        });
    }

    [Fact]
    public void ClosingAFloatingWindowReturnsItsPanels()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var console = host.GetContent("console")!;
            layout.FloatPanel("console", new PixelPoint(900, 40), new Size(420, 300));

            host.Windows.Single().Close();
            window.UpdateLayout();

            Assert.Empty(host.Windows);
            Assert.Empty(layout.Floats);
            Assert.Equal("center", layout.FindPanel("console")!.Id);
            Assert.Same(window, TopLevel.GetTopLevel(console));
            Assert.True(console.IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public void FloatingWindowsStayInTheLayoutWhenTheMainWindowCloses()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            layout.FloatPanel("console", new PixelPoint(900, 40), new Size(420, 300));
            var floating = host.Windows.Single();

            window.Close();

            Assert.False(floating.IsVisible);
            Assert.Equal(["console"], layout.Floats.SelectMany(f => ((DockGroup)f.Root).Panels));
        });
    }

    [Fact]
    public void AnotherLayoutClosesTheWindowsOfTheFormerOneAndOpensItsOwn()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            layout.FloatPanel("console", new PixelPoint(900, 40), new Size(420, 300));
            var former = host.Windows.Single();
            var next = new DockLayout(new DockSplit("root", DockOrientation.Horizontal, new DockGroup("left", "hierarchy"), new DockGroup("center", "scene", "console")));
            next.FloatPanel("hierarchy", new PixelPoint(900, 400), new Size(300, 300));

            host.Layout = next;

            Assert.False(former.IsVisible);
            Assert.Single(layout.Floats);
            var opened = Assert.Single(host.Windows);
            Assert.Equal("Hierarchy", opened.Title);
            Assert.Same(opened, TopLevel.GetTopLevel(host.GetContent("hierarchy")));
            window.Close();
        });
    }

    [Fact]
    public void ATabDraggedOutOfEveryWindowOpensInANewWindowThere()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            var outside = new Point(window.Bounds.Width + 300, 200);

            var pointer = DragTo(window, PressTab(window, "Console"), outside);
            Drop(window, pointer);

            var floating = Assert.Single(layout.Floats);
            Assert.Equal(["console"], ((DockGroup)floating.Root).Panels);
            Assert.True(floating.Position.X > window.Position.X + window.Bounds.Width);
            Assert.Same(host.Windows.Single(), TopLevel.GetTopLevel(host.GetContent("console")));
            window.Close();
        });
    }

    [Fact]
    public void APanelsHeaderActionsFollowItToAGroupEarlierInTheLayout()
    {
        Headless.Run(() =>
        {
            var actions = new Button();
            var provider = new DockContentProvider
            {
                new DockablePanel("hierarchy", "Hierarchy", () => new Border()),
                new DockablePanel("scene", "Scene", () => new Border()) { HeaderActions = actions },
            };
            var layout = new DockLayout(new DockSplit("root", DockOrientation.Horizontal, new DockGroup("left", "hierarchy"), new DockGroup("center", "scene")));
            var host = new DockHost { Layout = layout, ContentProvider = provider };
            var window = new Window { Content = host, Width = 800, Height = 600 };
            window.Show();
            window.UpdateLayout();

            layout.MovePanel("scene", "left");
            window.UpdateLayout();

            Assert.Same(GroupOf(host.GetContent("scene")!), actions.FindAncestorOfType<DockGroupView>());
            window.Close();
        });
    }

    [Fact]
    public void APanelsHeaderActionsFollowItBetweenWindows()
    {
        Headless.Run(() =>
        {
            var actions = new Button();
            var provider = new DockContentProvider
            {
                new DockablePanel("hierarchy", "Hierarchy", () => new Border()),
                new DockablePanel("inspector", "Inspector", () => new Border()) { HeaderActions = actions },
            };
            var layout = new DockLayout(new DockSplit("root", DockOrientation.Horizontal, new DockGroup("left", "hierarchy"), new DockGroup("right", "inspector")));
            var host = new DockHost { Layout = layout, ContentProvider = provider };
            var window = new Window { Content = host, Width = 800, Height = 600 };
            window.Show();
            layout.FloatPanel("inspector", new PixelPoint(900, 40), new Size(400, 300));
            Assert.Same(host.Windows.Single(), TopLevel.GetTopLevel(actions));

            layout.MovePanel("inspector", "left");
            window.UpdateLayout();

            Assert.Same(window, TopLevel.GetTopLevel(actions));
            Assert.Same(GroupOf(host.GetContent("inspector")!), actions.FindAncestorOfType<DockGroupView>());
            window.Close();
        });
    }

    [Fact]
    public void APanelLeavingAClosingWindowStaysShownWhenFocusMovesMeanwhile()
    {
        Headless.Run(() =>
        {
            var (window, host, layout) = Show();
            layout.FloatPanel("console", new PixelPoint(900, 40), new Size(420, 300));
            host.Windows.Single().Closed += (_, _) => layout.FocusGroup("left");

            layout.MovePanel("console", "center");
            window.UpdateLayout();

            Assert.Empty(host.Windows);
            Assert.True(host.GetContent("console")!.IsEffectivelyVisible);
            window.Close();
        });
    }
}
