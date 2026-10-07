using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Talesmith.Avalonia.Input;
using AvaloniaKey = Avalonia.Input.Key;

namespace Talesmith.Editor.ProjectSettings;

public partial class ProjectSettingsView : UserControl
{
    private static readonly AvaloniaKey[] ModifierKeys =
        [AvaloniaKey.LeftCtrl, AvaloniaKey.RightCtrl, AvaloniaKey.LeftShift, AvaloniaKey.RightShift, AvaloniaKey.LeftAlt, AvaloniaKey.RightAlt, AvaloniaKey.LWin, AvaloniaKey.RWin];

    private AvaloniaKey? _pendingModifier;

    public ProjectSettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    private InputSettingsPage? Input => (DataContext as ProjectSettingsViewModel)?.Input;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Input is not { Recording: not null } input)
            return;
        e.Handled = true;
        if (ModifierKeys.Contains(e.Key))
        {
            _pendingModifier = e.Key;
            return;
        }

        _pendingModifier = null;
        input.Record(KeyMapping.ToKey(e.Key), KeyMapping.ToModifiers(e.KeyModifiers));
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (Input is not { Recording: not null } input || _pendingModifier != e.Key)
            return;
        e.Handled = true;
        _pendingModifier = null;
        input.Record(KeyMapping.ToKey(e.Key), Talesmith.Input.KeyModifiers.None);
    }
}
