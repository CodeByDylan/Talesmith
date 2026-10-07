using System.Text.Json.Nodes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Inspector;

/// <summary>A property of a component as the inspector edits it: a path in the component's data, for every inspected entity at once.</summary>
/// <remarks>Values are created by an <see cref="InspectorData"/>, which tells them when their data changed; release them with
/// <see cref="Dispose"/> when their editor goes away.</remarks>
public sealed class PropertyValue : IPropertyValue, IDisposable
{
    private readonly InspectorData _data;

    internal PropertyValue(InspectorData data, string component, string path, PropertyDescriptor property)
    {
        _data = data;
        Component = component;
        Path = path;
        Property = property;
    }

    /// <summary>The component type name.</summary>
    public string Component { get; }

    /// <summary>The <see cref="JsonPaths"/> path within the component's data; empty for the whole data.</summary>
    public string Path { get; }

    public PropertyDescriptor Property { get; }

    /// <summary>Where the value is read and written.</summary>
    public InspectorData Data => _data;

    public bool IsMixed
    {
        get
        {
            if (_data.Count < 2)
                return false;
            var first = ValueAt(0);
            for (var i = 1; i < _data.Count; i++)
            {
                if (!JsonNode.DeepEquals(first, ValueAt(i)))
                    return true;
            }

            return false;
        }
    }

    public bool IsReadOnly => _data.IsReadOnly;

    /// <summary>Whether the value differs from the prefab the entity comes from.</summary>
    public bool IsModified => _data.IsModified(Component, Path);

    public event EventHandler? Changed;

    /// <summary>Whether the value follows something else because no inspected entity sets it; see <see cref="PropertyDescriptor.AutoValue"/>.</summary>
    public bool IsAuto => Property.AutoValue is not null && !Enumerable.Range(0, _data.Count).Any(i => _data.IsSet(i, Component, Path));

    public JsonNode? Get() => ValueAt(0);

    /// <summary>Clears the value of every inspected entity, so it follows what it follows while unset again.</summary>
    public void ResetToAuto()
    {
        if (IsReadOnly)
            return;
        var dot = Path.LastIndexOf('.');
        var key = Path[(dot + 1)..];
        _data.Update(Component, dot < 0 ? "" : Path[..dot], node =>
        {
            if (node is JsonObject data)
                data.Remove(key);
            return node;
        });
    }

    private JsonNode? ValueAt(int target) =>
        Property.AutoValue is not null && !_data.IsSet(target, Component, Path) && _data.GetAuto(target, Component, Path) is { } auto
            ? auto
            : _data.Get(target, Component, Path);

    /// <summary>The value of each inspected entity, in selection order.</summary>
    public IEnumerable<JsonNode?> GetAll()
    {
        for (var i = 0; i < _data.Count; i++)
            yield return ValueAt(i);
    }

    public void Set(JsonNode? value)
    {
        if (!IsReadOnly)
            _data.Set(Component, Path, value);
    }

    public void Update(Func<JsonNode?, JsonNode?> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (IsReadOnly)
            return;
        if (Property.AutoValue is not null)
            _data.UpdateAuto(Component, Path, change);
        else
            _data.Update(Component, Path, change);
    }

    /// <summary>Sets the value back to the prefab's.</summary>
    public void Revert() => _data.Revert(Component, Path);

    /// <summary>A value for a property inside this one, such as an object's field or a list's item.</summary>
    public PropertyValue Child(string key, PropertyDescriptor property) =>
        _data.CreateValue(Component, Path.Length == 0 ? key : $"{Path}.{key}", property);

    /// <summary>A value for a property next to this one in the same object, such as the texture of a sprite name.</summary>
    public PropertyValue Sibling(string key, PropertyDescriptor property)
    {
        var dot = Path.LastIndexOf('.');
        return _data.CreateValue(Component, dot < 0 ? key : $"{Path[..dot]}.{key}", property);
    }

    /// <summary>Whether a change at <paramref name="path"/> may change this value.</summary>
    internal bool IsAffectedBy(string component, string path) =>
        component == Component && (path.Length == 0 || Path.Length == 0 || path == Path || Path.StartsWith(path + ".", StringComparison.Ordinal) ||
                                   path.StartsWith(Path + ".", StringComparison.Ordinal));

    internal void Notify() => Changed?.Invoke(this, EventArgs.Empty);

    public void Dispose() => _data.Release(this);
}
