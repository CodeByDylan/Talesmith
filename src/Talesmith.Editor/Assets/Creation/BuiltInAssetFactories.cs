using System.Text;
using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Database;
using Talesmith.Assets.Json;
using Talesmith.Assets.Materials;
using Talesmith.Editor.Documents;
using Talesmith.Runtime.Serialization;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Assets.Creation;

/// <summary>A scene with a camera.</summary>
public sealed class SceneFactory : IAssetFactory
{
    public string Title => "Scene";

    public AssetKind Kind => AssetKind.Scene;

    public string Group => "documents";

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context) =>
        context.Operations.CreateFileAsync(context.Folder, "New Scene.tscene", Encoding.UTF8.GetBytes(DocumentSerializer.Write(SceneDocumentService.CreateDefaultScene())));
}

/// <summary>A prefab with one empty entity.</summary>
public sealed class PrefabFactory : IAssetFactory
{
    public string Title => "Prefab";

    public AssetKind Kind => AssetKind.Prefab;

    public string Group => "documents";

    public int Order => 1;

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context)
    {
        var prefab = PrefabDocument.Create();
        prefab.Entities.Add(new EntityDocument
        {
            Id = Guid.NewGuid(),
            Name = "Root",
            Components = { new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }) }
        });
        return context.Operations.CreateFileAsync(context.Folder, "New Prefab.tprefab", Encoding.UTF8.GetBytes(DocumentSerializer.Write(prefab)));
    }
}

/// <summary>A material with alpha blending and no shader.</summary>
public sealed class MaterialFactory : IAssetFactory
{
    public string Title => "Material";

    public AssetKind Kind => AssetKind.Material;

    public string Group => "rendering";

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context) =>
        context.Operations.CreateFileAsync(context.Folder, "New Material.tmaterial", AssetJson.SerializeToUtf8Bytes(new MaterialDefinition()));
}

/// <summary>A sprite atlas, packing the selected textures and folders when there are any.</summary>
public sealed class SpriteAtlasFactory : IAssetFactory
{
    public string Title => "Sprite Atlas";

    public AssetKind Kind => AssetKind.Atlas;

    public string Group => "rendering";

    public int Order => 1;

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context)
    {
        var sources = context.Selection.Where(a => a.Kind == AssetKind.Texture || a.IsFolder).Select(a => a.Guid.ToString()).ToArray();
        return context.Operations.CreateFileAsync(context.Folder, "New Atlas.tatlas", AssetJson.SerializeToUtf8Bytes(new SpriteAtlasDefinition { Sources = sources }));
    }
}

/// <summary>A localization table with an example string.</summary>
public sealed class LocalizationTableFactory : IAssetFactory
{
    public string Title => "Localization Table";

    public AssetKind Kind => AssetKind.Localization;

    public string Group => "data";

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context)
    {
        var table = new JsonObject
        {
            ["version"] = 1,
            ["strings"] = new JsonObject { ["game.title"] = new JsonObject { ["en"] = context.Project.Project.Name } }
        };
        return context.Operations.CreateFileAsync(context.Folder, "Strings.tloc", Encoding.UTF8.GetBytes(table.ToJsonString(AssetJson.Options)));
    }
}

/// <summary>A particle preset from one of the built-in effects.</summary>
public sealed class ParticlePresetFactory(BuiltInParticlePreset preset, int order) : IAssetFactory
{
    public string Title => preset.Name;

    public string? Submenu => "Particle Preset";

    public AssetKind Kind => AssetKind.ParticleSystem;

    public string Group => "effects";

    public int Order => order;

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context) =>
        context.Operations.CreateFileAsync(context.Folder, $"{preset.Name}{ParticlePresetSerializer.Extension}",
            Encoding.UTF8.GetBytes(ParticlePresetSerializer.Serialize(preset.CreatePreset())));
}
