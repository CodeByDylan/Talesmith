using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Talesmith.Editor.Dialogs;

public partial class SettingsDialogView : UserControl
{
    public SettingsDialogView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => SectionList.Focus(), DispatcherPriority.Background);
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsDialogViewModel settings || settings.Bindings.FirstOrDefault(b => b.IsRecording) is not { } recording)
            return;
        if (recording.Capture(e.Key, e.KeyModifiers))
            e.Handled = true;
    }
}
