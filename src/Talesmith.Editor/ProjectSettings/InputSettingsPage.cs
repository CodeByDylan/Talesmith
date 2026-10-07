using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Input;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>One key of a binding, such as the "Up" key of a vector binding, which can record the next key pressed.</summary>
public sealed partial class KeySlot(string label, Key key, Action changed) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private Key _key = key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private KeyModifiers _modifiers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    private bool _isRecording;

    public string Text => IsRecording ? "Press a key…" : Describe(Key, Modifiers);

    /// <summary>Takes the key pressed while recording; Escape cancels.</summary>
    public void Record(Key key, KeyModifiers modifiers, bool allowModifiers)
    {
        IsRecording = false;
        if (key is Key.None or Key.Escape)
            return;
        Key = key;
        Modifiers = allowModifiers ? modifiers : KeyModifiers.None;
        changed();
    }

    public static string Describe(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Meta");
        parts.Add(key == Key.None ? "Unbound" : key.ToString());
        return string.Join("+", parts);
    }
}

public enum BindingType
{
    Key,
    Mouse,
    Axis,
    Vector
}

/// <summary>One binding of an action: a key, a mouse button, two keys for an axis or four for a vector.</summary>
public sealed partial class BindingItem : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private int _mouseButtonIndex;

    public BindingItem(InputBinding binding, Action changed)
    {
        _changed = changed;
        switch (binding)
        {
            case KeyBinding key:
                Type = BindingType.Key;
                Slots = [new KeySlot("Key", key.Key, changed) { Modifiers = key.Modifiers }];
                break;
            case MouseButtonBinding mouse:
                Type = BindingType.Mouse;
                Slots = [];
                _mouseButtonIndex = (int)mouse.Button;
                break;
            case AxisBinding axis:
                Type = BindingType.Axis;
                Slots = [new KeySlot("−", axis.Negative, changed), new KeySlot("+", axis.Positive, changed)];
                break;
            case VectorBinding vector:
                Type = BindingType.Vector;
                Slots = [new KeySlot("Up", vector.Up, changed), new KeySlot("Left", vector.Left, changed), new KeySlot("Down", vector.Down, changed),
                    new KeySlot("Right", vector.Right, changed)];
                break;
            default:
                throw new ArgumentException($"Unknown binding {binding.GetType().Name}.", nameof(binding));
        }
    }

    public BindingType Type { get; }

    public IReadOnlyList<KeySlot> Slots { get; }

    public bool IsMouse => Type == BindingType.Mouse;

    public bool HasSlots => Slots.Count > 0;

    public string TypeName => Type switch
    {
        BindingType.Key => "Key",
        BindingType.Mouse => "Mouse",
        BindingType.Axis => "Axis",
        _ => "Vector"
    };

    public IReadOnlyList<string> MouseButtons { get; } = Enum.GetNames<MouseButton>();

    public InputBinding ToBinding() => Type switch
    {
        BindingType.Key => new KeyBinding(Slots[0].Key, Slots[0].Modifiers),
        BindingType.Mouse => new MouseButtonBinding((MouseButton)MouseButtonIndex),
        BindingType.Axis => new AxisBinding(Slots[0].Key, Slots[1].Key),
        _ => new VectorBinding(Slots[0].Key, Slots[2].Key, Slots[1].Key, Slots[3].Key)
    };

    public static InputBinding Default(BindingType type) => type switch
    {
        BindingType.Key => new KeyBinding(Key.None),
        BindingType.Mouse => new MouseButtonBinding(MouseButton.Left),
        BindingType.Axis => new AxisBinding(Key.None, Key.None),
        _ => new VectorBinding(Key.None, Key.None, Key.None, Key.None)
    };

    partial void OnMouseButtonIndexChanged(int value) => _changed();
}

/// <summary>An action of the input profile with its bindings.</summary>
public sealed partial class InputActionItem : ObservableObject
{
    private readonly Action _changed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindName))]
    private int _kindIndex;

    public InputActionItem(string name, InputActionDefinition definition, Action changed)
    {
        _changed = changed;
        _name = name;
        _kindIndex = (int)definition.Kind;
        foreach (var binding in definition.Bindings)
            Bindings.Add(new BindingItem(binding, changed));
        Bindings.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Summary));
            changed();
        };
    }

    public ObservableCollection<BindingItem> Bindings { get; } = [];

    public InputActionKind Kind => (InputActionKind)KindIndex;

    public string KindName => Kind.ToString();

    /// <summary>The bindings in short, such as "W/A/S/D, Arrows".</summary>
    public string Summary => Bindings.Count == 0 ? "No bindings" : string.Join(", ", Bindings.Select(b => b.Type switch
    {
        BindingType.Mouse => $"Mouse {(MouseButton)b.MouseButtonIndex}",
        _ => string.Join("/", b.Slots.Select(s => KeySlot.Describe(s.Key, s.Modifiers)))
    }));

    public InputActionDefinition ToDefinition() => new(Kind, [.. Bindings.Select(b => b.ToBinding())]);

    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));

    partial void OnNameChanged(string value) => _changed();

    partial void OnKindIndexChanged(int value) => _changed();
}

/// <summary>The input page of the project settings: the actions of <c>config/input.json</c> and their bindings.</summary>
public sealed partial class InputSettingsPage : ObservableObject
{
    private readonly string _path;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private InputActionItem? _selectedAction;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _error;

    /// <param name="path">The input profile file.</param>
    public InputSettingsPage(string path)
    {
        _path = path;
        Load();
    }

    public ObservableCollection<InputActionItem> Actions { get; } = [];

    public bool HasSelection => SelectedAction is not null;

    public IReadOnlyList<string> Kinds { get; } = Enum.GetNames<InputActionKind>();

    /// <summary>The slot recording a key, if any.</summary>
    public KeySlot? Recording { get; private set; }

    /// <summary>Validates the actions and builds the profile.</summary>
    /// <exception cref="InvalidDataException">Two actions share a name or an action has no name.</exception>
    public InputProfile ToProfile()
    {
        var profile = new InputProfile();
        foreach (var action in Actions)
        {
            var name = action.Name.Trim();
            if (name.Length == 0)
                throw new InvalidDataException("Every action needs a name.");
            if (!profile.Actions.TryAdd(name, action.ToDefinition()))
                throw new InvalidDataException($"Two actions are named {name}.");
        }

        return profile;
    }

    public void Save()
    {
        var profile = ToProfile();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, profile.ToJson() + "\n");
        File.Move(temporary, _path, overwrite: true);
        IsDirty = false;
    }

    /// <summary>Starts recording the next key pressed into a slot.</summary>
    [RelayCommand]
    private void StartRecording(KeySlot slot)
    {
        if (Recording is { } previous)
            previous.IsRecording = false;
        Recording = slot;
        slot.IsRecording = true;
    }

    /// <summary>Gives the recording slot a key; returns whether one was recording.</summary>
    public bool Record(Key key, KeyModifiers modifiers)
    {
        if (Recording is not { } slot)
            return false;
        Recording = null;
        var binding = SelectedAction?.Bindings.FirstOrDefault(b => b.Slots.Contains(slot));
        slot.Record(key, modifiers, allowModifiers: binding?.Type == BindingType.Key);
        SelectedAction?.RefreshSummary();
        return true;
    }

    [RelayCommand]
    private void AddAction()
    {
        var name = "NewAction";
        for (var i = 2; Actions.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)); i++)
            name = $"NewAction{i}";
        var action = new InputActionItem(name, new InputActionDefinition(InputActionKind.Button, [new KeyBinding(Key.None)]), MarkDirty);
        Actions.Add(action);
        SelectedAction = action;
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveAction(InputActionItem action)
    {
        var index = Actions.IndexOf(action);
        Actions.Remove(action);
        SelectedAction = Actions.Count == 0 ? null : Actions[Math.Clamp(index, 0, Actions.Count - 1)];
        MarkDirty();
    }

    [RelayCommand]
    private void AddBinding(BindingType type) => SelectedAction?.Bindings.Add(new BindingItem(BindingItem.Default(type), MarkDirty));

    [RelayCommand]
    private void RemoveBinding(BindingItem binding) => SelectedAction?.Bindings.Remove(binding);

    /// <summary>Discards changes and reads the file again.</summary>
    [RelayCommand]
    public void Reset() => Load();

    private void Load()
    {
        _loading = true;
        var selected = SelectedAction?.Name;
        Actions.Clear();
        Error = null;
        try
        {
            var profile = File.Exists(_path) ? InputProfile.FromJson(File.ReadAllText(_path)) : new InputProfile();
            foreach (var (name, definition) in profile.Actions.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
                Actions.Add(new InputActionItem(name, definition, MarkDirty));
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            Error = $"{Path.GetFileName(_path)} could not be read: {ex.Message}";
        }

        SelectedAction = Actions.FirstOrDefault(a => a.Name == selected) ?? Actions.FirstOrDefault();
        _loading = false;
        IsDirty = false;
    }

    private void MarkDirty()
    {
        if (!_loading)
            IsDirty = true;
    }
}
