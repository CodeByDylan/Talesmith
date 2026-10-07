using Talesmith.Events;

namespace Talesmith.Input;

/// <summary>The game's actions by name (case-insensitive), with their default and current bindings.</summary>
/// <remarks>
/// Defaults come from <see cref="Define"/> or the profile the map was created from; <see cref="Apply"/> and <see cref="Rebind"/> change
/// the current bindings only, so <see cref="ResetToDefaults"/> can undo them.
/// </remarks>
public sealed class InputActionMap
{
    private readonly List<InputAction> _actions = [];
    private readonly Dictionary<string, InputAction> _byName = new(StringComparer.OrdinalIgnoreCase);

    public InputActionMap()
    {
    }

    /// <summary>Creates a map whose defaults are the actions of a profile.</summary>
    public InputActionMap(InputProfile defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        foreach (var (name, definition) in defaults.Actions)
            Add(name, definition.Kind, Copy(definition.Bindings));
    }

    public int Count => _actions.Count;

    /// <exception cref="KeyNotFoundException">No action has the name.</exception>
    public InputAction this[string name] =>
        _byName.TryGetValue(name, out var action) ? action : throw new KeyNotFoundException($"Input action '{name}' is not defined.");

    public bool TryGet(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out InputAction? action) =>
        _byName.TryGetValue(name, out action);

    /// <summary>Defines an action with default bindings, or replaces the defaults and bindings of an existing one.</summary>
    public InputAction Define(string name, InputActionKind kind, params InputBinding[] defaultBindings)
    {
        var bindings = Copy(defaultBindings);
        if (!_byName.TryGetValue(name, out var action))
            return Add(name, kind, bindings);

        action.SetDefaults(kind, bindings);
        return action;
    }

    /// <summary>Replaces the bindings of every action in the profile; actions the map does not have yet are defined from it.</summary>
    public void Apply(InputProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        foreach (var (name, definition) in profile.Actions)
        {
            if (_byName.TryGetValue(name, out var action))
                action.SetBindings(definition.Kind, Copy(definition.Bindings));
            else
                Add(name, definition.Kind, Copy(definition.Bindings));
        }
    }

    /// <summary>A profile of the current bindings, for saving.</summary>
    public InputProfile ToProfile()
    {
        var profile = new InputProfile();
        foreach (var action in _actions)
            profile.Actions[action.Name] = new InputActionDefinition(action.Kind, [.. action.Bindings]);
        return profile;
    }

    /// <summary>Replaces one binding of an action; an index equal to the binding count adds a binding.</summary>
    public void Rebind(string actionName, int bindingIndex, InputBinding newBinding)
    {
        ArgumentNullException.ThrowIfNull(newBinding);
        var action = this[actionName];
        ArgumentOutOfRangeException.ThrowIfNegative(bindingIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bindingIndex, action.Bindings.Count);
        action.ReplaceBinding(bindingIndex, newBinding);
    }

    /// <summary>The actions with a binding that shares a key or mouse button with the given binding.</summary>
    public IReadOnlyList<InputAction> FindConflicts(InputBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var conflicts = new List<InputAction>();
        foreach (var action in _actions)
        {
            foreach (var existing in action.Bindings)
            {
                if (!existing.Overlaps(binding))
                    continue;

                conflicts.Add(action);
                break;
            }
        }

        return conflicts;
    }

    /// <summary>Restores the default bindings of every action.</summary>
    public void ResetToDefaults()
    {
        foreach (var action in _actions)
            action.ResetToDefaults();
    }

    public List<InputAction>.Enumerator GetEnumerator() => _actions.GetEnumerator();

    internal void Update(DeviceState state, IEventBus events)
    {
        for (var i = 0; i < _actions.Count; i++)
        {
            var action = _actions[i];
            action.Update(state);
            if (action.WasPressed)
                events.Publish(new ActionTriggered(action.Name));
            if (action.WasReleased)
                events.Publish(new ActionReleased(action.Name));
        }
    }

    private InputAction Add(string name, InputActionKind kind, InputBinding[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var action = new InputAction(name, kind, bindings);
        _actions.Add(action);
        _byName.Add(name, action);
        return action;
    }

    private static InputBinding[] Copy(IReadOnlyCollection<InputBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var copy = new InputBinding[bindings.Count];
        var i = 0;
        foreach (var binding in bindings)
            copy[i++] = binding ?? throw new ArgumentException("Bindings cannot be null.", nameof(bindings));
        return copy;
    }
}
