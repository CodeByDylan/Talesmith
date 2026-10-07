using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Assets;

/// <summary>A category of asset, such as textures or scenes, used by the editor to show, filter and create assets.</summary>
/// <param name="Id">A stable identifier, such as "texture".</param>
/// <param name="Icon">The name of an icon from the editor's icon set, such as "image".</param>
/// <remarks>Plugins add kinds with <see cref="AssetKindServiceCollectionExtensions.AddAssetKind"/>.</remarks>
public sealed record AssetKind(string Id, string DisplayName, string Icon)
{
    public static AssetKind Texture { get; } = new("texture", "Texture", "image");

    public static AssetKind Atlas { get; } = new("atlas", "Sprite Atlas", "image");

    public static AssetKind Font { get; } = new("font", "Font", "type");

    public static AssetKind Audio { get; } = new("audio", "Audio", "music");

    public static AssetKind Scene { get; } = new("scene", "Scene", "clapperboard");

    public static AssetKind Prefab { get; } = new("prefab", "Prefab", "package");

    public static AssetKind Material { get; } = new("material", "Material", "file");

    public static AssetKind Shader { get; } = new("shader", "Shader", "file-code");

    public static AssetKind ParticleSystem { get; } = new("particles", "Particle System", "sparkles");

    public static AssetKind TileMap { get; } = new("tilemap", "Tile Map", "map");

    public static AssetKind Script { get; } = new("script", "Script", "file-code");

    public static AssetKind PluginManifest { get; } = new("plugin", "Plugin Manifest", "plug");

    public static AssetKind Localization { get; } = new("localization", "Localization", "languages");

    public static AssetKind Folder { get; } = new("folder", "Folder", "folder");

    public static AssetKind Other { get; } = new("other", "File", "file");

    public override string ToString() => DisplayName;
}

/// <summary>Assigns an <see cref="AssetKind"/> to files with the given extensions.</summary>
/// <param name="Extensions">Lower-case extensions including the dot, such as ".png".</param>
public sealed record AssetKindRegistration(AssetKind Kind, IReadOnlyList<string> Extensions);

/// <summary>Classifies asset files by extension into <see cref="AssetKind"/>s.</summary>
/// <remarks>Built-in kinds come first; registrations are applied in order, so a later registration of an extension wins.</remarks>
public sealed class AssetKindRegistry
{
    private readonly Dictionary<string, AssetKind> _byExtension = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AssetKind> _kinds = [];

    public AssetKindRegistry(IEnumerable<AssetKindRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        foreach (var registration in BuiltIn.Concat(registrations))
        {
            if (!_kinds.Contains(registration.Kind))
                _kinds.Add(registration.Kind);
            foreach (var extension in registration.Extensions)
                _byExtension[extension] = registration.Kind;
        }

        if (!_kinds.Contains(AssetKind.Folder))
            _kinds.Add(AssetKind.Folder);
        if (!_kinds.Contains(AssetKind.Other))
            _kinds.Add(AssetKind.Other);
    }

    /// <summary>The built-in kinds and their extensions.</summary>
    public static IReadOnlyList<AssetKindRegistration> BuiltIn { get; } =
    [
        new(AssetKind.Texture, [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico"]),
        new(AssetKind.Atlas, [".tatlas"]),
        new(AssetKind.Font, [".ttf", ".otf", ".ttc"]),
        new(AssetKind.Audio, [".wav", ".ogg"]),
        new(AssetKind.Scene, [".tscene"]),
        new(AssetKind.Prefab, [".tprefab"]),
        new(AssetKind.Material, [".tmaterial"]),
        new(AssetKind.Shader, [".tshader", ".sksl", ".spv"]),
        new(AssetKind.ParticleSystem, [".tparticles"]),
        new(AssetKind.TileMap, [".hexy"]),
        new(AssetKind.Script, [".cs"]),
        new(AssetKind.PluginManifest, [".tplugin"]),
        new(AssetKind.Localization, [".tloc"])
    ];

    /// <summary>A registry with the built-in kinds only.</summary>
    public static AssetKindRegistry Default { get; } = new([]);

    /// <summary>Every known kind, built-in first, including <see cref="AssetKind.Folder"/> and <see cref="AssetKind.Other"/>.</summary>
    public IReadOnlyList<AssetKind> Kinds => _kinds;

    /// <summary>The kind of a file by its extension, or <see cref="AssetKind.Other"/>.</summary>
    public AssetKind Classify(string path) => _byExtension.GetValueOrDefault(AssetPath.GetExtension(path), AssetKind.Other);

    /// <summary>The extensions classified as <paramref name="kind"/>.</summary>
    public IReadOnlyList<string> GetExtensions(AssetKind kind) =>
        [.. _byExtension.Where(pair => pair.Value == kind).Select(pair => pair.Key).Order(StringComparer.Ordinal)];
}

/// <summary>Registers asset kinds with dependency injection.</summary>
public static class AssetKindServiceCollectionExtensions
{
    /// <summary>Classifies files with <paramref name="extensions"/> as <paramref name="kind"/>, replacing earlier classifications.</summary>
    public static IServiceCollection AddAssetKind(this IServiceCollection services, AssetKind kind, params string[] extensions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(kind);
        services.AddSingleton(new AssetKindRegistration(kind, [.. extensions.Select(extension => extension.ToLowerInvariant())]));
        return services;
    }
}
