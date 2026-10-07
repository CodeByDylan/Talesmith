using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Talesmith.Editor.Assets.Creation;

public partial class CreateTileMapView : UserControl
{
    public CreateTileMapView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            Input.Focus();
            Input.SelectAll();
        }, DispatcherPriority.Background);
    }
}
