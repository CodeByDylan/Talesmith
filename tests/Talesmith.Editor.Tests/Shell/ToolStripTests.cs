using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Tests.TileMaps;
using Talesmith.Editor.TileMaps.Tools;

namespace Talesmith.Editor.Tests.Shell;

public sealed class ToolStripTests
{
    [Theory]
    [InlineData(1600, true, false, false)]
    [InlineData(1024, true, true, false)]
    [InlineData(960, false, false, true)]
    [InlineData(770, false, true, true)]
    public void EveryOptionStaysInTheBarWhoseViewOptionsShrinkOrMoveToASecondRow(double width, bool brush, bool wraps, bool compact) => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var shell = new ShellView { DataContext = harness.Fixture.Get<ShellViewModel>() };
        harness.Tools.ActiveTool = brush ? harness.Tool<BrushTool>() : harness.Tools.Tools.OfType<Talesmith.Editor.Viewport.Tools.SelectTool>().Single();
        var stage = new Panel { Width = width, Height = 720, HorizontalAlignment = HorizontalAlignment.Left, Children = { shell } };
        var window = new Window { Width = 1700, Height = 800, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var strip = shell.GetVisualDescendants().OfType<ToolStripPanel>().Single();
            Assert.Equal(wraps, strip.Wraps);
            Assert.Equal(compact, strip.Classes.Contains("compact"));
            Assert.Equal(wraps ? 2 * strip.RowHeight : strip.RowHeight, strip.Bounds.Height);

            var pickers = strip.GetVisualDescendants().OfType<ComboBox>().ToList();
            Assert.Equal(2, pickers.Count);
            foreach (var control in strip.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c is Button or ComboBox))
                Assert.True(new Rect(control.TranslatePoint(default, stage)!.Value, control.Bounds.Size).Right <= width);
        }
        finally
        {
            window.Close();
        }
    });
}
