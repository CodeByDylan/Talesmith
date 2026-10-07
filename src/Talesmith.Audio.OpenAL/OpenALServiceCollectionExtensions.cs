using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Talesmith.Audio.OpenAL;

/// <summary>Registers the OpenAL audio backend with dependency injection.</summary>
public static partial class OpenALServiceCollectionExtensions
{
    /// <summary>Registers audio with <see cref="OpenALAudioService"/> as the <see cref="IAudioService"/>.</summary>
    /// <remarks>When no device can be opened, the service falls back to <see cref="SilentAudioService"/> and logs a warning if logging is registered.</remarks>
    public static IServiceCollection AddOpenALAudio(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton(CreateAudioService));
        return services.AddTalesmithAudio();
    }

    private static IAudioService CreateAudioService(IServiceProvider services)
    {
        var logger = services.GetService<ILogger<OpenALAudioService>>();
        try
        {
            return new OpenALAudioService(logger);
        }
        catch (AudioDeviceException ex)
        {
            if (logger is not null)
                LogNoDevice(logger, ex);
            return new SilentAudioService();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No audio device could be opened, so sound is disabled")]
    private static partial void LogNoDevice(ILogger logger, Exception exception);
}
