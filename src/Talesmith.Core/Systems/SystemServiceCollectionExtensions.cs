using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Talesmith.Systems;

/// <summary>Registers systems with dependency injection.</summary>
public static class SystemServiceCollectionExtensions
{
    /// <summary>Registers a system for every scene; one instance is created per scene.</summary>
    public static IServiceCollection AddSystem<T>(this IServiceCollection services) where T : class, ISystem =>
        services.AddSystem(SystemDescriptor.For(typeof(T)));

    /// <summary>Registers a system with an explicit phase and order instead of its attributes.</summary>
    public static IServiceCollection AddSystem<T>(this IServiceCollection services, SystemPhase phase, int order = 0) where T : class, ISystem =>
        services.AddSystem(SystemDescriptor.For(typeof(T)) with { Phase = phase, Order = order, Modes = SystemDescriptor.ModesOf(typeof(T), phase) });

    public static IServiceCollection AddSystem(this IServiceCollection services, SystemDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        services.AddSingleton(descriptor);
        services.TryAdd(ServiceDescriptor.Scoped(descriptor.Type, descriptor.Type));
        return services;
    }
}
