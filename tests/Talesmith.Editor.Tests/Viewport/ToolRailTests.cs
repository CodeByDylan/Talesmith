using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Tests.TileMaps;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Tests.Viewport;

public sealed class ToolRailTests
{
    [Fact]
    public void ToolsContinueInAnotherColumnWhenTheViewportIsShort() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var (window, rail, stage) = Show(harness, 600, 2000);
        try
        {
            var tools = VisibleTools(harness.Tools);
            Assert.Single(tools.Select(t => rail.ButtonOf(t)!.Bounds.X).Distinct());

            stage.Height = 6 * Button(rail).Height;
            window.UpdateLayout();

            Assert.True(tools.Select(t => rail.ButtonOf(t)!.Bounds.X).Distinct().Count() > 1);
            Assert.All(tools, t => Assert.True(IsPlaced(rail, rail.ButtonOf(t)!, stage)));
            Assert.Empty(rail.Overflow);
            Assert.False(IsPlaced(rail, rail.MoreButton, stage));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ToolsWithoutRoomAreUnderMoreWhichShowsTheActiveOne() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var (window, rail, stage) = Show(harness, 600, 2000);
        try
        {
            var button = Button(rail);
            stage.Width = 2 * button.Width + 6;
            stage.Height = 4 * button.Height + 10;
            window.UpdateLayout();

            var tools = VisibleTools(harness.Tools);
            var placed = tools.Where(t => IsPlaced(rail, rail.ButtonOf(t)!, stage)).ToList();
            Assert.NotEmpty(rail.Overflow);
            Assert.True(IsPlaced(rail, rail.MoreButton, stage));
            Assert.Equal(tools, placed.Concat(rail.Overflow).OrderBy(tools.IndexOf));
            Assert.All(rail.Overflow, t => Assert.False(IsPlaced(rail, rail.ButtonOf(t)!, stage)));

            var hidden = rail.Overflow[^1];
            hidden.IsActive = true;
            Assert.True(rail.MoreButton.IsChecked);
            placed[0].IsActive = true;
            Assert.False(rail.MoreButton.IsChecked);

            var menu = rail.CreateMoreMenu();
            var items = menu.Items.OfType<MenuItem>().ToList();
            Assert.Equal(rail.Overflow.Select(t => t.Tool.Name), items.Select(i => i.Header));
            items[^1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(hidden.IsActive);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TheRailFollowsWhichToolGroupsAreAvailable() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var (window, rail, stage) = Show(harness, 600, 2000);
        try
        {
            var tile = harness.Tools.Groups.Single(g => g.Name == "Tile");
            Assert.True(IsPlaced(rail, rail.ButtonOf(tile.Tools[0])!, stage));

            harness.Editor.Close();
            window.UpdateLayout();

            Assert.False(tile.IsVisible);
            Assert.All(tile.Tools, t => Assert.False(IsPlaced(rail, rail.ButtonOf(t)!, stage)));
            Assert.All(VisibleTools(harness.Tools), t => Assert.True(IsPlaced(rail, rail.ButtonOf(t)!, stage)));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Shows the harness's tools in a rail on a stage of the given size, with the editor's commands, which keep the tools' availability
    /// up to date.</summary>
    private static (Window Window, ToolRail Rail, Panel Stage) Show(TileMapHarness harness, double width, double height)
    {
        _ = harness.Fixture.Get<ShellViewModel>();
        var rail = new ToolRail { Manager = harness.Tools, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var stage = new Panel { Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { rail } };
        var window = new Window { Width = 800, Height = 2100, Content = stage };
        window.Show();
        window.UpdateLayout();
        return (window, rail, stage);
    }

    private static List<ToolItemViewModel> VisibleTools(ToolManager tools) => [.. tools.Groups.Where(g => g.IsVisible).SelectMany(g => g.Tools)];

    private static Size Button(ToolRail rail) => rail.ButtonOf(rail.Manager!.Groups[0].Tools[0])!.Bounds.Size;

    /// <summary>Whether a button lies inside the rail, which clips the others away, and the rail inside the stage, so it can be seen and
    /// clicked.</summary>
    private static bool IsPlaced(ToolRail rail, Control button, Panel stage) =>
        button.Bounds is { Width: > 0, Height: > 0 } bounds && new Rect(rail.Bounds.Size).Contains(bounds) && rail.Bounds.Bottom <= stage.Height + 0.5;
}
