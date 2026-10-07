using Avalonia.Controls;
using Avalonia.VisualTree;
using Talesmith.UI.Docking;

namespace Talesmith.UI.Tests.Docking;

public sealed class DockTabStripTests
{
    [Fact]
    public void ANarrowGroupKeepsTheActiveTitleWholeAndShowsOtherTabsAsIcons()
    {
        Headless.Run(() =>
        {
            var (window, layout) = Show(240);
            var tabs = Tabs(window);
            var inspector = tabs.Single(t => t.Title == "Inspector");

            Assert.False(inspector.IsCompact);
            Assert.Equal(inspector.FullWidth, inspector.Bounds.Width, 0.5);
            Assert.All(tabs.Where(t => t != inspector), t => Assert.True(t.IsCompact));
            Assert.All(tabs, t => Assert.False(t.IsOverflowed));

            layout.ActivatePanel("history");
            window.UpdateLayout();
            var history = tabs.Single(t => t.Title == "History");
            Assert.False(history.IsCompact);
            Assert.Equal(history.FullWidth, history.Bounds.Width, 0.5);
            Assert.True(inspector.IsCompact);
            window.Close();
        });
    }

    [Fact]
    public void AWideGroupShowsEveryTitle()
    {
        Headless.Run(() =>
        {
            var (window, _) = Show(700);

            Assert.All(Tabs(window), t => Assert.False(t.IsCompact));
            window.Close();
        });
    }

    private static (Window Window, DockLayout Layout) Show(double width)
    {
        var provider = new DockContentProvider
        {
            new DockablePanel("inspector", "Inspector", () => new Border()) { Icon = Icons.Sliders },
            new DockablePanel("history", "History", () => new Border()) { Icon = Icons.Undo },
            new DockablePanel("plugins", "Plugins", () => new Border()) { Icon = Icons.Plug },
        };
        var layout = new DockLayout(new DockGroup("right", "inspector", "history", "plugins"));
        var window = new Window { Content = new DockHost { Layout = layout, ContentProvider = provider }, Width = width, Height = 400 };
        window.Show();
        window.UpdateLayout();
        return (window, layout);
    }

    private static List<DockTab> Tabs(Window window) => [.. window.GetVisualDescendants().OfType<DockTab>()];
}
