using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting;

namespace Talesmith.Editor.Inspector;

/// <summary>The values components and script fields have when their saved data leaves them out, so the inspector shows what the runtime uses.</summary>
public sealed partial class ComponentDefaults(Func<ComponentRegistry?> registry, Func<ScriptTypeRegistry?> scripts)
{
    private readonly Dictionary<string, JsonObject?> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject?> _scripts = new(StringComparer.Ordinal);

    /// <summary>The default at a path of a component, given the component's saved data, which names the scripts of script components.</summary>
    public JsonNode? Get(string component, string path, JsonNode? data)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(path);
        if (ScriptField().Match(path) is { Success: true } match)
        {
            var index = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var type = JsonValues.Text(data?["scripts"]?.AsArray().ElementAtOrDefault(index)?["type"]);
            return type is null || Script(type) is not { } fields ? null : JsonPaths.Get(fields, match.Groups[2].Value)?.DeepClone();
        }

        var defaults = Component(component);
        return defaults is null ? null : path.Length == 0 ? defaults.DeepClone() : JsonPaths.Get(defaults, path)?.DeepClone();
    }

    private JsonObject? Component(string type)
    {
        if (_components.TryGetValue(type, out var cached))
            return cached;
        JsonObject? value = null;
        try
        {
            value = registry()?.Find(type)?.CreateDefault();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            value = null;
        }

        return _components[type] = value;
    }

    private JsonObject? Script(string typeName)
    {
        if (_scripts.TryGetValue(typeName, out var cached))
            return cached;
        JsonObject? value = null;
        if (scripts()?.Find(typeName) is { } info)
        {
            try
            {
                value = info.Members.Write(info.Create(), NoReferences.Instance);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                value = null;
            }
        }

        return _scripts[typeName] = value;
    }

    [GeneratedRegex(@"^scripts\.(\d+)\.fields\.(.+)$")]
    private static partial Regex ScriptField();

    private sealed class NoReferences : ICaptureContext
    {
        public static NoReferences Instance { get; } = new();

        public AssetGuid GetGuid(object asset) => AssetGuid.Empty;

        public Guid GetEntityId(Entity entity) => Guid.Empty;
    }
}
