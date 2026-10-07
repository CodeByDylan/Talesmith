using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Shell;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;

namespace Talesmith.Editor.Tests.TileMaps;

public sealed class TileGridTests
{
    [Fact]
    public void TileToolsShowTheMapsCellsAsTheGridShownOrHiddenOnItsOwn() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("hex-adventure");
        _ = harness.Fixture.Get<ShellViewModel>();
        var grid = harness.Fixture.Get<ViewportGrid>();
        var options = harness.Fixture.Get<ViewportOptions>();
        harness.Tools.ActiveTool = harness.Tool<SelectTool>();
        Assert.Null(grid.ToolGrid);
        Assert.Same(Icons.Grid, grid.Icon);

        harness.Tools.ActiveTool = harness.Tool<BrushTool>();
        Assert.Same(harness.Tool<BrushTool>(), grid.ToolGrid);
        Assert.Same(Icons.Hexagon, grid.Icon);
        Assert.StartsWith("Tile grid", grid.ToolTip, StringComparison.Ordinal);
        Assert.True(grid.IsShown);

        grid.Toggle();
        Assert.False(options.ShowTileGrid);
        Assert.True(options.ShowGrid);

        harness.Tools.ActiveTool = harness.Tool<SelectTool>();
        Assert.True(grid.IsShown);
        grid.Toggle();
        Assert.False(options.ShowGrid);
        harness.Tools.ActiveTool = harness.Tool<EraserTool>();
        Assert.False(grid.IsShown);
    });

    [Theory]
    [InlineData("hex-adventure")]
    [InlineData("platformer")]
    public void TheTileGridOutlinesTheCellsInTheirShape(string template) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync(template);
        _ = harness.Fixture.Get<ShellViewModel>();
        var fixture = harness.Fixture;
        var viewport = fixture.Get<ViewportService>();
        fixture.Get<ViewportOptions>().ShowIcons = false;
        var overlay = new ViewportOverlay(viewport, harness.Tools, fixture.Get<ISelectionService>(), fixture.Get<ISceneDocumentService>());
        var window = new Window { Width = 480, Height = 360, Content = overlay };
        window.Show();
        try
        {
            var layout = harness.Map.Layout;
            var cell = new GridCoord(2, 2);
            var center = harness.Editor.Origin + layout.CellToWorld(cell);
            viewport.Camera.Set(center, 64 / Math.Min(layout.CellSize.X, layout.CellSize.Y));
            harness.Tools.ActiveTool = harness.Tool<BrushTool>();
            var edge = harness.Editor.Origin + layout.CellToWorld(cell) + (layout.CornerOffset(0) + layout.CornerOffset(1)) / 2;

            using (var shown = Render(window))
                Assert.True(IsDrawnNear(shown, viewport.Camera.WorldToScreen(edge), Pixel(shown, viewport.Camera.WorldToScreen(center))));

            fixture.Get<ViewportOptions>().ShowTileGrid = false;
            using (var hidden = Render(window))
                Assert.False(IsDrawnNear(hidden, viewport.Camera.WorldToScreen(edge), Pixel(hidden, viewport.Camera.WorldToScreen(center))));
        }
        finally
        {
            window.Close();
        }
    });

    private static WriteableBitmap Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return window.CaptureRenderedFrame()!;
    }

    /// <summary>Whether a pixel next to <paramref name="point"/> differs from the background, as lines snapped to whole pixels can fall either
    /// side of it.</summary>
    private static bool IsDrawnNear(WriteableBitmap bitmap, Point point, uint background)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (Pixel(bitmap, new Point(point.X + dx, point.Y + dy)) != background)
                    return true;
            }
        }

        return false;
    }

    private static uint Pixel(WriteableBitmap bitmap, Point point)
    {
        using var buffer = bitmap.Lock();
        var x = (int)Math.Floor(point.X);
        var y = (int)Math.Floor(point.Y);
        return (uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
    }
}
