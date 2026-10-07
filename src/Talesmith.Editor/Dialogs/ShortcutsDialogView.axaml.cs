using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Talesmith.Editor.Dialogs;

public partial class ShortcutsDialogView : UserControl
{
    public ShortcutsDialogView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Background);
    }
}
