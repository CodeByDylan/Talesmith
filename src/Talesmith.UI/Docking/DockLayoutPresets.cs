using System.Text.Json;
using System.Text.Json.Nodes;

namespace Talesmith.UI.Docking;

/// <summary>Named dock layouts the user can switch between, stored as JSON.</summary>
public sealed class DockLayoutPresets
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly SortedDictionary<string, string> _presets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after a preset is saved or removed.</summary>
    public event EventHandler? Changed;

    public IReadOnlyCollection<string> Names => _presets.Keys;

    public bool Contains(string name) => _presets.ContainsKey(name);

    /// <summary>Stores a snapshot of <paramref name="layout"/> under <paramref name="name"/>, replacing any preset with that name.</summary>
    public void Save(string name, DockLayout layout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _presets[name.Trim()] = DockLayoutSerializer.Serialize(layout);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Creates a new layout from the preset; see <see cref="DockLayoutSerializer.Deserialize"/>.</summary>
    public bool TryLoad(string name, Func<string, bool>? isKnownPanel, out DockLayout? layout)
    {
        layout = null;
        return _presets.TryGetValue(name, out var json) && DockLayoutSerializer.TryDeserialize(json, isKnownPanel, out layout);
    }

    public bool Remove(string name)
    {
        if (!_presets.Remove(name))
            return false;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Serializes every preset to one JSON document.</summary>
    public string ToJson()
    {
        var root = new JsonObject();
        foreach (var (name, layout) in _presets)
            root[name] = JsonNode.Parse(layout);
        return root.ToJsonString(Options);
    }

    /// <summary>Loads presets written by <see cref="ToJson"/>; invalid entries are skipped.</summary>
    public static DockLayoutPresets FromJson(string? json)
    {
        var presets = new DockLayoutPresets();
        if (string.IsNullOrWhiteSpace(json))
            return presets;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return presets;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var layout = property.Value.GetRawText();
                if (DockLayoutSerializer.TryDeserialize(layout, null, out _))
                    presets._presets[property.Name] = layout;
            }
        }
        catch (JsonException)
        {
        }

        return presets;
    }
}
