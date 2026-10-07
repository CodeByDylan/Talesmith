using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Media;

namespace Talesmith.Editor.Commands;

/// <summary>A user-invocable editor action with its menu metadata and keyboard shortcut.</summary>
/// <param name="Id">A stable identifier such as "scene.save", also used for keybinding overrides.</param>
/// <param name="Title">The user-facing name.</param>
/// <param name="Category">The group shown in the command palette and the shortcut list.</param>
/// <param name="Command">The action.</param>
/// <param name="Gesture">The keyboard shortcut in effect, if any.</param>
/// <param name="Icon">A 24×24 stroke icon geometry, if any.</param>
public sealed record EditorCommand(string Id, string Title, string Category, ICommand Command, KeyGesture? Gesture = null, Geometry? Icon = null)
{
    /// <summary>A second shortcut for the same action, such as Ctrl+Y for redo.</summary>
    public KeyGesture? AlternateGesture { get; init; }

    /// <summary>A one-line explanation shown in tooltips and the command palette.</summary>
    public string? Description { get; init; }

    /// <summary>The shortcut the command was registered with, before user overrides.</summary>
    public KeyGesture? DefaultGesture { get; init; }

    /// <summary>Whether the command appears in the command palette.</summary>
    public bool ShowInPalette { get; init; } = true;

    /// <summary>The shortcut formatted for display, such as "Ctrl+S".</summary>
    public string? GestureText => Gesture is null ? null : KeyGestures.Format(Gesture);

    /// <summary>A shortcut the command shares with commands of other contexts, such as tools; the command with <see cref="Gesture"/> runs it.</summary>
    public KeyGesture? SharedGesture { get; init; }

    /// <summary>What the shortcut runs when it differs from the command, such as picking among tools of different contexts that share a key.</summary>
    public Func<bool>? ShortcutAction { get; init; }

    public bool CanExecute => Command.CanExecute(null);

    /// <summary>Runs the command when it can run; returns whether it ran.</summary>
    public bool TryExecute()
    {
        if (!Command.CanExecute(null))
            return false;
        Command.Execute(null);
        return true;
    }

    /// <summary>Runs what the command's shortcut does: <see cref="ShortcutAction"/>, or the command itself.</summary>
    public bool TryExecuteShortcut() => ShortcutAction?.Invoke() ?? TryExecute();
}
