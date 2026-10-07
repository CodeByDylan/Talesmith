using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Talesmith.Assets;

namespace Talesmith.Runtime.Serialization;

/// <summary>An entity saved in a scene or prefab: its identity, place in the hierarchy and component data.</summary>
public sealed class EntityDocument
{
    /// <summary>The entity's stable id within its document.</summary>
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>The id of the parent entity, which comes earlier in the document, or null for a root.</summary>
    public Guid? Parent { get; set; }

    /// <summary>Inactive entities are created with an <c>Inactive</c> component, which render systems skip.</summary>
    public bool Active { get; set; } = true;

    public EditorEntityState Editor { get; set; } = new();

    /// <summary>Set when the entity is an instance of a prefab, whose entities are created in its place.</summary>
    public PrefabLink? Prefab { get; set; }

    /// <summary>The components, in inspector order; for prefab instances, components added to or replacing those of the prefab's root.</summary>
    public List<ComponentDocument> Components { get; set; } = [];

    /// <summary>Fields this version does not know, kept so they are written back unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public ComponentDocument? FindComponent(string type) => Components.Find(c => string.Equals(c.Type, type, StringComparison.Ordinal));

    public EntityDocument Clone() => new()
    {
        Id = Id,
        Name = Name,
        Parent = Parent,
        Active = Active,
        Editor = Editor.Clone(),
        Prefab = Prefab?.Clone(),
        Components = Components.ConvertAll(c => c.Clone()),
        Extra = Clone(Extra)
    };

    internal static Dictionary<string, JsonElement>? Clone(Dictionary<string, JsonElement>? extra) => extra is null ? null : new(extra);
}

/// <summary>Editor-only state of an entity; the runtime ignores it.</summary>
public sealed class EditorEntityState
{
    /// <summary>Hidden in the editor's viewport.</summary>
    public bool Hidden { get; set; }

    /// <summary>Cannot be selected in the editor's viewport.</summary>
    public bool Locked { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public EditorEntityState Clone() => new() { Hidden = Hidden, Locked = Locked, Extra = EntityDocument.Clone(Extra) };
}

/// <summary>A component saved as its type name and a JSON object of property values.</summary>
/// <remarks>Components whose type is not registered, such as those of a disabled plugin, keep their data and are written back unchanged.</remarks>
public sealed class ComponentDocument
{
    public ComponentDocument()
    {
    }

    public ComponentDocument(string type, JsonObject data)
    {
        Type = type;
        Data = data;
    }

    /// <summary>The component's type name; see <see cref="IComponentDefinition.TypeName"/>.</summary>
    public string Type { get; set; } = "";

    [JsonConverter(typeof(CompactJsonObjectConverter))]
    public JsonObject Data { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public ComponentDocument Clone() => new(Type, (JsonObject)Data.DeepClone()) { Extra = EntityDocument.Clone(Extra) };
}

/// <summary>Links an entity to the prefab it instantiates, with the changes made to this instance.</summary>
public sealed class PrefabLink
{
    /// <summary>The guid of the <c>.tprefab</c> asset.</summary>
    public AssetGuid Asset { get; set; }

    /// <summary>Property values that differ from the prefab, applied in order.</summary>
    public List<PrefabOverride> Overrides { get; set; } = [];

    /// <summary>Components of the prefab's entities that this instance does not have.</summary>
    public List<PrefabComponentRef> RemovedComponents { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public PrefabLink Clone() => new()
    {
        Asset = Asset,
        Overrides = Overrides.ConvertAll(o => o.Clone()),
        RemovedComponents = RemovedComponents.ConvertAll(r => new PrefabComponentRef { Entity = r.Entity, Component = r.Component }),
        Extra = EntityDocument.Clone(Extra)
    };
}

/// <summary>A property value of a prefab instance that differs from the prefab.</summary>
public sealed class PrefabOverride
{
    /// <summary>The id of the entity within the prefab; entities of nested prefabs use their ids as expanded in this prefab.</summary>
    public Guid Entity { get; set; }

    /// <summary>The component's type name.</summary>
    public string Component { get; set; } = "";

    /// <summary>The property within the component's data, such as "position" or "tint"; nested values are separated by dots and list items are numbers, as in "frames.2.duration".</summary>
    public string Path { get; set; } = "";

    [JsonConverter(typeof(CompactJsonNodeConverter))]
    public JsonNode? Value { get; set; }

    public PrefabOverride Clone() => new() { Entity = Entity, Component = Component, Path = Path, Value = Value?.DeepClone() };
}

/// <summary>Identifies a component of an entity within a prefab.</summary>
public sealed class PrefabComponentRef
{
    public Guid Entity { get; set; }

    public string Component { get; set; } = "";
}
