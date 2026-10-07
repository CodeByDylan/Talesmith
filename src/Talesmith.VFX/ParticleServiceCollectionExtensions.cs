using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets;
using Talesmith.Authoring;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization.Converters;
using Talesmith.Systems;
using Talesmith.VFX.Presets;
using Talesmith.VFX.Rendering;
using Talesmith.VFX.Systems;

namespace Talesmith.VFX;

/// <summary>Registers the particle module with dependency injection.</summary>
public static class ParticleServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <see cref="ParticleEmitter"/> component, its simulation and render systems, the <c>.tparticles</c> importer and the
    /// particle services: <see cref="ParticleOptions"/>, <see cref="ParticleEnvironment"/>, <see cref="ParticleStats"/>, a
    /// <see cref="ParticleBudget"/> per scene and the <see cref="ParticleModuleRegistry"/> of plugin modules.
    /// </summary>
    /// <remarks>Calling it more than once has no further effect.</remarks>
    public static IServiceCollection AddTalesmithParticles(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(ParticleModuleRegistry)))
            return services;

        services.AddComponent<ParticleEmitter>();
        services.TryAddSingleton<ParticleOptions>();
        services.TryAddSingleton<ParticleEnvironment>();
        services.TryAddSingleton<ParticleStats>();
        services.TryAddSingleton<ParticleAssets>();
        services.TryAddSingleton(sp => new ParticleModuleRegistry(sp.GetServices<ParticleModuleRegistration>()));
        services.TryAddScoped(sp => new ParticleBudget(sp.GetRequiredService<ParticleOptions>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, ParticlePresetImporter>());
        services.AddValueConverter<ParticleModuleValueConverter>();
        services.AddValueConverter<MinMaxFloatValueConverter>();
        services.AddValueConverter<MinMaxColorValueConverter>();
        services.AddSceneListener<SceneParticleListener>();
        services.AddSystem<ParticleSimulationSystem>();
        services.AddSystem<ParticleRenderSystem>();
        return services;
    }
}
