using System.Globalization;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Maps;

/// <summary>The value types a custom property can hold.</summary>
public enum PropertyType
{
    String,
    Int,
    Float,
    Bool,
    Color,
    File
}

/// <summary>A typed custom property, as authored in a map editor, stored as invariant text like the .hexy format does.</summary>
public readonly record struct PropertyValue(PropertyType Type, string Raw)
{
    public static PropertyValue FromString(string value) => new(PropertyType.String, value);

    public static PropertyValue FromInt(int value) => new(PropertyType.Int, value.ToString(CultureInfo.InvariantCulture));

    public static PropertyValue FromFloat(float value) => new(PropertyType.Float, value.ToString(CultureInfo.InvariantCulture));

    public static PropertyValue FromBool(bool value) => new(PropertyType.Bool, value ? "true" : "false");

    public static PropertyValue FromColor(Color value) => new(PropertyType.Color, value.ToString());

    public static PropertyValue FromFile(string path) => new(PropertyType.File, path);

    /// <summary>The value a new property of a type starts with, matching Hexy.</summary>
    public static PropertyValue Default(PropertyType type) => new(type, type switch
    {
        PropertyType.Int or PropertyType.Float => "0",
        PropertyType.Bool => "false",
        PropertyType.Color => "#FFFFFFFF",
        _ => string.Empty
    });

    public int AsInt(int fallback = 0) => int.TryParse(Raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public float AsFloat(float fallback = 0) => float.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    public bool AsBool(bool fallback = false) => bool.TryParse(Raw, out var value) ? value : fallback;

    public Color AsColor(Color fallback = default) => Color.TryParse(Raw, out var value) ? value : fallback;

    public override string ToString() => Raw;
}

/// <summary>Named custom properties of a map, layer, tile or object, in the order they were authored.</summary>
/// <remarks>Immutable: <see cref="With"/> and <see cref="Without"/> return changed copies, so an old set is a complete undo record.</remarks>
public sealed class PropertySet
{
    private readonly Dictionary<string, PropertyValue> _values;

    public PropertySet(IEnumerable<KeyValuePair<string, PropertyValue>>? values = null) =>
        _values = new Dictionary<string, PropertyValue>(values ?? [], StringComparer.Ordinal);

    private PropertySet(Dictionary<string, PropertyValue> values) => _values = values;

    public static PropertySet Empty { get; } = new();

    public int Count => _values.Count;

    /// <summary>The properties in authored order.</summary>
    public IReadOnlyDictionary<string, PropertyValue> Values => _values;

    public bool Contains(string name) => _values.ContainsKey(name);

    public bool TryGet(string name, out PropertyValue value) => _values.TryGetValue(name, out value);

    public string? GetString(string name) => _values.TryGetValue(name, out var value) ? value.Raw : null;

    public int GetInt(string name, int fallback = 0) => _values.TryGetValue(name, out var value) ? value.AsInt(fallback) : fallback;

    public float GetFloat(string name, float fallback = 0) => _values.TryGetValue(name, out var value) ? value.AsFloat(fallback) : fallback;

    public bool GetBool(string name, bool fallback = false) => _values.TryGetValue(name, out var value) ? value.AsBool(fallback) : fallback;

    public Color GetColor(string name, Color fallback = default) => _values.TryGetValue(name, out var value) ? value.AsColor(fallback) : fallback;

    /// <summary>Gets a copy with the property added, or replaced in place when it exists.</summary>
    public PropertySet With(string name, PropertyValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_values.TryGetValue(name, out var existing) && existing == value)
            return this;
        var values = new Dictionary<string, PropertyValue>(_values, StringComparer.Ordinal) { [name] = value };
        return new PropertySet(values);
    }

    /// <summary>Gets a copy without the property.</summary>
    public PropertySet Without(string name)
    {
        if (!_values.ContainsKey(name))
            return this;
        var values = new Dictionary<string, PropertyValue>(_values.Count - 1, StringComparer.Ordinal);
        foreach (var (key, value) in _values)
        {
            if (!string.Equals(key, name, StringComparison.Ordinal))
                values.Add(key, value);
        }

        return values.Count == 0 ? Empty : new PropertySet(values);
    }

    /// <summary>Gets a copy with a property renamed, keeping its position.</summary>
    public PropertySet Renamed(string name, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(newName);
        if (!_values.ContainsKey(name) || string.Equals(name, newName, StringComparison.Ordinal))
            return this;
        var values = new Dictionary<string, PropertyValue>(_values.Count, StringComparer.Ordinal);
        foreach (var (key, value) in _values)
        {
            if (string.Equals(key, newName, StringComparison.Ordinal))
                continue;
            values.Add(string.Equals(key, name, StringComparison.Ordinal) ? newName : key, value);
        }

        return new PropertySet(values);
    }

    /// <summary>Whether both sets hold the same properties in the same order.</summary>
    public bool SequenceEquals(PropertySet? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null || other.Count != Count)
            return false;
        using var a = _values.GetEnumerator();
        using var b = other._values.GetEnumerator();
        while (a.MoveNext() && b.MoveNext())
        {
            if (!string.Equals(a.Current.Key, b.Current.Key, StringComparison.Ordinal) || a.Current.Value != b.Current.Value)
                return false;
        }

        return true;
    }
}
