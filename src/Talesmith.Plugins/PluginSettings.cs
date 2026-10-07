using System.Text.Json;
using System.Text.Json.Nodes;

namespace Talesmith.Plugins;

/// <summary>A plugin's settings, kept in the <c>settings</c> section of the game's <c>config/plugins.json</c>.</summary>
/// <remarks>Instances made with the public constructor live only in memory, which suits tests and tools.</remarks>
public sealed class PluginSettings : IPluginSettings
{
    // Per instance, so cached metadata for plugin types does not outlive a collectible plugin.
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly PluginConfigurationStore _store;

    /// <summary>Creates settings that live only in memory.</summary>
    public PluginSettings(string pluginId)
        : this(pluginId, PluginConfigurationStore.InMemory())
    {
    }

    internal PluginSettings(string pluginId, PluginConfigurationStore store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        PluginId = pluginId;
        _store = store;
    }

    public string PluginId { get; }

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_store.Gate)
                return [.. Values.Select(p => p.Key)];
        }
    }

    private JsonObject Values => _store.GetSettings(PluginId);

    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_store.Gate)
            return Values.ContainsKey(key);
    }

    public bool TryGet<T>(string key, out T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_store.Gate)
        {
            if (Values.TryGetPropertyValue(key, out var node) && TryConvert(node, out value))
                return true;
        }

        value = default!;
        return false;
    }

    public T Get<T>(string key, T fallback) => TryGet<T>(key, out var value) ? value : fallback;

    public void Set<T>(string key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var node = JsonSerializer.SerializeToNode(value, _json);
        lock (_store.Gate)
        {
            Values[key] = node;
            _store.MarkChanged(PluginId);
        }
    }

    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_store.Gate)
        {
            if (!Values.Remove(key))
                return false;
            _store.MarkChanged(PluginId);
            return true;
        }
    }

    public void Save() => _store.SaveSettings(PluginId);

    private bool TryConvert<T>(JsonNode? node, out T value)
    {
        value = default!;
        if (node is null)
            return default(T) is null;

        try
        {
            value = node.Deserialize<T>(_json)!;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }
}
