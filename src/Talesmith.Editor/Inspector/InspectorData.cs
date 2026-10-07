using System.Text.Json.Nodes;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Inspector;

/// <summary>Where the inspector reads and writes component data: the scene's entities, or an entity of the play session.</summary>
/// <remarks>It hands out <see cref="PropertyValue"/>s and tells them when their data changed, so editors update in place.</remarks>
public abstract class InspectorData
{
    private readonly List<PropertyValue> _values = [];

    /// <summary>The number of inspected entities.</summary>
    public abstract int Count { get; }

    public virtual bool IsReadOnly => false;

    /// <summary>Whether this is the play session's live data, whose edits are discarded on Stop.</summary>
    public virtual bool IsLive => false;

    /// <summary>The value of the inspected entity at <paramref name="target"/>, or null when unset.</summary>
    public abstract JsonNode? Get(int target, string component, string path);

    /// <summary>Sets the value on every inspected entity.</summary>
    public abstract void Set(string component, string path, JsonNode? value);

    /// <summary>Changes the value of each inspected entity from its own current value.</summary>
    public virtual void Update(string component, string path, Func<JsonNode?, JsonNode?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Set(component, path, change(Get(0, component, path)?.DeepClone()));
    }

    public virtual bool IsModified(string component, string path) => false;

    public virtual void Revert(string component, string path)
    {
    }

    /// <summary>Whether the inspected entity at <paramref name="target"/> saves the value itself, rather than leaving it to its default.</summary>
    public virtual bool IsSet(int target, string component, string path) => true;

    /// <summary>The value an unset property follows, such as a sprite's region from its named sprite, or null when unknown.</summary>
    public virtual JsonNode? GetAuto(int target, string component, string path) => null;

    /// <summary>Like <see cref="Update"/>, starting from the value an unset property follows.</summary>
    public virtual void UpdateAuto(string component, string path, Func<JsonNode?, JsonNode?> change) => Update(component, path, change);

    /// <summary>Tells the values that follow others while unset to read again, such as after the edit world loaded a texture.</summary>
    public void NotifyAuto()
    {
        OnInvalidated();
        foreach (var value in _values.ToArray())
        {
            if (value.Property.AutoValue is not null)
                value.Notify();
        }
    }

    /// <summary>Called before values are told to read again.</summary>
    protected virtual void OnInvalidated()
    {
    }

    public PropertyValue CreateValue(string component, string path, PropertyDescriptor property)
    {
        var value = new PropertyValue(this, component, path, property);
        _values.Add(value);
        foreach (var scope in _scopes)
            scope.Add(value);
        return value;
    }

    /// <summary>Collects every value created until the result is disposed into <paramref name="values"/>, so an editor that rebuilds its rows can
    /// release the values of the old ones, nested ones included.</summary>
    public IDisposable Collect(List<PropertyValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _scopes.Add(values);
        return new Scope(() => _scopes.Remove(values));
    }

    private readonly List<List<PropertyValue>> _scopes = [];

    private sealed class Scope(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    /// <summary>Tells the values a change at <paramref name="path"/> of a component may affect; an empty path affects the whole component.</summary>
    public void Notify(string component, string path)
    {
        OnInvalidated();
        foreach (var value in _values.ToArray())
        {
            if (value.IsAffectedBy(component, path))
                value.Notify();
        }
    }

    /// <summary>Tells every value to read again.</summary>
    public void NotifyAll()
    {
        OnInvalidated();
        foreach (var value in _values.ToArray())
            value.Notify();
    }

    /// <summary>The values handed out and not released, for tests.</summary>
    internal IReadOnlyList<PropertyValue> Values => _values;

    internal void Release(PropertyValue value) => _values.Remove(value);

    /// <summary>Releases every value, such as when the inspector shows something else.</summary>
    public void ReleaseAll() => _values.Clear();
}

/// <summary>The selected entities of the open scene, edited through <see cref="EntityDataService"/>: every edit is undoable and edits of several
/// entities are one step.</summary>
public sealed class DocumentInspectorData(EntityDataService entities, IUndoService undo, IReadOnlyList<Guid> targets, ComponentDefaults? defaults = null,
    Func<Guid, string, JsonObject?>? effective = null)
    : InspectorData
{
    private readonly Dictionary<(Guid, string), JsonObject?> _effective = [];

    public IReadOnlyList<Guid> Targets { get; private set; } = targets;

    /// <summary>Inspects other entities with the same components, so the editors built for these targets show theirs.</summary>
    public void Retarget(IReadOnlyList<Guid> targets)
    {
        Targets = targets;
        NotifyAll();
    }

    public override int Count => Targets.Count;

    public override JsonNode? Get(int target, string component, string path) => Get(Targets[target], component, path);

    private JsonNode? Get(Guid id, string component, string path) =>
        entities.Get(id, component, path) ?? defaults?.Get(component, path, entities.Get(id, component, ""));

    public override void Set(string component, string path, JsonNode? value)
    {
        var name = path.Length == 0 ? component : path;
        using var transaction = Targets.Count > 1 ? undo.BeginTransaction($"Change {name} of {Targets.Count} entities") : null;
        foreach (var id in Targets)
        {
            if (entities.GetComponent(id, component) is not null)
                entities.Set(id, component, path, value?.DeepClone());
        }
    }

    public override void Update(string component, string path, Func<JsonNode?, JsonNode?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var name = path.Length == 0 ? component : path;
        using var transaction = Targets.Count > 1 ? undo.BeginTransaction($"Change {name} of {Targets.Count} entities") : null;
        foreach (var id in Targets)
        {
            if (entities.GetComponent(id, component) is not null)
                entities.Set(id, component, path, change(Get(id, component, path)?.DeepClone()));
        }
    }

    public override bool IsSet(int target, string component, string path) => entities.Get(Targets[target], component, path) is not null;

    public override JsonNode? GetAuto(int target, string component, string path) => GetAuto(Targets[target], component, path);

    public override void UpdateAuto(string component, string path, Func<JsonNode?, JsonNode?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var transaction = Targets.Count > 1 ? undo.BeginTransaction($"Change {path} of {Targets.Count} entities") : null;
        foreach (var id in Targets)
        {
            if (entities.GetComponent(id, component) is null)
                continue;
            var current = entities.Get(id, component, path) ?? GetAuto(id, component, path) ?? Get(id, component, path);
            entities.Set(id, component, path, change(current?.DeepClone()));
        }
    }

    protected override void OnInvalidated() => _effective.Clear();

    private JsonNode? GetAuto(Guid id, string component, string path)
    {
        if (effective is null)
            return null;
        if (!_effective.TryGetValue((id, component), out var data))
            _effective[(id, component)] = data = effective(id, component);
        return data is null ? null : JsonPaths.Get(data, path);
    }

    public override bool IsModified(string component, string path) => Targets.Any(id => entities.IsOverridden(id, component, path));

    public override void Revert(string component, string path)
    {
        using var transaction = Targets.Count > 1 ? undo.BeginTransaction($"Revert {path} of {Targets.Count} entities") : null;
        foreach (var id in Targets)
            entities.RevertToPrefab(id, component, path);
    }
}
