using Avalonia;
using Avalonia.Controls;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class SplitBarTests
{
    private static (SplitBar Bar, Border First, Border Second) Show(double width, bool firstGrows = false)
    {
        var first = new Border { Width = 200, Height = 24 };
        var second = new Border { MinWidth = 120, Height = 24 };
        var bar = new SplitBar { Width = width, Spacing = 8, RowSpacing = 6, FirstGrows = firstGrows, Children = { first, second } };
        var window = new Window { Content = new StackPanel { Children = { bar } }, Width = 900, Height = 200 };
        window.Show();
        window.UpdateLayout();
        return (bar, first, second);
    }

    [Fact]
    public void BothPartsShareARowWhileTheyFitTheSecondTakingTheRest()
    {
        Headless.Run(() =>
        {
            var (bar, first, second) = Show(500);

            Assert.False(bar.Wraps);
            Assert.Equal(new Rect(0, 0, 200, 24), first.Bounds);
            Assert.Equal(new Rect(208, 0, 292, 24), second.Bounds);
        });
    }

    [Fact]
    public void ThePartsTakeARowEachWhenTheyDoNotFit()
    {
        Headless.Run(() =>
        {
            var (bar, first, second) = Show(300);

            Assert.True(bar.Wraps);
            Assert.Equal(54, bar.Bounds.Height);
            Assert.Equal(0, first.Bounds.Y);
            Assert.Equal(new Rect(0, 30, 300, 24), second.Bounds);
        });
    }

    [Fact]
    public void AGrowingFirstPartNeedsOnlyItsMinimumToShareTheRow()
    {
        Headless.Run(() =>
        {
            var (bar, first, second) = Show(300, firstGrows: true);
            first.Width = double.NaN;
            first.MinWidth = 150;
            bar.UpdateLayout();

            Assert.False(bar.Wraps);
            Assert.Equal(new Rect(0, 0, 172, 24), first.Bounds);
            Assert.Equal(new Rect(180, 0, 120, 24), second.Bounds);
        });
    }
}
