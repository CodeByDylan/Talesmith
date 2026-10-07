using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Editor.DragAndDrop;
using Talesmith.Editor.Projects;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Talesmith.VFX;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Hierarchy;

/// <summary>A kind of entity the editor creates from the menus, such as a sprite or a spot light.</summary>
/// <param name="Id">A stable id, used in command ids such as <c>entity.create.light.spot</c>.</param>
/// <param name="Group">The submenu of the 2D Object menu, or null for the menu itself.</param>
public sealed record EntityTemplate(string Id, string Name, Geometry Icon, string? Group, string? Description, Func<IReadOnlyList<ComponentDocument>> Components);

/// <summary>The components of the entities the editor creates: empty entities, the 2D objects and entities made from dropped assets.</summary>
public sealed class EntityTemplates(IProjectService project)
{
    private IReadOnlyList<EntityTemplate>? _all;

    /// <summary>The 2D objects: sprite, camera, tile map, the three lights, one particle emitter per built-in preset and an audio source.</summary>
    public IReadOnlyList<EntityTemplate> All => _all ??= CreateAll();

    public EntityTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>A transform at a position, with its defaults.</summary>
    public static ComponentDocument Transform(Vector2 position) => new("Transform", new JsonObject { ["position"] = JsonFormats.WriteVector2(Round(position)) });

    /// <summary>The default data of a component type, or an empty object when the type is unknown.</summary>
    public JsonObject Default(string type) => Registry?.Find(type)?.CreateDefault() ?? [];

    /// <summary>The components of an entity made from a dropped asset, or null when the asset cannot make one.</summary>
    /// <remarks>Textures make sprites, <c>.hexy</c> maps tile maps, <c>.tparticles</c> presets particle emitters and sounds audio sources.</remarks>
    public IReadOnlyList<ComponentDocument>? FromAsset(DraggedAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var guid = asset.Guid.ToString();
        var kind = AssetKindRegistry.Default.Classify(asset.Path);
        if (kind == AssetKind.Texture)
            return [With("Sprite", "texture", guid)];
        if (kind == AssetKind.TileMap)
            return [With("TileMapRenderer", "map", guid)];
        if (kind == AssetKind.ParticleSystem)
            return [With(TypeName<ParticleEmitter>(), "preset", guid)];
        if (kind == AssetKind.Audio)
            return [With("AudioSource", "clip", guid)];
        return null;
    }

    private ComponentDocument With(string type, string key, string value)
    {
        var data = Default(type);
        data[key] = value;
        return new ComponentDocument(type, data);
    }

    private List<EntityTemplate> CreateAll()
    {
        var list = new List<EntityTemplate>
        {
            new("sprite", "Sprite", Icons.Image, null, "Draws a texture or one of its sprites.", () => [Component("Sprite")]),
            new("camera", "Camera", Icons.Camera, null, "Shows the scene; the active camera with the highest priority draws.",
                () => [new ComponentDocument("Camera", Default("Camera").Also(d => d["zoom"] ??= 1))]),
            new("tilemap", "Tile Map", Icons.Map, null, "Shows a .hexy tile map and spawns its objects.", () => [Component("TileMapRenderer")]),
            new("light.point", "Point Light", Icons.Lightbulb, "Light", "Shines in every direction from a point.", () => [Light("point")]),
            new("light.spot", "Spot Light", Icons.Flashlight, "Light", "Shines in a cone.", () => [Light("spot")]),
            new("light.directional", "Directional Light", Icons.Sun, "Light", "Lights everything from one direction, like the sun.", () => [Light("directional")]),
        };
        foreach (var preset in BuiltInParticlePresets.All)
        {
            list.Add(new EntityTemplate($"particles.{Slug(preset.Name)}", preset.Name, Icons.Sparkles, "Particle Emitter", preset.Description,
                () => [Particles(preset)]));
        }

        list.Add(new EntityTemplate("audio", "Audio Source", Icons.Volume, null, "Plays a sound or music track.", () => [Component("AudioSource")]));
        return list;
    }

    private ComponentDocument Component(string type) => new(type, Default(type));

    private ComponentDocument Light(string type)
    {
        var name = TypeName<Talesmith.Lighting.Light2D>();
        var data = Default(name);
        data["type"] = type;
        return new ComponentDocument(name, data);
    }

    private ComponentDocument Particles(BuiltInParticlePreset preset)
    {
        var name = TypeName<ParticleEmitter>();
        if (Registry?.Find(typeof(ParticleEmitter)) is ReflectionComponentDefinition<ParticleEmitter> definition)
            return new ComponentDocument(name, definition.CaptureValue(new ParticleEmitter(preset.Create()), NoReferences.Instance));
        return new ComponentDocument(name, Default(name));
    }

    private string TypeName<T>() => Registry?.Find(typeof(T))?.TypeName ?? ComponentRegistry.GetTypeName(typeof(T));

    private ComponentRegistry? Registry => project.EditSession?.Game.Services.GetService<ComponentRegistry>();

    private static string Slug(string name) => name.ToLowerInvariant().Replace(' ', '-');

    private static Vector2 Round(Vector2 value) => new(MathF.Round(value.X), MathF.Round(value.Y));

    /// <summary>Captures values that hold no asset or entity references.</summary>
    private sealed class NoReferences : ICaptureContext
    {
        public static NoReferences Instance { get; } = new();

        public AssetGuid GetGuid(object asset) => AssetGuid.Empty;

        public Guid GetEntityId(Entity entity) => Guid.Empty;
    }
}

internal static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject data, Action<JsonObject> change)
    {
        change(data);
        return data;
    }
}
