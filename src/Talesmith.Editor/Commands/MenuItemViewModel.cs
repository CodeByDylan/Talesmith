using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Media;

namespace Talesmith.Editor.Commands;

/// <summary>An entry in the main menu or a context menu.</summary>
public sealed class MenuItemViewModel
{
    /// <summary>The header text; "-" renders a separator.</summary>
    public required string Header { get; init; }

    public ICommand? Command { get; init; }

    public object? CommandParameter { get; init; }

    public KeyGesture? Gesture { get; init; }

    public Geometry? Icon { get; init; }

    public string? ToolTip { get; init; }

    public ObservableCollection<MenuItemViewModel> Items { get; init; } = [];

    public bool IsSeparator => Header == "-";

    public static MenuItemViewModel Separator => new() { Header = "-" };

    /// <summary>Creates an entry for a registered command.</summary>
    public static MenuItemViewModel For(EditorCommand command) => new()
    {
        Header = command.Title,
        Command = command.Command,
        Gesture = command.Gesture,
        Icon = command.Icon,
        ToolTip = command.Description
    };
}
