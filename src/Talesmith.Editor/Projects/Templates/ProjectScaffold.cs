using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using Talesmith.Assets;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Editor.ProjectSettings;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Projects.Templates;

/// <summary>Writes the files of a new project: settings, folders, images, maps and scenes, each asset with its <c>.meta</c> file.</summary>
public sealed class ProjectScaffold
{
    /// <summary>The asset folders every project starts with.</summary>
    public static IReadOnlyList<string> StandardFolders { get; } = ["scenes", "sprites", "audio", "maps", "prefabs", "scripts", "config"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProjectScaffold(string folder, string name)
    {
        Folder = folder;
        Name = name;
        AssetRoot = Path.Combine(folder, EditorProject.AssetFolderName);
    }

    public string Folder { get; }

    /// <summary>The project's display name, written as the game's title.</summary>
    public string Name { get; }

    public string AssetRoot { get; }

    /// <summary>Creates the project folder, the standard asset folders and the ignore file for editor state.</summary>
    public void CreateStructure()
    {
        foreach (var folder in StandardFolders)
            Directory.CreateDirectory(Path.Combine(AssetRoot, folder));
        File.WriteAllText(Path.Combine(Folder, ".gitignore"), ".talesmith/\nbin/\nobj/\n");
    }

    /// <summary>Writes <c>config/game.json</c> for a 1280×720 window.</summary>
    public Task WriteSettingsAsync(string startScene, string clearColor, ViewSettings view, string textureFilter = "nearest",
        CancellationToken cancellationToken = default)
    {
        var settings = new JsonObject
        {
            ["title"] = Name,
            ["windowWidth"] = 1280,
            ["windowHeight"] = 720,
            ["view"] = ViewSettingsJson.Create(view),
            ["renderer"] = "auto",
            ["fixedUpdateRate"] = 60,
            ["textureFilter"] = textureFilter,
            ["clearColor"] = clearColor,
            ["startScene"] = startScene,
            ["inputProfile"] = "config/input.json",
            ["pluginsFolder"] = "plugins",
            ["pauseWhenInactive"] = true
        };
        return WriteTextAsync(GameSettings.FileName, settings.ToJsonString(JsonOptions), cancellationToken);
    }

    /// <summary>Writes the input profile from action definitions in the format of <c>config/input.json</c>.</summary>
    public Task WriteInputProfileAsync(JsonObject actions, CancellationToken cancellationToken = default) =>
        WriteTextAsync("config/input.json", new JsonObject { ["actions"] = actions }.ToJsonString(JsonOptions), cancellationToken);

    public async Task WriteTextAsync(string assetPath, string text, CancellationToken cancellationToken = default)
    {
        var path = Absolute(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, cancellationToken);
    }

    /// <summary>Writes a PNG image and its meta with the given import settings; returns its guid.</summary>
    public async Task<AssetGuid> WriteImageAsync(string assetPath, SKBitmap image, TextureImportSettings? settings = null, CancellationToken cancellationToken = default)
    {
        var path = Absolute(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, TemplateArt.EncodePng(image), cancellationToken);
        return await WriteMetaAsync(assetPath, TextureImporter.ImporterId, settings, cancellationToken);
    }

    /// <summary>Writes a tile map as a .hexy file; returns its guid.</summary>
    public async Task<AssetGuid> WriteMapAsync(string assetPath, TileMap map, CancellationToken cancellationToken = default)
    {
        await HexyMapWriter.SaveAsync(map, Absolute(assetPath), cancellationToken);
        return await WriteMetaAsync(assetPath, null, null, cancellationToken);
    }

    /// <summary>Writes a scene file; returns its guid.</summary>
    public async Task<AssetGuid> WriteSceneAsync(string assetPath, SceneDocument scene, CancellationToken cancellationToken = default)
    {
        await WriteTextAsync(assetPath, DocumentSerializer.Write(scene), cancellationToken);
        return await WriteMetaAsync(assetPath, null, null, cancellationToken);
    }

    /// <summary>A texture tileset made from an image in memory, which the map file stores.</summary>
    public static Tileset CreateTileset(string name, SKBitmap image, int tileWidth, int tileHeight)
    {
        var pixels = image.Copy(SKColorType.Rgba8888);
        var data = new Imaging.ImageData(pixels.Width, pixels.Height, pixels.Bytes);
        var texture = new TextureAsset($"{name}.png", data);
        return Tileset.FromImage(name, texture, TilesetImageFile.FromBytes($"assets/{name}.png", TemplateArt.EncodePng(image)), tileWidth, tileHeight);
    }

    private async Task<AssetGuid> WriteMetaAsync(string assetPath, string? importer, object? settings, CancellationToken cancellationToken)
    {
        var guid = AssetGuid.NewGuid();
        var meta = new AssetMeta(guid) { Importer = importer, ImporterVersion = importer is null ? 0 : 1 };
        if (settings is not null)
            meta = meta.WithSettings(settings);
        await AssetMetaFile.WriteAsync(AssetMetaFile.GetMetaPath(Absolute(assetPath)), meta, cancellationToken);
        return guid;
    }

    private string Absolute(string assetPath) => Path.Combine(AssetRoot, AssetPath.Normalize(assetPath));
}

/// <summary>Builds the entities of a template's starter scene.</summary>
public sealed class SceneBuilder
{
    private readonly SceneDocument _scene = SceneDocument.Create();

    public SceneBuilder(string? clearColor = null)
    {
        if (clearColor is not null)
            _scene.Environment.ClearColor = Mathematics.Color.Parse(clearColor);
    }

    public SceneDocument Document => _scene;

    public EntityDocument Entity(string name, Vector2 position, EntityDocument? parent = null)
    {
        var entity = new EntityDocument { Id = Guid.NewGuid(), Name = name, Parent = parent?.Id };
        entity.Components.Add(new ComponentDocument("Transform", new JsonObject { ["position"] = Vector(position) }));
        var index = parent is null ? _scene.Entities.Count : LastDescendantIndex(parent) + 1;
        _scene.Entities.Insert(index, entity);
        return entity;
    }

    public static void AddCamera(EntityDocument entity, float zoom) =>
        entity.Components.Add(new ComponentDocument("Camera", new JsonObject { ["zoom"] = zoom, ["pixelSnap"] = true }));

    public static void AddSprite(EntityDocument entity, AssetGuid texture, string? sprite, int layer, Vector2? origin = null)
    {
        var data = new JsonObject { ["texture"] = texture.ToString(), ["layer"] = layer };
        if (sprite is not null)
            data["sprite"] = sprite;
        if (origin is { } value)
            data["origin"] = Vector(value);
        entity.Components.Add(new ComponentDocument("Sprite", data));
    }

    public static void AddAnimator(EntityDocument entity, AssetGuid texture, string animation) =>
        entity.Components.Add(new ComponentDocument("SpriteAnimator", new JsonObject { ["texture"] = texture.ToString(), ["animation"] = animation }));

    public static void AddTileMap(EntityDocument entity, AssetGuid map, int renderLayer) =>
        entity.Components.Add(new ComponentDocument("TileMapRenderer", new JsonObject { ["map"] = map.ToString(), ["renderLayer"] = renderLayer }));

    private static JsonArray Vector(Vector2 value) => [value.X, value.Y];

    private int LastDescendantIndex(EntityDocument parent)
    {
        var index = _scene.Entities.IndexOf(parent);
        var ids = new HashSet<Guid> { parent.Id };
        for (var i = index + 1; i < _scene.Entities.Count && _scene.Entities[i].Parent is { } p && ids.Contains(p); i++)
        {
            ids.Add(_scene.Entities[i].Id);
            index = i;
        }

        return index;
    }
}
