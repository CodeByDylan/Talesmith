using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Talesmith.Input;

/// <summary>Registers the input service with dependency injection.</summary>
public static class InputServiceCollectionExtensions
{
    /// <summary>Registers one <see cref="InputService"/> as itself, <see cref="IInputService"/> and <see cref="IInputSink"/>.</summary>
    /// <remarks>Requires an <see cref="Talesmith.Events.IEventBus"/> registration.</remarks>
    public static IServiceCollection AddTalesmithInput(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<InputService>();
        services.TryAddSingleton<IInputService>(provider => provider.GetRequiredService<InputService>());
        services.TryAddSingleton<IInputSink>(provider => provider.GetRequiredService<InputService>());
        return services;
    }
}
