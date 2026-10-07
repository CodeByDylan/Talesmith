using Avalonia.Controls;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class CurveCanvasTests
{
    [Fact]
    public void TheContextMenuShowsItsCommands()
    {
        Headless.Run(() =>
        {
            var canvas = new CurveCanvas();
            var window = new Window { Content = canvas, Width = 320, Height = 200 };
            window.Show();
            var menu = Assert.IsType<MenuFlyout>(canvas.ContextFlyout);

            menu.ShowAt(canvas);
            var presenter = Assert.IsAssignableFrom<ItemsControl>(menu.Popup.Child);
            presenter.UpdateLayout();

            Assert.Contains(presenter.GetRealizedContainers(), c => c is MenuItem { Header: "Add key" });
            window.Close();
        });
    }
}
