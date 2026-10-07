using System.Text.Json;
using System.Text.Json.Nodes;

namespace Talesmith.Plugins;

/// <summary>Owns the game's plugin configuration file: enable and disable switches and the live settings of every plugin.</summary>
/// <remarks>Settings changed through <see cref="PluginSettings"/> stay in memory until saved; saving one plugin's settings keeps others' unsaved changes.</remarks>
internal sealed class PluginConfigurationStore
{
    private readonly string? _path;
    private readonly Dictionary<string, JsonObject> _live = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unsaved = new(StringComparer.Ordinal);
    private PluginConfiguration _saved;

    private PluginConfigurationStore(string? path, PluginConfiguration saved)
    {
        _path = path;
        _saved = saved;
    }

    /// <summary>Guards the configuration and every live settings object.</summary>
    public Lock Gate { get; } = new();

    public string? Path => _path;

    /// <summary>The configuration as last saved or read.</summary>
    public PluginConfiguration Saved
    {
        get
        {
            lock (Gate)
                return _saved;
        }
    }

    /// <summary>Opens the configuration file; a missing file is an empty configuration that is created on the first save.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid plugin configuration.</exception>
    public static PluginConfigurationStore Open(string path) => new(path, PluginConfiguration.Load(path));

    /// <summary>A configuration that is never written to disk.</summary>
    public static PluginConfigurationStore InMemory(PluginConfiguration? configuration = null) => new(null, configuration ?? PluginConfiguration.Default);

    /// <summary>Reads the file again, keeping settings that were changed but not saved yet.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid plugin configuration.</exception>
    public void Refresh()
    {
        if (_path is null)
            return;
        var configuration = PluginConfiguration.Load(_path);
        lock (Gate)
        {
            _saved = configuration;
            foreach (var id in _live.Keys.Where(id => !_unsaved.Contains(id)).ToList())
                _live.Remove(id);
        }
    }

    /// <summary>Switches a plugin on or off and saves the file.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public void SetEnabled(string id, bool enabled)
    {
        lock (Gate)
        {
            var configuration = _saved.WithPluginEnabled(id, enabled);
            Write(configuration);
            _saved = configuration;
        }
    }

    /// <summary>The live settings object of a plugin; callers must hold <see cref="Gate"/>.</summary>
    public JsonObject GetSettings(string id)
    {
        if (_live.TryGetValue(id, out var settings))
            return settings;
        settings = _saved.Settings.TryGetValue(id, out var saved) ? JsonNode.Parse(saved.GetRawText())!.AsObject() : [];
        _live.Add(id, settings);
        return settings;
    }

    /// <summary>Records that a plugin's live settings changed; callers must hold <see cref="Gate"/>.</summary>
    public void MarkChanged(string id) => _unsaved.Add(id);

    /// <summary>Saves a plugin's live settings into the file.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public void SaveSettings(string id)
    {
        lock (Gate)
        {
            var settings = GetSettings(id);
            var configuration = _saved.WithSettings(id, settings.Count == 0 ? null : JsonSerializer.SerializeToElement(settings));
            Write(configuration);
            _saved = configuration;
            _unsaved.Remove(id);
        }
    }

    private void Write(PluginConfiguration configuration)
    {
        if (_path is not null)
            configuration.Save(_path);
    }
}
