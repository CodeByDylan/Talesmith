using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Fonts;
using Talesmith.Assets.Localization;
using Talesmith.Assets.Materials;
using Talesmith.Assets.Packs;
using Talesmith.Assets.Shaders;
using Talesmith.Assets.Textures;

namespace Talesmith.Assets;

/// <summary>Registers asset loading with dependency injection.</summary>
public static class AssetServiceCollectionExtensions
{
    /// <summary>
    /// Registers the folder as <see cref="IAssetSource"/> (through its <see cref="ContentPack"/> when a build wrote one), an
    /// <see cref="AssetCatalog"/> loaded from it, the <see cref="AssetManager"/>, the asset kinds, localization and the importers for
    /// textures, sprite atlases, fonts, materials, shaders and string tables.
    /// </summary>
    /// <remarks>
    /// The asset manager needs logging, registered with <c>AddLogging</c>. Services registered before this call win, so the editor can
    /// provide the asset database's live catalog as <see cref="AssetCatalog"/>.
    /// </remarks>
    public static IServiceCollection AddTalesmithAssets(this IServiceCollection services, string rootFolder)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(PackAssetSource.OpenFolder(rootFolder));
        services.TryAddSingleton(provider => AssetCatalog.Load(provider.GetRequiredService<IAssetSource>()));
        services.TryAddSingleton<IAssetCatalog>(provider => provider.GetRequiredService<AssetCatalog>());
        services.TryAddSingleton<IAssetMetaProvider>(provider => provider.GetRequiredService<AssetCatalog>());
        services.TryAddSingleton<AssetKindRegistry>();
        services.TryAddSingleton<IAssetManager, AssetManager>();
        services.TryAddSingleton(new LocalizationOptions());
        services.TryAddSingleton<ILocalization, LocalizationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, TextureImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, SpriteAtlasImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, FontImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, ShaderImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, MaterialImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, StringTableImporter>());
        return services;
    }
}
