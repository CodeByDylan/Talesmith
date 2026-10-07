using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets;
using Talesmith.Audio.Importing;

namespace Talesmith.Audio;

/// <summary>Registers audio with dependency injection.</summary>
public static class AudioServiceCollectionExtensions
{
    /// <summary>Registers the sound clip and music track importers and, unless a backend is already registered, the <see cref="SilentAudioService"/>.</summary>
    public static IServiceCollection AddTalesmithAudio(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, SoundClipImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, MusicTrackImporter>());
        services.TryAddSingleton<IAudioService, SilentAudioService>();
        return services;
    }
}
