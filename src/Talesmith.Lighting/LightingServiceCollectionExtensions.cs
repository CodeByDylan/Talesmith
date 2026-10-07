using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Authoring;
using Talesmith.Lighting.Systems;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;

namespace Talesmith.Lighting;

/// <summary>Registers 2D lighting with dependency injection.</summary>
public static class LightingServiceCollectionExtensions
{
    /// <summary>Adds the lighting components, a <see cref="LightingEnvironment"/> per scene and the systems that light every frame.</summary>
    public static IServiceCollection AddTalesmithLighting(this IServiceCollection services)
    {
        services.TryAddScoped<LightingEnvironment>();
        if (!services.Any(d => d.ImplementationType == typeof(SceneLightingListener)))
            services.AddSceneListener<SceneLightingListener>();
        services.AddComponent<Light2D>();
        services.AddComponent<ShadowCaster2D>();
        services.AddComponent<Emissive>();
        if (!services.Any(d => d.ServiceType == typeof(LightingSystem)))
        {
            services.AddSystem<LightAnimationSystem>();
            services.AddSystem<LightingSystem>();
        }

        return services;
    }
}
