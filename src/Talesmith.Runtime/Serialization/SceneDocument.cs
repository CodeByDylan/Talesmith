using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Talesmith.Mathematics;

namespace Talesmith.Runtime.Serialization;

/// <summary>A scene saved as a <c>.tscene</c> file: its environment and entities.</summary>
/// <remarks>Read and write documents with <see cref="DocumentSerializer"/>; instantiate them with <c>SceneInstantiator</c>.</remarks>
public sealed class SceneDocument
{
    /// <summary>The format version written by this build; older documents are migrated when read.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public Guid Id { get; set; }

    public SceneEnvironment Environment { get; set; } = new();

    /// <summary>The entities in hierarchy order: every child follows its parent.</summary>
    public List<EntityDocument> Entities { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Creates an empty scene with a new id.</summary>
    public static SceneDocument Create() => new() { Id = Guid.NewGuid() };

    public EntityDocument? FindEntity(Guid id) => Entities.Find(e => e.Id == id);

    /// <summary>A deep copy that shares nothing with this document.</summary>
    public SceneDocument Clone() => new()
    {
        Version = Version,
        Id = Id,
        Environment = Environment.Clone(),
        Entities = Entities.ConvertAll(e => e.Clone()),
        Extra = EntityDocument.Clone(Extra)
    };
}

/// <summary>A reusable group of entities saved as a <c>.tprefab</c> file.</summary>
/// <remarks>The first entity without a parent is the root; it becomes the instance entity, and other roots become its children.</remarks>
public sealed class PrefabDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public Guid Id { get; set; }

    /// <summary>The entities in hierarchy order: every child follows its parent.</summary>
    public List<EntityDocument> Entities { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public EntityDocument? Root => Entities.Find(e => e.Parent is null);

    public static PrefabDocument Create() => new() { Id = Guid.NewGuid() };

    public EntityDocument? FindEntity(Guid id) => Entities.Find(e => e.Id == id);

    public PrefabDocument Clone() => new()
    {
        Version = Version,
        Id = Id,
        Entities = Entities.ConvertAll(e => e.Clone()),
        Extra = EntityDocument.Clone(Extra)
    };
}

/// <summary>Scene-wide settings: background, ambient light, gravity and render layers.</summary>
public sealed class SceneEnvironment
{
    /// <summary>The color behind everything; null uses the game's clear color.</summary>
    public Color? ClearColor { get; set; }

    /// <summary>The light color of unlit areas, used by the lighting module.</summary>
    public Color AmbientLight { get; set; } = Color.White;

    public float AmbientIntensity { get; set; } = 1;

    /// <summary>World units per second squared, used by the physics module; Y points down.</summary>
    public Vector2 Gravity { get; set; } = new(0, 980);

    /// <summary>The named render layers the editor offers, in drawing order.</summary>
    public List<RenderLayerDefinition> RenderLayers { get; set; } = RenderLayerDefinition.CreateDefaults();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public SceneEnvironment Clone() => new()
    {
        ClearColor = ClearColor,
        AmbientLight = AmbientLight,
        AmbientIntensity = AmbientIntensity,
        Gravity = Gravity,
        RenderLayers = RenderLayers.ConvertAll(l => l.Clone()),
        Extra = EntityDocument.Clone(Extra)
    };
}

/// <summary>A named render layer number; lower layers are drawn first.</summary>
public sealed class RenderLayerDefinition
{
    public string Name { get; set; } = "";

    /// <summary>The layer number used by sprites and tile maps.</summary>
    public int Layer { get; set; }

    /// <summary>Whether lights affect the layer; unlit layers show their colors as they are.</summary>
    public bool Lit { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>The layers of <see cref="Talesmith.Rendering.RenderLayers"/>.</summary>
    public static List<RenderLayerDefinition> CreateDefaults() =>
    [
        new() { Name = "Background", Layer = Talesmith.Rendering.RenderLayers.Background },
        new() { Name = "Terrain", Layer = Talesmith.Rendering.RenderLayers.Terrain },
        new() { Name = "Decals", Layer = Talesmith.Rendering.RenderLayers.Decals },
        new() { Name = "Entities", Layer = Talesmith.Rendering.RenderLayers.Entities },
        new() { Name = "Effects", Layer = Talesmith.Rendering.RenderLayers.Effects },
        new() { Name = "Overlay", Layer = Talesmith.Rendering.RenderLayers.Overlay, Lit = false }
    ];

    public RenderLayerDefinition Clone() => new() { Name = Name, Layer = Layer, Lit = Lit, Extra = EntityDocument.Clone(Extra) };
}
