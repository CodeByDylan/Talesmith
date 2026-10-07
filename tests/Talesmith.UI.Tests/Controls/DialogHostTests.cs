using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class DialogHostTests
{
    private static (Window Window, DialogHost Host) Show(double width, double height)
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = width, Height = height };
        window.Show();
        return (window, host);
    }

    private static Rect BoundsIn(Visual root, Visual visual) => new(visual.TranslatePoint(default, root)!.Value, visual.Bounds.Size);

    [Fact]
    public void ADialogLargerThanTheWindowShrinksToFitWithItsFooterInView()
    {
        Headless.Run(() =>
        {
            var (window, host) = Show(640, 420);
            var done = new Button { Content = "Done" };
            var dialog = new Dialog { Header = "Settings", Width = 820, Height = 620, Footer = done, Content = new Border { Height = 200 } };

            _ = host.ShowAsync(new UserControl { Content = dialog });
            window.UpdateLayout();

            var area = new Rect(window.Bounds.Size);
            Assert.True(area.Contains(BoundsIn(window, dialog)));
            Assert.True(area.Contains(BoundsIn(window, done)));
            window.Close();
        });
    }

    [Fact]
    public void ADialogsBodyScrollsWhenItIsTallerThanTheWindow()
    {
        Headless.Run(() =>
        {
            var (window, host) = Show(640, 420);
            var rows = new StackPanel();
            for (var i = 0; i < 30; i++)
                rows.Children.Add(new TextBox { Text = $"Row {i}" });
            var create = new Button { Content = "Create" };
            var dialog = new Dialog { Header = "New thing", Footer = create, Content = rows };

            _ = host.ShowAsync(dialog);
            window.UpdateLayout();

            var scroller = dialog.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
            Assert.True(new Rect(window.Bounds.Size).Contains(BoundsIn(window, create)));
            window.Close();
        });
    }

    [Fact]
    public void ADialogsBodyFillsTheDialogWhenThereIsRoom()
    {
        Headless.Run(() =>
        {
            var (window, host) = Show(1200, 900);
            var list = new Border();
            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { new TextBlock { Text = "Shortcuts" }, list } };
            Grid.SetRow(list, 1);
            var dialog = new Dialog { Header = "Keyboard shortcuts", Width = 760, Height = 620, Padding = default, Content = body };

            _ = host.ShowAsync(dialog);
            window.UpdateLayout();

            Assert.Equal(new Size(760, 620), dialog.Bounds.Size);
            var scroller = dialog.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.Equal(scroller.Viewport.Height, scroller.Extent.Height);
            Assert.True(list.Bounds.Height > 400);
            window.Close();
        });
    }
}
