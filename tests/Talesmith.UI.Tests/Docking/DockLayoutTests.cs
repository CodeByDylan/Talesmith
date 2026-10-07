using Avalonia;
using Talesmith.UI.Docking;

namespace Talesmith.UI.Tests.Docking;

public sealed class DockLayoutTests
{
    private static DockLayout CreateDefault() => new(
        new DockSplit("root", DockOrientation.Horizontal,
            new DockGroup("left", "hierarchy") { Size = 0.2 },
            new DockSplit("middle", DockOrientation.Vertical,
                new DockGroup("center", "viewport") { Size = 0.7 },
                new DockGroup("bottom", "assets", "console") { Size = 0.3 })
            { Size = 0.6 },
            new DockGroup("right", "inspector") { Size = 0.2 }));

    [Fact]
    public void FindPanelReturnsTheContainingGroup()
    {
        var layout = CreateDefault();

        Assert.Equal("bottom", layout.FindPanel("console")?.Id);
        Assert.Null(layout.FindPanel("missing"));
        Assert.Equal(["hierarchy", "viewport", "assets", "console", "inspector"], layout.Panels);
    }

    [Fact]
    public void MovePanelToAnotherGroupAddsItAsActiveTab()
    {
        var layout = CreateDefault();

        layout.MovePanel("console", "right");

        var right = (DockGroup)layout.FindNode("right")!;
        Assert.Equal(["inspector", "console"], right.Panels);
        Assert.Equal("console", right.ActivePanel);
        Assert.Same(right, layout.FocusedGroup);
        Assert.Equal(["assets"], ((DockGroup)layout.FindNode("bottom")!).Panels);
    }

    [Fact]
    public void MovePanelLastPanelOfGroupRemovesGroupAndMergesSplit()
    {
        var layout = CreateDefault();

        layout.MovePanel("assets", "center");
        layout.MovePanel("console", "center");

        Assert.Null(layout.FindNode("bottom"));
        Assert.Null(layout.FindNode("middle"));
        var root = Assert.IsType<DockSplit>(layout.Root);
        Assert.Equal(["left", "center", "right"], root.Children.Select(c => c.Id));
        Assert.Equal(1, root.Children.Sum(c => c.Size), 6);
    }

    [Fact]
    public void MovePanelWithinGroupReorders()
    {
        var layout = new DockLayout(new DockGroup("g", "a", "b", "c", "d"));

        layout.MovePanel("a", "g", 3);
        Assert.Equal(["b", "c", "a", "d"], ((DockGroup)layout.Root).Panels);

        layout.MovePanel("d", "g", 0);
        Assert.Equal(["d", "b", "c", "a"], ((DockGroup)layout.Root).Panels);

        layout.MovePanel("b", "g");
        Assert.Equal(["d", "c", "a", "b"], ((DockGroup)layout.Root).Panels);
    }

    [Fact]
    public void DockPanelAtEdgeOfGroupInPerpendicularSplitCreatesNestedSplit()
    {
        var layout = CreateDefault();

        Assert.True(layout.DockPanel("inspector", "left", DockEdge.Bottom, 0.4));

        var left = layout.FindPanel("hierarchy")!;
        var split = Assert.IsType<DockSplit>(left.Parent);
        Assert.Equal(DockOrientation.Vertical, split.Orientation);
        Assert.Equal(["hierarchy", "inspector"], split.Children.OfType<DockGroup>().SelectMany(g => g.Panels));
        Assert.Equal(0.4, split.Children[1].Size, 6);
        Assert.Null(layout.FindNode("right"));
    }

    [Fact]
    public void DockPanelAtEdgeOfGroupInParallelSplitInsertsSibling()
    {
        var layout = CreateDefault();

        layout.DockPanel("console", "center", DockEdge.Top);

        var middle = (DockSplit)layout.FindNode("middle")!;
        Assert.Equal(3, middle.Children.Count);
        Assert.Equal(["console"], ((DockGroup)middle.Children[0]).Panels);
        Assert.Equal("center", middle.Children[1].Id);
        Assert.Equal(1, middle.Children.Sum(c => c.Size), 6);
    }

    [Fact]
    public void DockPanelAtRootEdgeSpansTheWorkspace()
    {
        var layout = CreateDefault();

        layout.DockPanel("console", "root", DockEdge.Bottom, 0.25);

        var root = Assert.IsType<DockSplit>(layout.Root);
        Assert.Equal(DockOrientation.Vertical, root.Orientation);
        Assert.Equal("root", root.Children[0].Id);
        Assert.Equal(["console"], ((DockGroup)root.Children[1]).Panels);
    }

    [Fact]
    public void DockPanelOntoItsOwnSinglePanelGroupIsRejected()
    {
        var layout = CreateDefault();

        Assert.False(layout.DockPanel("hierarchy", "left", DockEdge.Right));
        Assert.Equal("left", layout.FindPanel("hierarchy")?.Id);
    }

    [Fact]
    public void DockPanelFromItsOwnGroupWithOtherTabsSplitsTheGroup()
    {
        var layout = CreateDefault();

        layout.DockPanel("console", "bottom", DockEdge.Right);

        var split = Assert.IsType<DockSplit>(layout.FindPanel("assets")!.Parent);
        Assert.Equal(DockOrientation.Horizontal, split.Orientation);
        Assert.Equal(["assets", "console"], split.Children.OfType<DockGroup>().SelectMany(g => g.Panels));
    }

    [Fact]
    public void ClosePanelThenEnsureVisibleReopensInTheSameGroup()
    {
        var layout = CreateDefault();

        layout.ClosePanel("console");
        Assert.False(layout.Contains("console"));
        layout.EnsureVisible("console");

        var bottom = (DockGroup)layout.FindNode("bottom")!;
        Assert.Equal(["assets", "console"], bottom.Panels);
        Assert.Equal("console", bottom.ActivePanel);
    }

    [Fact]
    public void ClosePanelLastInGroupReopensBesideItsNeighbor()
    {
        var layout = CreateDefault();

        layout.ClosePanel("inspector");
        Assert.Null(layout.FindNode("right"));
        layout.EnsureVisible("inspector");

        var root = (DockSplit)layout.Root;
        Assert.Equal("right", root.Children[^1].Id);
        Assert.Equal(["inspector"], ((DockGroup)root.Children[^1]).Panels);
        Assert.Equal(0.2, root.Children[^1].Size, 2);
    }

    [Fact]
    public void EnsureVisibleNeverPlacedPanelUsesFallbackLayout()
    {
        var layout = new DockLayout(new DockSplit(DockOrientation.Horizontal, new DockGroup("left", "hierarchy"), new DockGroup("center", "viewport")));
        layout.Fallback = CreateDefault();

        layout.EnsureVisible("inspector");

        var root = (DockSplit)layout.Root;
        Assert.Equal(["hierarchy", "viewport", "inspector"], root.Children.OfType<DockGroup>().SelectMany(g => g.Panels));
    }

    [Fact]
    public void EnsureVisibleExpandsCollapsedAncestorsAndRestoresMaximize()
    {
        var layout = CreateDefault();
        layout.SetCollapsed("left", true);
        layout.ToggleMaximize("center");

        layout.EnsureVisible("hierarchy");

        Assert.False(layout.FindNode("left")!.IsCollapsed);
        Assert.Null(layout.MaximizedGroup);
        Assert.Equal("left", layout.FocusedGroup?.Id);
    }

    [Fact]
    public void ToggleMaximizeTogglesAndIsShownReflectsIt()
    {
        var layout = CreateDefault();

        layout.ToggleMaximize("bottom");
        Assert.Equal("bottom", layout.MaximizedGroup?.Id);
        Assert.True(layout.IsShown(layout.FindNode("bottom")!));
        Assert.False(layout.IsShown(layout.FindNode("left")!));

        layout.ToggleMaximize("bottom");
        Assert.Null(layout.MaximizedGroup);
    }

    [Fact]
    public void ResizeSetsNeighbourSizesAndRaisesSizesChange()
    {
        var layout = CreateDefault();
        var kinds = new List<DockChangeKind>();
        layout.Changed += (_, e) => kinds.Add(e.Kind);

        layout.Resize("root", 0, 0.3, 0.5);

        var root = (DockSplit)layout.Root;
        Assert.Equal(0.3, root.Children[0].Size);
        Assert.Equal(0.5, root.Children[1].Size);
        Assert.Equal([DockChangeKind.Sizes], kinds);
    }

    [Fact]
    public void ActivatePanelRaisesActivationOnly()
    {
        var layout = CreateDefault();
        var kinds = new List<DockChangeKind>();
        layout.Changed += (_, e) => kinds.Add(e.Kind);

        layout.ActivatePanel("console");

        Assert.Equal("console", layout.FindPanel("console")!.ActivePanel);
        Assert.Equal([DockChangeKind.Activation], kinds);
    }

    [Fact]
    public void EnsureVisibleRaisesStructureOnlyWhenTheArrangementChanges()
    {
        var layout = CreateDefault();
        var kinds = new List<DockChangeKind>();
        layout.Changed += (_, e) => kinds.Add(e.Kind);

        layout.EnsureVisible("console");
        Assert.Equal("console", layout.FindPanel("console")!.ActivePanel);
        Assert.Equal([DockChangeKind.Activation], kinds);

        kinds.Clear();
        layout.EnsureVisible("console");
        Assert.Empty(kinds);

        layout.ClosePanel("console");
        kinds.Clear();
        layout.EnsureVisible("console");
        Assert.Equal([DockChangeKind.Structure], kinds);

        layout.SetCollapsed("bottom", true);
        kinds.Clear();
        layout.EnsureVisible("console");
        Assert.False(layout.FindNode("bottom")!.IsCollapsed);
        Assert.Equal([DockChangeKind.Structure], kinds);
    }

    [Fact]
    public void ClosingEveryPanelLeavesAnEmptyRootGroup()
    {
        var layout = new DockLayout(new DockSplit(DockOrientation.Horizontal, new DockGroup("a", "one"), new DockGroup("b", "two")));

        layout.ClosePanel("one");
        layout.ClosePanel("two");

        var root = Assert.IsType<DockGroup>(layout.Root);
        Assert.Empty(root.Panels);
        layout.EnsureVisible("two");
        Assert.Equal(["two"], layout.Panels);
    }

    [Fact]
    public void FloatingAPanelMovesItIntoAWindowOfItsOwn()
    {
        var layout = CreateDefault();

        var window = layout.FloatPanel("console", new PixelPoint(100, 200), new Size(480, 360))!;

        var group = Assert.IsType<DockGroup>(window.Root);
        Assert.Equal(["console"], group.Panels);
        Assert.Same(group, layout.FindPanel("console"));
        Assert.Same(window, layout.FloatOf(group));
        Assert.Null(layout.FloatOf(layout.FindNode("bottom")!));
        Assert.Equal(["assets"], ((DockGroup)layout.FindNode("bottom")!).Panels);
        Assert.Equal(new PixelPoint(100, 200), window.Position);
        Assert.Equal(new Size(480, 360), window.Size);
        Assert.Same(group, layout.FocusedGroup);
        Assert.Contains("console", layout.Panels);
    }

    [Fact]
    public void TheWorkspaceKeepsItsLastPanel()
    {
        var layout = new DockLayout(new DockGroup("only", "scene"));

        Assert.Null(layout.FloatPanel("scene", default, new Size(400, 300)));
        Assert.Null(layout.FloatPanel("missing", default, new Size(400, 300)));
        Assert.Empty(layout.Floats);
    }

    [Fact]
    public void ClosingAFloatingWindowReturnsItsPanelsWhereTheyWere()
    {
        var layout = CreateDefault();
        var window = layout.FloatPanel("console", default, new Size(480, 360))!;
        layout.MovePanel("inspector", window.Root.Id);
        Assert.Null(layout.FindNode("right"));
        var kinds = new List<DockChangeKind>();
        layout.Changed += (_, e) => kinds.Add(e.Kind);

        Assert.True(layout.CloseFloat(window.Id));

        Assert.Empty(layout.Floats);
        Assert.Empty(layout.FloatingPanelHomes);
        Assert.Equal(["assets", "console"], ((DockGroup)layout.FindNode("bottom")!).Panels);
        var right = layout.FindPanel("inspector")!;
        Assert.Equal("right", right.Id);
        Assert.Same(layout.Root, right.Parent);
        Assert.Same(right, ((DockSplit)layout.Root).Children[^1]);
        Assert.Equal("inspector", right.ActivePanel);
        Assert.Equal([DockChangeKind.Structure], kinds);
    }

    [Fact]
    public void APanelDockedIntoAFloatingWindowSplitsItsWindow()
    {
        var layout = CreateDefault();
        var window = layout.FloatPanel("console", default, new Size(480, 360))!;

        layout.DockPanel("assets", window.Root.Id, DockEdge.Bottom);

        var split = Assert.IsType<DockSplit>(window.Root);
        Assert.Equal(DockOrientation.Vertical, split.Orientation);
        Assert.Equal([["console"], ["assets"]], split.Children.Select(c => ((DockGroup)c).Panels));
        Assert.Same(window, layout.FloatOf(layout.FindPanel("assets")!));
        Assert.Equal("bottom", layout.FloatingPanelHomes["assets"].GroupId);
        Assert.Null(layout.FindNode("bottom"));
    }

    [Fact]
    public void APanelMovedBackIntoTheWorkspaceClosesItsEmptyWindow()
    {
        var layout = CreateDefault();
        layout.FloatPanel("console", default, new Size(480, 360));

        layout.MovePanel("console", "right");

        Assert.Empty(layout.Floats);
        Assert.Empty(layout.FloatingPanelHomes);
        Assert.Equal(["inspector", "console"], layout.FindPanel("console")!.Panels);
    }

    [Fact]
    public void ReturnedPanelsGoWhereTheFallbackHasThemWhenTheirPlaceIsGone()
    {
        var layout = CreateDefault();
        layout.Fallback = CreateDefault();
        var window = layout.FloatPanel("inspector", default, new Size(480, 360))!;
        layout.ClosePanel("hierarchy");
        layout.DockPanel("hierarchy", "center", DockEdge.Left);
        layout.FloatPanel("hierarchy", default, new Size(480, 360));

        Assert.True(layout.ReturnPanel("inspector"));

        Assert.DoesNotContain(window, layout.Floats);
        Assert.Equal("right", layout.FindPanel("inspector")!.Id);
        Assert.Null(layout.FloatOf(layout.FindPanel("inspector")!));
        Assert.False(layout.ReturnPanel("inspector"));
    }

    [Fact]
    public void MaximizingAGroupOfAFloatingWindowLeavesTheWorkspaceShown()
    {
        var layout = CreateDefault();
        var window = layout.FloatPanel("console", default, new Size(480, 360))!;
        layout.DockPanel("assets", window.Root.Id, DockEdge.Right);

        layout.ToggleMaximize(layout.FindPanel("console")!.Id);

        Assert.True(layout.IsShown(layout.FindNode("left")!));
        Assert.False(layout.IsShown(layout.FindPanel("assets")!));
    }

    [Fact]
    public void MovingAFloatingWindowOnlyRecordsWhereItIs()
    {
        var layout = CreateDefault();
        var window = layout.FloatPanel("console", default, new Size(480, 360))!;
        var kinds = new List<DockChangeKind>();
        layout.Changed += (_, e) => kinds.Add(e.Kind);

        layout.MoveFloat(window.Id, new PixelPoint(40, 50), new Size(600, 400));
        layout.MoveFloat(window.Id, new PixelPoint(40, 50), new Size(600, 400));

        Assert.Equal(new PixelPoint(40, 50), window.Position);
        Assert.Equal(new Size(600, 400), window.Size);
        Assert.Equal([DockChangeKind.Bounds], kinds);
    }
}
