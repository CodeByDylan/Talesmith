using System.Collections.Frozen;
using System.Text.Json;
using Talesmith.Assets;

namespace Talesmith.Plugins;

/// <summary>The game's plugin choices and plugin settings from <c>assets/config/plugins.json</c>.</summary>
/// <remarks>
/// <code>
/// {
///   "disabled": [ "samples.cutscenes" ],
///   "enabled": [ "tools.debug-console" ],
///   "settings": { "samples.hexquest": { "difficulty": "hard" } }
/// }
/// </code>
/// <c>enabled</c> switches on plugins whose manifest has <c>"enabled": false</c>.
/// </remarks>
public sealed class PluginConfiguration
{
    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    /// <summary>The settings used when the configuration file does not exist.</summary>
    public static PluginConfiguration Default { get; } = new();

    /// <summary>Ids of installed plugins that must not be loaded.</summary>
    public IReadOnlySet<string> Disabled { get; init; } = FrozenSet<string>.Empty;

    /// <summary>Ids of plugins to load even though their manifest disables them.</summary>
    public IReadOnlySet<string> Enabled { get; init; } = FrozenSet<string>.Empty;

    /// <summary>Each plugin's settings object, by plugin id.</summary>
    public IReadOnlyDictionary<string, JsonElement> Settings { get; init; } = FrozenDictionary<string, JsonElement>.Empty;

    /// <summary>Whether the configuration switches the plugin on (true), off (false) or leaves it to the manifest (null).</summary>
    public bool? IsEnabled(string id) => Disabled.Contains(id) ? false : Enabled.Contains(id) ? true : null;

    /// <summary>A copy that switches the plugin on or off, overriding its manifest.</summary>
    public PluginConfiguration WithPluginEnabled(string id, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var disabled = new HashSet<string>(Disabled, StringComparer.Ordinal);
        var enabledIds = new HashSet<string>(Enabled, StringComparer.Ordinal);
        (enabled ? enabledIds : disabled).Add(id);
        (enabled ? disabled : enabledIds).Remove(id);
        return new PluginConfiguration { Disabled = disabled, Enabled = enabledIds, Settings = Settings };
    }

    /// <summary>A copy with the plugin's settings replaced, or removed when <paramref name="settings"/> is null.</summary>
    public PluginConfiguration WithSettings(string id, JsonElement? settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var all = new Dictionary<string, JsonElement>(Settings, StringComparer.Ordinal);
        if (settings is { } value)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Plugin settings must be a JSON object.", nameof(settings));
            all[id] = value.Clone();
        }
        else
        {
            all.Remove(id);
        }

        return new PluginConfiguration { Disabled = Disabled, Enabled = Enabled, Settings = all };
    }

    /// <summary>Reads the configuration file, or returns <see cref="Default"/> when it does not exist.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON or has an unexpected shape.</exception>
    /// <exception cref="IOException">The file exists but cannot be read.</exception>
    public static PluginConfiguration Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return File.Exists(path) ? Parse(File.ReadAllBytes(path), path) : Default;
    }

    /// <summary>Parses configuration JSON; <paramref name="source"/> names it in error messages.</summary>
    /// <exception cref="InvalidDataException">The JSON is not valid or has an unexpected shape.</exception>
    public static PluginConfiguration Parse(ReadOnlyMemory<byte> json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            return Read(document.RootElement, source);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>Writes the configuration atomically, creating the folder when needed.</summary>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        AtomicFile.Write(path, Write);
    }

    /// <summary>Writes the configuration as JSON with ids in a stable order.</summary>
    public void Write(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new Utf8JsonWriter(stream, WriterOptions);
        writer.WriteStartObject();
        WriteIds(writer, "disabled", Disabled);
        if (Enabled.Count > 0)
            WriteIds(writer, "enabled", Enabled);
        if (Settings.Count > 0)
        {
            writer.WriteStartObject("settings");
            foreach (var (id, value) in Settings.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(id);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteIds(Utf8JsonWriter writer, string name, IEnumerable<string> ids)
    {
        writer.WriteStartArray(name);
        foreach (var id in ids.Order(StringComparer.Ordinal))
            writer.WriteStringValue(id);
        writer.WriteEndArray();
    }

    private static PluginConfiguration Read(JsonElement root, string source)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{source} must contain a JSON object such as {{ \"disabled\": [] }}.");

        var disabled = new HashSet<string>(StringComparer.Ordinal);
        var enabled = new HashSet<string>(StringComparer.Ordinal);
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Name)
            {
                case "disabled":
                    ReadIds(property.Value, "disabled", source, disabled);
                    break;
                case "enabled":
                    ReadIds(property.Value, "enabled", source, enabled);
                    break;
                case "settings":
                    ReadSettings(property.Value, source, settings);
                    break;
                default:
                    throw new InvalidDataException($"{source} has unknown property \"{property.Name}\" (expected disabled, enabled or settings).");
            }
        }

        var both = disabled.Intersect(enabled, StringComparer.Ordinal).ToList();
        if (both.Count > 0)
            throw new InvalidDataException($"{source} lists {string.Join(", ", both)} as both enabled and disabled.");
        return new PluginConfiguration { Disabled = disabled, Enabled = enabled, Settings = settings };
    }

    private static void ReadIds(JsonElement element, string name, string source, HashSet<string> ids)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{source}: {name} must be a list of plugin ids.");
        foreach (var id in element.EnumerateArray())
        {
            if (id.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{source}: {name} must contain only plugin id strings.");
            ids.Add(id.GetString()!.Trim());
        }
    }

    private static void ReadSettings(JsonElement element, string source, Dictionary<string, JsonElement> settings)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{source}: settings must be an object with one settings object per plugin id.");
        foreach (var plugin in element.EnumerateObject())
        {
            if (plugin.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{source}: settings of \"{plugin.Name}\" must be a JSON object.");
            settings[plugin.Name] = plugin.Value.Clone();
        }
    }
}
