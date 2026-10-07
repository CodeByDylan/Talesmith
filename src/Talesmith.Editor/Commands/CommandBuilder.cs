using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace Talesmith.Editor.Commands;

/// <summary>Collects the commands, menu entries and toolbar buttons of <see cref="IEditorCommandContributor"/>s.</summary>
/// <remarks>Menu paths start with a top-level menu from <see cref="MenuPaths"/> and name submenus after slashes, such as
/// <c>"GameObject/2D Object"</c>. Entries with the same group are kept together, and groups are separated by lines. What a contributor adds
/// reaches the registry only when it finishes without throwing.</remarks>
public sealed class CommandBuilder(EditorCommandRegistry registry)
{
    private readonly List<EditorCommand> _commands = [];
    private readonly List<MenuEntry> _menu = [];
    private readonly List<ToolbarEntry> _toolbar = [];
    private readonly List<string> _notInText = [];

    /// <summary>Adds a command; its shortcut is replaced by the user's override, if any.</summary>
    /// <exception cref="InvalidOperationException">A command with the same id is registered.</exception>
    public EditorCommand Add(EditorCommand command)
    {
        command = registry.Prepare(command);
        if (_commands.Exists(c => c.Id == command.Id))
            throw EditorCommandRegistry.DuplicateId(command.Id);
        _commands.Add(command);
        return registry.WithBinding(command);
    }

    /// <summary>Adds a command that runs an action.</summary>
    /// <param name="gesture">A shortcut such as "Ctrl+Shift+N", or null.</param>
    public EditorCommand Add(string id, string title, string category, Action execute, Func<bool>? canExecute = null, string? gesture = null,
        Geometry? icon = null, string? description = null) =>
        Add(id, title, category, canExecute is null ? new RelayCommand(execute) : new RelayCommand(execute, canExecute), gesture, icon, description);

    /// <summary>Adds a command that runs asynchronous work; it cannot run again until the work finished.</summary>
    public EditorCommand Add(string id, string title, string category, Func<Task> execute, Func<bool>? canExecute = null, string? gesture = null,
        Geometry? icon = null, string? description = null) =>
        Add(id, title, category, canExecute is null ? new AsyncRelayCommand(execute) : new AsyncRelayCommand(execute, canExecute), gesture, icon, description);

    public EditorCommand Add(string id, string title, string category, ICommand command, string? gesture = null, Geometry? icon = null, string? description = null)
    {
        var parsed = KeyGestures.Parse(gesture);
        return Add(new EditorCommand(id, title, category, command, parsed, icon) { DefaultGesture = parsed, Description = description });
    }

    /// <summary>Shows a command in a menu.</summary>
    /// <param name="order">The position within the group; lower comes first.</param>
    public void Menu(string path, string commandId, string group = "", int order = 0) =>
        _menu.Add(new MenuEntry(path, commandId, null, group, order));

    /// <summary>Shows a custom item, such as a submenu filled at run time, in a menu.</summary>
    public void Menu(string path, MenuItemViewModel item, string group = "", int order = 0) =>
        _menu.Add(new MenuEntry(path, null, item, group, order));

    /// <summary>Keeps the shortcuts of commands that act on the selection, such as Duplicate, from running while a text field has focus.</summary>
    public void KeepOutOfText(params string[] ids) => _notInText.AddRange(ids);

    /// <summary>Shows a command as an icon button in the app bar, left of the command search.</summary>
    public void Toolbar(string commandId, int order = 0) => _toolbar.Add(new ToolbarEntry(commandId, order));

    internal void Commit(Func<EditorCommand, EditorCommand>? adapt)
    {
        foreach (var command in _commands)
            registry.Add(adapt is null ? command : adapt(command));
        foreach (var entry in _menu)
            registry.AddMenuEntry(entry);
        foreach (var entry in _toolbar)
            registry.AddToolbarEntry(entry);
        registry.KeepOutOfText(_notInText);
    }
}

/// <summary>The top-level menus of the main menu, in order.</summary>
public static class MenuPaths
{
    public const string File = "File";
    public const string Edit = "Edit";
    public const string Scene = "Scene";
    public const string Assets = "Assets";
    public const string GameObject = "GameObject";
    public const string Component = "Component";
    public const string Tools = "Tools";
    public const string Plugins = "Plugins";
    public const string Window = "Window";
    public const string Help = "Help";

    public static IReadOnlyList<string> TopLevel { get; } = [File, Edit, Scene, Assets, GameObject, Component, Tools, Plugins, Window, Help];
}

/// <summary>A command or custom item placed in a menu.</summary>
public sealed record MenuEntry(string Path, string? CommandId, MenuItemViewModel? Item, string Group, int Order);

/// <summary>A command shown as a button in the app bar.</summary>
public sealed record ToolbarEntry(string CommandId, int Order);
