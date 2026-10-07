using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Ecs;

namespace Talesmith.Runtime.Serialization;

/// <summary>How a component type is saved in scenes and prefabs, shown in the editor and put on entities.</summary>
/// <remarks>Saved component data is a JSON object of property values. Register a custom definition to override the reflection-based one.</remarks>
public interface IComponentDefinition
{
    /// <summary>The stable name written to files, such as "Sprite" for engine components or a full type name for others.</summary>
    string TypeName { get; }

    Type ComponentType { get; }

    ComponentInfo Info { get; }

    /// <summary>The editable properties, in inspector order.</summary>
    IReadOnlyList<PropertyDescriptor> Properties { get; }

    /// <summary>The data of a newly added component.</summary>
    JsonObject CreateDefault();

    /// <summary>The assets the data refers to, with the type to load each as, so they can be loaded before <see cref="Apply"/>.</summary>
    IEnumerable<AssetDependency> GetDependencies(JsonObject data);

    /// <summary>Adds the component to an entity, or replaces it, from saved data.</summary>
    void Apply(World world, Entity entity, JsonObject data, IInstantiationContext context);

    /// <summary>Reads the component of an entity back into saved data, or null when the entity has none.</summary>
    JsonObject? Capture(World world, Entity entity, ICaptureContext context);

    /// <summary>Like <see cref="Capture"/>, but keeps values that follow others while unset, such as a sprite's region, so the editor can show them.</summary>
    JsonObject? CaptureEffective(World world, Entity entity, ICaptureContext context) => Capture(world, entity, context);

    void Remove(World world, Entity entity);
}

/// <summary>An asset that saved data refers to.</summary>
/// <param name="AssetType">The type to load the asset as, since one file can load as several types, such as a sound clip or a music track.</param>
public readonly record struct AssetDependency(AssetGuid Guid, Type AssetType);

/// <summary>How a component is presented in the editor.</summary>
/// <param name="Icon">The name of an icon from the editor's icon set, or null for the default.</param>
public sealed record ComponentInfo(string DisplayName, string Category, string? Description = null, string? Icon = null, bool Hidden = false);

/// <summary>Resolves references while saved data is put on entities.</summary>
public interface IInstantiationContext
{
    /// <summary>Gets an asset that was loaded before instantiation began, or null when it is missing.</summary>
    T? GetAsset<T>(AssetGuid guid) where T : class;

    /// <summary>Gets the entity created for a saved entity id in the same scene or prefab, or <see cref="Entity.Null"/>.</summary>
    Entity GetEntity(Guid id);

    IServiceProvider Services { get; }
}

/// <summary>Turns runtime references back into saved references while components are captured.</summary>
public interface ICaptureContext
{
    /// <summary>Gets the guid of a loaded asset instance, or <see cref="AssetGuid.Empty"/> when it did not come from a file.</summary>
    AssetGuid GetGuid(object asset);

    /// <summary>Gets the saved id of an entity, or <see cref="Guid.Empty"/> when it has none.</summary>
    Guid GetEntityId(Entity entity);
}
