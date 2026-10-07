using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Talesmith.Assets.Hexy;

/// <summary>Registers the .hexy map importer with dependency injection.</summary>
public static class HexyServiceCollectionExtensions
{
    /// <summary>Lets the asset manager load .hexy files as <see cref="Maps.TileMap"/>s.</summary>
    public static IServiceCollection AddHexyMaps(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, HexyMapImporter>());
        return services;
    }
}
