using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Tests.TileMaps;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Tests.Viewport;

public sealed class ViewportChromeTests
{
    [Fact]
    public void EveryToolStaysWithinReachInATinyViewport() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        _ = harness.Fixture.Get<ShellViewModel>();
        var view = harness.Fixture.Get<SceneViewportPanel>().CreateContent();
        var stage = new Panel { Width = 900, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { view } };
        var window = new Window { Width = 1000, Height = 800, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var rail = view.FindControl<Border>("RailHost")!;
            var zoom = view.FindControl<Border>("ZoomBar")!;
            var tools = rail.GetVisualDescendants().OfType<ToolRail>().Single();
            Assert.DoesNotContain("compact", zoom.Classes);
            Assert.Empty(tools.Overflow);

            stage.Width = 130;
            stage.Height = 170;
            window.UpdateLayout();

            Assert.False(rail.Bounds.Intersects(zoom.Bounds));
            Assert.True(new Rect(view.Bounds.Size).Contains(rail.Bounds));
            Assert.True(new Rect(view.Bounds.Size).Contains(zoom.Bounds));
            Assert.Contains("compact", zoom.Classes);
            Assert.NotEmpty(tools.Overflow);
            Assert.True(tools.MoreButton.Bounds.Width > 0 && new Rect(tools.Bounds.Size).Contains(tools.MoreButton.Bounds));

            stage.Width = 900;
            stage.Height = 700;
            window.UpdateLayout();
            Assert.DoesNotContain("compact", zoom.Classes);
            Assert.Empty(tools.Overflow);
        }
        finally
        {
            window.Close();
        }
    });
}
