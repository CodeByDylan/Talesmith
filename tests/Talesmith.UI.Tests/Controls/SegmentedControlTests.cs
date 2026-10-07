using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class SegmentedControlTests
{
    private static (Border Stage, SegmentedControl Control) Show(double width, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var control = new SegmentedControl { ItemsSource = new[] { "Clamp", "Repeat", "Mirror" }, SelectedIndex = 0, HorizontalAlignment = alignment };
        var stage = new Border { Width = width, Child = control };
        var window = new Window { Content = new StackPanel { Children = { stage } }, Width = 900, Height = 300 };
        window.Show();
        window.UpdateLayout();
        return (stage, control);
    }

    private static List<Rect> Segments(SegmentedControl control) =>
        [.. Enumerable.Range(0, control.ItemCount).Select(i => control.ContainerFromIndex(i)!.Bounds)];

    [Fact]
    public void SegmentsOfEqualWidthShareARowWhileTheyFit()
    {
        Headless.Run(() =>
        {
            var (_, control) = Show(400);
            var segments = Segments(control);

            Assert.All(segments, s => Assert.Equal(0, s.Y));
            Assert.All(segments, s => Assert.Equal(segments[0].Width, s.Width, 3));
            Assert.Equal(control.Bounds.Width, segments.Sum(s => s.Width) + 6, 3);
        });
    }

    [Fact]
    public void SegmentsContinueOnAnotherRowInsteadOfCuttingOffTheirContent()
    {
        Headless.Run(() =>
        {
            var (stage, control) = Show(400);
            var natural = Segments(control)[0].Width;

            stage.Width = 150;
            stage.UpdateLayout();
            var segments = Segments(control);

            Assert.All(segments, s => Assert.True(s.Width >= natural, $"A segment is {s.Width} wide, less than the {natural} its content needs."));
            Assert.Equal(2, segments.Select(s => s.Y).Distinct().Count());
            Assert.True(control.Bounds.Width <= 150);
            Assert.All(segments, s => Assert.True(s.Right <= control.Bounds.Width - 3 + 0.001));
        });
    }

    [Fact]
    public void AStretchedControlSharesItsWidthOutAmongTheSegments()
    {
        Headless.Run(() =>
        {
            var (_, control) = Show(400, HorizontalAlignment.Stretch);
            var segments = Segments(control);

            Assert.All(segments, s => Assert.Equal(0, s.Y));
            Assert.All(segments, s => Assert.Equal((400 - 6) / 3.0, s.Width, 1.0));
        });
    }
}
