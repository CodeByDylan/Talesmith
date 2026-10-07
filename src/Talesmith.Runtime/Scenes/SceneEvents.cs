namespace Talesmith.Runtime.Scenes;

/// <summary>Raised when a scene starts loading.</summary>
public readonly record struct SceneLoading(SceneRequest Request);

/// <summary>Raised when a scene became active.</summary>
public readonly record struct SceneLoaded(SceneRequest Request, Scene Scene);

/// <summary>Raised after a scene was unloaded.</summary>
public readonly record struct SceneUnloaded(SceneRequest Request);

/// <summary>Raised when a scene failed to load; the previous scene, if any, stays active.</summary>
public readonly record struct SceneLoadFailed(SceneRequest Request, Exception Error);
