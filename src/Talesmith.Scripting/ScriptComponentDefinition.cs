using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Talesmith.Ecs;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Scripting;

/// <summary>Saves a <see cref="ScriptComponent"/> as a list of scripts with their type, enabled state and fields.</summary>
/// <remarks>
/// Fields are read and written by each script type's <see cref="ScriptTypeInfo.Members"/>, so they use the same value converters as
/// components. Applying data to an entity that already has scripts updates the scripts in place, matching them by type in order, so
/// editing a script's fields while the game plays keeps its state. Unknown script types are kept verbatim.
/// </remarks>
internal sealed class ScriptComponentDefinition(ScriptTypeRegistry registry, ILogger<ScriptComponentDefinition> logger) : IComponentDefinition
{
    public const string ScriptsProperty = "scripts";
    public const string TypeProperty = "type";
    public const string EnabledProperty = "enabled";
    public const string FieldsProperty = "fields";

    public string TypeName { get; } = ComponentRegistry.GetTypeName(typeof(ScriptComponent));

    public Type ComponentType => typeof(ScriptComponent);

    public ComponentInfo Info { get; } = new("Scripts", "Scripting", "C# scripts that give the entity behavior.", "file-code");

    public IReadOnlyList<PropertyDescriptor> Properties => [];

    public JsonObject CreateDefault() => new() { [ScriptsProperty] = new JsonArray() };

    public IEnumerable<AssetDependency> GetDependencies(JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var dependencies = new List<AssetDependency>();
        foreach (var entry in Parse(data))
        {
            if (registry.TryFind(entry.Type, out var info))
                info.Members.CollectDependencies(entry.Fields, dependencies);
        }

        return dependencies;
    }

    public void Apply(World world, Entity entity, JsonObject data, IInstantiationContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        var entries = Parse(data);
        var existing = world.TryGet<ScriptComponent>(entity, out var current) ? current : null;
        var component = existing ?? new ScriptComponent();
        var matched = Match(component, entries);

        foreach (var script in component.Items.ToArray())
        {
            if (Array.IndexOf(matched, script) < 0)
                component.Remove(script);
        }

        component.MissingItems.Clear();
        var ordered = new List<Script>(entries.Count);
        for (var position = 0; position < entries.Count; position++)
        {
            var entry = entries[position];
            if (!registry.TryFind(entry.Type, out var info))
            {
                registry.ReportMissing(entry.Type);
                logger.ScriptTypeMissing(entry.Type, ScriptRuntime.Describe(world, entity));
                component.MissingItems.Add(new MissingScript(entry.Type, entry.Enabled, (JsonObject)entry.Fields.DeepClone(), position));
                continue;
            }

            var script = matched[position];
            if (script is null)
            {
                try
                {
                    script = info.Create();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    logger.ScriptCreateFailed(info.TypeName, ex.InnerException?.Message ?? ex.Message);
                    component.MissingItems.Add(new MissingScript(entry.Type, entry.Enabled, (JsonObject)entry.Fields.DeepClone(), position));
                    continue;
                }
            }

            info.Members.Read(script, entry.Fields, context, (field, error) => logger.ScriptFieldInvalid(info.TypeName, field, error.Message));
            script.Enabled = entry.Enabled;
            if (script.Owner is null)
                component.Add(script);
            ordered.Add(script);
        }

        component.Items.Clear();
        component.Items.AddRange(ordered);
        if (existing is null)
            world.Set(entity, component);
    }

    public JsonObject? Capture(World world, Entity entity, ICaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(entity) || !world.TryGet<ScriptComponent>(entity, out var component) || component is null)
            return null;

        var entries = new List<JsonNode>(component.Items.Count + component.MissingItems.Count);
        foreach (var script in component.Items)
        {
            var info = registry.GetOrAdd(script.GetType());
            entries.Add(Entry(info.TypeName, script.Enabled, info.Members.Write(script, context)));
        }

        foreach (var missing in component.MissingItems.OrderBy(m => m.Position))
            entries.Insert(Math.Clamp(missing.Position, 0, entries.Count), Entry(missing.TypeName, missing.Enabled, (JsonObject)missing.Fields.DeepClone()));

        return new JsonObject { [ScriptsProperty] = new JsonArray([.. entries]) };
    }

    public void Remove(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.IsAlive(entity))
            world.Remove<ScriptComponent>(entity);
    }

    private static JsonObject Entry(string type, bool enabled, JsonObject fields) =>
        new() { [TypeProperty] = type, [EnabledProperty] = enabled, [FieldsProperty] = fields };

    /// <summary>For each entry, the existing script of the same type to reuse, taking scripts in order.</summary>
    private Script?[] Match(ScriptComponent component, List<SavedScript> entries)
    {
        var matched = new Script?[entries.Count];
        var used = new bool[component.Items.Count];
        for (var position = 0; position < entries.Count; position++)
        {
            if (!registry.TryFind(entries[position].Type, out var info))
                continue;
            for (var i = 0; i < component.Items.Count; i++)
            {
                if (!used[i] && component.Items[i].GetType() == info.Type)
                {
                    used[i] = true;
                    matched[position] = component.Items[i];
                    break;
                }
            }
        }

        return matched;
    }

    /// <exception cref="FormatException">The data has no list of scripts.</exception>
    private static List<SavedScript> Parse(JsonObject data)
    {
        if (!data.TryGetPropertyValue(ScriptsProperty, out var node) || node is null)
            return [];
        if (node is not JsonArray array)
            throw new FormatException($"Expected \"{ScriptsProperty}\" to be a list of scripts.");

        var entries = new List<SavedScript>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject entry || entry[TypeProperty] is not JsonValue type || !type.TryGetValue<string>(out var typeName) || typeName.Length == 0)
                continue;
            var enabled = entry[EnabledProperty] is not JsonValue flag || !flag.TryGetValue<bool>(out var value) || value;
            var fields = entry[FieldsProperty] as JsonObject ?? [];
            entries.Add(new SavedScript(typeName, enabled, fields));
        }

        return entries;
    }

    private sealed record SavedScript(string Type, bool Enabled, JsonObject Fields);
}
