using Microsoft.Extensions.DependencyInjection;
using Talesmith.Authoring;
using Talesmith.Systems;

namespace Talesmith.Runtime.Audio;

/// <summary>Registers audio components with dependency injection.</summary>
public static class AudioSourceServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="AudioSource"/> and <see cref="AudioListener"/> components and the <see cref="AudioSourceSystem"/>.</summary>
    public static IServiceCollection AddAudioSources(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AudioSourceSystem)))
            return services;

        services.AddComponent<AudioSource>();
        services.AddComponent<AudioListener>();
        services.AddSystem<AudioSourceSystem>();
        return services;
    }
}
