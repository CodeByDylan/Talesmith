using Avalonia.Controls;
using Avalonia.VisualTree;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class PropertyRowTests
{
    [Fact]
    public void RowsInheritTheLabelWidthOfTheirContainer()
    {
        Headless.Run(() =>
        {
            var first = new PropertyRow { Label = "Position" };
            var second = new PropertyRow { Label = "Rotation" };
            var stack = new StackPanel { Children = { first, second } };
            PropertyGrid.SetLabelWidth(stack, 140);

            Assert.Equal(140, first.LabelWidth);
            Assert.Equal(140, second.LabelWidth);
        });
    }

    [Fact]
    public void ModifiedRowShowsItsMarkerOutsideTheRow()
    {
        Headless.Run(() =>
        {
            var row = new PropertyRow { Label = "Position", IsModified = true, Content = new TextBlock { Text = "412" } };
            var window = new Window { Content = new StackPanel { Margin = new Avalonia.Thickness(20), Children = { row } }, Width = 300, Height = 100 };
            window.Show();
            window.UpdateLayout();

            var marker = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Marker");
            Assert.True(marker.IsEffectivelyVisible);
            Assert.DoesNotContain(marker.GetVisualAncestors().TakeWhile(a => a != window), a => a.ClipToBounds);
            window.Close();
        });
    }

    [Theory]
    [InlineData(400, 120)]
    [InlineData(220, 84)]
    [InlineData(150, 48)]
    public void ANarrowRowNarrowsItsLabelColumnSoTheValueKeepsRoom(double width, double label)
    {
        Headless.Run(() =>
        {
            var value = new Border();
            var row = new PropertyRow { Label = "Ambient light", Content = value };
            var stack = new StackPanel { Width = width, Children = { row } };
            PropertyGrid.SetLabelWidth(stack, 120);
            var window = new Window { Content = stack, Width = 500, Height = 100 };
            window.Show();
            window.UpdateLayout();

            Assert.Equal(label, row.LabelColumnWidth);
            Assert.Equal(width - label, value.Bounds.Width);
            window.Close();
        });
    }
}
