using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Runtime.Scenes;

/// <summary>Maps a scene name to its type.</summary>
public sealed record SceneRegistration(string Name, Type Type);

/// <summary>Registers scenes with dependency injection.</summary>
public static class SceneServiceCollectionExtensions
{
    /// <summary>Registers a scene that <see cref="ISceneManager.LoadAsync"/> can load by <paramref name="name"/>.</summary>
    public static IServiceCollection AddScene<T>(this IServiceCollection services, string name) where T : Scene
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        services.AddSingleton(new SceneRegistration(name, typeof(T)));
        services.AddScoped<T>();
        return services;
    }
}
