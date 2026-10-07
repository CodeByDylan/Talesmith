using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Talesmith.Editor.Shell;

namespace Talesmith.Editor.Tests.Shell;

public sealed class AppBarTests
{
    [Theory]
    [InlineData(1600, "")]
    [InlineData(1024, "compact,narrow")]
    [InlineData(700, "compact,narrow,collapsed")]
    public void EveryButtonOfTheAppBarStaysInTheWindow(double width, string form) => Headless.Run(async () =>
    {
        await using var fixture = await EditorFixture.OpenAsync();
        var shell = new ShellView { DataContext = fixture.Get<ShellViewModel>() };
        var stage = new Panel { Width = width, Height = 720, HorizontalAlignment = HorizontalAlignment.Left, Children = { shell } };
        var window = new Window { Width = 1700, Height = 800, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var bar = shell.GetVisualDescendants().OfType<AppBarPanel>().Single();
            Assert.Equal(form, string.Join(",", bar.Classes.Where(c => !c.StartsWith(':'))));

            var buttons = bar.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            Assert.Contains(buttons, b => ToolTip.GetTip(b) as string == "Settings (Ctrl+,)");
            foreach (var button in buttons)
            {
                var bounds = new Rect(button.TranslatePoint(default, stage)!.Value, button.Bounds.Size);
                Assert.True(bounds.Right <= width, $"{ToolTip.GetTip(button)} ends at {bounds.Right}, past {width}");
            }

            var menu = shell.FindControl<Menu>(form.Contains("collapsed", StringComparison.Ordinal) ? "CompactMenu" : "MainMenu")!;
            Assert.True(menu.IsEffectivelyVisible);
            Assert.True(new Rect(menu.TranslatePoint(default, stage)!.Value, menu.Bounds.Size).Right <= width);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TheMenuButtonOfANarrowBarHoldsEveryMenu() => Headless.Run(async () =>
    {
        await using var fixture = await EditorFixture.OpenAsync();
        var model = fixture.Get<ShellViewModel>();
        var shell = new ShellView { DataContext = model };
        var stage = new Panel { Width = 700, Height = 720, HorizontalAlignment = HorizontalAlignment.Left, Children = { shell } };
        var window = new Window { Width = 1700, Height = 800, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var button = shell.FindControl<Menu>("CompactMenu")!.GetLogicalChildren().OfType<MenuItem>().Single();
            button.IsSubMenuOpen = true;
            window.UpdateLayout();

            var menus = button.GetRealizedContainers().OfType<MenuItem>().Select(m => m.Header as string).ToList();
            Assert.Equal(model.Menu.Select(m => m.Header), menus);
            Assert.Contains("File", menus);
        }
        finally
        {
            window.Close();
        }
    });
}
