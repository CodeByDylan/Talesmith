using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Talesmith.Editor.Scripting;

public partial class CreateScriptDialogView : UserControl
{
    public CreateScriptDialogView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, DispatcherPriority.Background);
    }
}
