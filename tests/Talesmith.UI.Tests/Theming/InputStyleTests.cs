using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Talesmith.UI.Tests.Theming;

public sealed class InputStyleTests
{
    [Fact]
    public void ADropdownShortensASelectionThatDoesNotFitWithAnEllipsis()
    {
        Headless.Run(() =>
        {
            var box = new ComboBox { ItemsSource = new List<string> { "Nearest (pixel art)" }, SelectedIndex = 0, Width = 100 };
            var window = new Window { Content = new StackPanel { Children = { box } }, Width = 400, Height = 200 };
            window.Show();
            window.UpdateLayout();

            var selection = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Nearest (pixel art)");
            Assert.Equal(TextTrimming.CharacterEllipsis, selection.TextTrimming);
            Assert.True(selection.TextLayout.TextLines[0].HasCollapsed);
        });
    }
}
