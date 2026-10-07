using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Runtime.Scenes;

/// <summary>Reacts to scenes starting and stopping, for work that does not need to run every frame, such as starting music.</summary>
/// <remarks>Listeners are created in each scene's service scope, so every scene gets fresh instances.</remarks>
public interface ISceneListener
{
    /// <summary>Called after the scene's systems started.</summary>
    void OnSceneStarted(Scene scene);

    /// <summary>Called before the scene's systems stop.</summary>
    void OnSceneStopping(Scene scene)
    {
    }
}

public static class SceneListenerServiceCollectionExtensions
{
    public static IServiceCollection AddSceneListener<T>(this IServiceCollection services) where T : class, ISceneListener =>
        services.AddScoped<ISceneListener, T>();
}
