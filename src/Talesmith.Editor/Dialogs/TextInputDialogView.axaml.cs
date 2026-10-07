using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Talesmith.Editor.Dialogs;

public partial class TextInputDialogView : UserControl
{
    public TextInputDialogView() => InitializeComponent();

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
