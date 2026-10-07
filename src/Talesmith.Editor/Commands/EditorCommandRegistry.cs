using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Settings;

namespace Talesmith.Editor.Commands;

/// <summary>The commands available in the editor, looked up by id or shortcut, with their menu and toolbar placements.</summary>
/// <remarks>Shortcuts follow the user's overrides in <see cref="EditorSettings.KeyBindings"/>, also when they change while the editor runs.</remarks>
public sealed class EditorCommandRegistry : IDisposable
{
    private readonly ISettingsService _settings;
    private readonly List<EditorCommand> _commands = [];
    private readonly List<EditorCommand> _registered = [];
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly List<MenuEntry> _menu = [];
    private readonly List<ToolbarEntry> _toolbar = [];
    private readonly HashSet<string> _notInText = new(StringComparer.Ordinal);
    private Dictionary<string, string> _appliedBindings;

    public EditorCommandRegistry(ISettingsService settings)
    {
        _settings = settings;
        _appliedBindings = new Dictionary<string, string>(settings.Current.KeyBindings);
        settings.Changed += OnSettingsChanged;
    }

    public IReadOnlyList<EditorCommand> Commands => _commands;

    public IReadOnlyList<MenuEntry> MenuEntries => _menu;

    public IReadOnlyList<ToolbarEntry> ToolbarEntries => _toolbar;

    /// <summary>Raised when commands are added or their shortcuts change.</summary>
    public event EventHandler? Changed;

    /// <summary>Lets each contributor add its commands, menu entries and toolbar buttons.</summary>
    public void Load(IEnumerable<IEditorCommandContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        foreach (var contributor in contributors)
            Load(contributor);
    }

    /// <summary>Lets a contributor add its commands, menu entries and toolbar buttons; when it throws, nothing it added is kept.</summary>
    /// <param name="adapt">Changes each of the contributor's commands before it is registered, or null.</param>
    public void Load(IEditorCommandContributor contributor, Func<EditorCommand, EditorCommand>? adapt = null)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        var builder = new CommandBuilder(this);
        contributor.Contribute(builder);
        builder.Commit(adapt);
    }

    /// <exception cref="InvalidOperationException">A command with the same id is registered.</exception>
    public EditorCommand Add(EditorCommand command)
    {
        command = Prepare(command);
        _index[command.Id] = _commands.Count;
        _registered.Add(command);
        command = WithBinding(command, _appliedBindings);
        _commands.Add(command);
        Changed?.Invoke(this, EventArgs.Empty);
        return command;
    }

    /// <summary>Stops following the settings, which outlive the project and would otherwise keep the editor's commands alive.</summary>
    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    /// <summary>Checks that a command can be added and fills in its default shortcut.</summary>
    /// <exception cref="InvalidOperationException">A command with the same id is registered.</exception>
    internal EditorCommand Prepare(EditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_index.ContainsKey(command.Id))
            throw DuplicateId(command.Id);
        return command with { DefaultGesture = command.DefaultGesture ?? command.Gesture };
    }

    internal static InvalidOperationException DuplicateId(string id) =>
        new($"A command with id '{id}' is already registered; command ids must be unique.");

    /// <summary>A command as it is registered, with the user's shortcut.</summary>
    internal EditorCommand WithBinding(EditorCommand command) => WithBinding(command, _appliedBindings);

    public EditorCommand? Find(string id) => _index.TryGetValue(id, out var index) ? _commands[index] : null;

    /// <exception cref="KeyNotFoundException">No command has the id.</exception>
    public EditorCommand Get(string id) => Find(id) ?? throw new KeyNotFoundException($"No command with id '{id}' is registered.");

    /// <summary>Finds the command bound to a key press, preferring one that can run when several share it.</summary>
    public EditorCommand? FindByGesture(KeyEventArgs e)
    {
        EditorCommand? found = null;
        foreach (var command in _commands)
        {
            if (command.Gesture?.Matches(e) != true && command.AlternateGesture?.Matches(e) != true)
                continue;
            if (command.CanExecute)
                return command;
            found ??= command;
        }

        return found;
    }

    /// <summary>Whether a command's shortcut runs while a text field has focus; commands that act on the selection leave typing alone.</summary>
    public bool WorksInText(EditorCommand command) => command is not null && !_notInText.Contains(command.Id);

    internal void KeepOutOfText(IEnumerable<string> ids) => _notInText.UnionWith(ids);

    /// <summary>Runs a command by id; returns false when it is unknown or cannot run now.</summary>
    public bool TryExecute(string id) => Find(id)?.TryExecute() == true;

    /// <summary>Asks every command to re-evaluate whether it can run, such as after the selection or the document changed.</summary>
    public void RefreshCanExecute()
    {
        foreach (var command in _commands)
        {
            if (command.Command is IRelayCommand relay)
                relay.NotifyCanExecuteChanged();
        }
    }

    internal void AddMenuEntry(MenuEntry entry) => _menu.Add(entry);

    internal void AddToolbarEntry(ToolbarEntry entry) => _toolbar.Add(entry);

    private static EditorCommand WithBinding(EditorCommand command, Dictionary<string, string> bindings) =>
        bindings.TryGetValue(command.Id, out var text)
            ? command with { Gesture = KeyGestures.Parse(text), AlternateGesture = null }
            : command;

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var bindings = _settings.Current.KeyBindings;
        if (bindings.Count == _appliedBindings.Count && bindings.All(b => _appliedBindings.TryGetValue(b.Key, out var v) && v == b.Value))
            return;
        _appliedBindings = new Dictionary<string, string>(bindings);
        for (var i = 0; i < _commands.Count; i++)
            _commands[i] = WithBinding(_registered[i], _appliedBindings);

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
