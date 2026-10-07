using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Talesmith.Editor.TileMaps.Controls;
using Talesmith.Editor.TileMaps.Panel;

namespace Talesmith.Editor.Tests.TileMaps;

public sealed class TileMapPanelLayoutTests
{
    [Theory]
    [InlineData(340, true)]
    [InlineData(900, false)]
    public void ThePaletteStaysWithinReachInAShortPanel(double height, bool scrolls) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("hex-adventure");
        var view = (TileMapPanelView)harness.Fixture.Get<TileMapPanel>().CreateContent();
        var stage = new Panel { Width = 280, Height = height, Children = { view } };
        var window = new Window { Width = 400, Height = 1000, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var scroller = view.FindControl<ScrollViewer>("BodyScroll")!;
            var palette = view.GetVisualDescendants().OfType<TilePalette>().Single().FindAncestorOfType<ScrollViewer>()!;

            Assert.Equal(scrolls, scroller.Extent.Height > scroller.Viewport.Height);
            Assert.True(palette.Bounds.Height >= 100, $"The palette is {palette.Bounds.Height} pixels tall");
            scroller.Offset = new Vector(0, scroller.Extent.Height);
            window.UpdateLayout();
            var top = palette.TranslatePoint(default, scroller)!.Value.Y;
            Assert.True(top >= 0 && top + palette.Bounds.Height <= scroller.Viewport.Height + 1, $"The palette starts at {top} of {scroller.Viewport.Height}");
        }
        finally
        {
            window.Close();
        }
    });
}
