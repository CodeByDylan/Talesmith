using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Talesmith.Avalonia.Presentation;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Dialogs;
using Talesmith.UI.Docking;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Shell;

/// <summary>The main editor window: hosts the shell, dialogs and notifications, routes keyboard shortcuts, also in the floating panel windows,
/// and asks before closing with unsaved changes.</summary>
public partial class EditorWindow : Window
{
    private readonly ShellViewModel? _shell;
    private bool _closeApproved;

    public EditorWindow(ShellViewModel shell, WindowHost host)
    {
        _shell = shell;
        InitializeComponent();
        DataContext = shell;
        DataTemplates.AddRange(DialogTemplates.Load());
        host.Attach(this, Dialogs, Toasts);
        shell.CloseRequested += (_, _) => Close();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Bubble);
        AddHandler(DockHost.WindowOpenedEvent, OnDockWindowOpened);
    }

    /// <summary>Parameterless constructor for the XAML previewer.</summary>
    public EditorWindow() => InitializeComponent();

    /// <summary>Whether the window may close without asking, such as when the editor switches projects after asking already.</summary>
    public bool CloseApproved
    {
        get => _closeApproved;
        set => _closeApproved = value;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_shell is null || e.Cancel || _closeApproved)
            return;
        e.Cancel = true;
        _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        if (!await _shell!.CanCloseAsync())
            return;
        _closeApproved = true;
        Close();
    }

    /// <summary>Gives a floating panel window the editor's icon and shortcuts.</summary>
    private void OnDockWindowOpened(object? sender, DockWindowOpenedEventArgs e)
    {
        e.Window.Icon = Icon;
        e.Window.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        e.Window.AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>Lets editor shortcuts with Ctrl, Alt or a function key through while a game has keyboard focus, before the game takes the keys.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shell is null || Dialogs.IsOpen || FocusOf(sender) is not GameView)
            return;
        if (_shell.Commands.FindByGesture(e) is { } command && command.Gesture is { } gesture && KeyGestures.IsGlobal(gesture))
        {
            command.TryExecuteShortcut();
            e.Handled = true;
        }
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _shell is null || Dialogs.IsOpen)
            return;
        if (_shell.Commands.FindByGesture(e) is not { } command)
            return;
        if (IsTextInputFocused(FocusOf(sender)) && !RunsInText(e, command))
            return;
        command.TryExecuteShortcut();
        e.Handled = true;
    }

    /// <summary>The element with keyboard focus in the window a key went to: this one, or a floating panel window.</summary>
    private static IInputElement? FocusOf(object? window) => (window as TopLevel)?.FocusManager?.GetFocusedElement();

    private bool RunsInText(KeyEventArgs e, EditorCommand command) =>
        (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) != 0 && _shell!.Commands.WorksInText(command);

    /// <summary>Whether keys go to text editing: a text box, or a control inside one such as a number field's.</summary>
    internal static bool IsTextInputFocused(IInputElement? focused) =>
        focused is global::Avalonia.Visual visual && (visual is TextBox || visual.FindAncestorOfType<TextBox>() is not null);
}
