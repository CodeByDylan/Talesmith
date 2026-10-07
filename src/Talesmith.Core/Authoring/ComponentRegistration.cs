using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Authoring;

/// <summary>A component type that scenes, prefabs and the editor may use.</summary>
public sealed record ComponentRegistration(Type Type);

/// <summary>Registers authorable components with dependency injection.</summary>
public static class ComponentServiceCollectionExtensions
{
    /// <summary>Makes a component available to scenes, prefabs and the editor's Add Component menu.</summary>
    public static IServiceCollection AddComponent<T>(this IServiceCollection services) => services.AddComponent(typeof(T));

    /// <summary>Makes a component available to scenes, prefabs and the editor; registering a type again does nothing.</summary>
    public static IServiceCollection AddComponent(this IServiceCollection services, Type type)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(type);
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ComponentRegistration) && descriptor.ImplementationInstance is ComponentRegistration existing && existing.Type == type)
                return services;
        }

        services.AddSingleton(new ComponentRegistration(type));
        return services;
    }
}
