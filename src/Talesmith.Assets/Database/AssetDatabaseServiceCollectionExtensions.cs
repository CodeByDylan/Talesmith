using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Database.Dependencies;

namespace Talesmith.Assets.Database;

/// <summary>Registers the asset database with dependency injection, for the editor and the build.</summary>
public static class AssetDatabaseServiceCollectionExtensions
{
    /// <summary>Registers an <see cref="AssetDatabase"/> with the engine's dependency extractors and asset kinds.</summary>
    /// <remarks>
    /// The database reads importer ids and versions from the registered <see cref="IAssetImporter"/>s, so register importers too, for
    /// example with <c>AddTalesmithAssets</c>. Plugins add <see cref="IAssetDependencyExtractor"/>s for their own formats. Call
    /// <see cref="AssetDatabase.ScanAsync"/> before using it.
    /// </remarks>
    public static IServiceCollection AddTalesmithAssetDatabase(this IServiceCollection services, AssetDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AssetDatabase)))
            return services;

        services.AddSingleton(options);
        foreach (var extractor in DefaultDependencyExtractors.Create())
            services.AddSingleton(extractor);
        services.TryAddSingleton<AssetKindRegistry>();
        services.AddSingleton(provider => new AssetDatabase(
            provider.GetRequiredService<AssetDatabaseOptions>(),
            provider.GetServices<IAssetImporter>(),
            provider.GetRequiredService<ILogger<AssetDatabase>>(),
            provider.GetServices<IAssetDependencyExtractor>(),
            provider.GetRequiredService<AssetKindRegistry>()));
        return services;
    }
}
