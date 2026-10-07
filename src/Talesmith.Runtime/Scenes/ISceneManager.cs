namespace Talesmith.Runtime.Scenes;

/// <summary>Loads, switches and unloads scenes.</summary>
public interface ISceneManager
{
    /// <summary>The active scene, or null before the first scene loaded.</summary>
    Scene? Current { get; }

    /// <summary>Whether a scene is loading, from the start of the current scene's fade-out until the new scene has faded in.</summary>
    bool IsLoading { get; }

    /// <summary>How far the scene being loaded has come, or null when no scene is loading, including during the fades around a load.</summary>
    /// <remarks>Safe to read from any thread, such as a loading screen's.</remarks>
    SceneLoadProgress? LoadProgress { get; }

    /// <summary>The names of every registered scene.</summary>
    IReadOnlyCollection<string> RegisteredScenes { get; }

    /// <summary>Loads a scene and makes it active, fading out the current one; returns when the new scene is active.</summary>
    /// <exception cref="InvalidOperationException">No scene is registered with the requested name.</exception>
    Task LoadAsync(SceneRequest request, SceneTransition? transition = null, CancellationToken cancellationToken = default);

    /// <summary>Loads the current scene again with the same request.</summary>
    Task ReloadAsync(SceneTransition? transition = null);
}
