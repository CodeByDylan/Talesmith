using Talesmith.Rendering;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Runtime.Hosting;

/// <summary>Raised by a host, such as the editor, when a game starts playing a scene.</summary>
public readonly record struct PlayModeEntered(SceneRequest Scene);

/// <summary>Raised by a host, such as the editor, when play mode stopped.</summary>
public readonly record struct PlayModeExited;

/// <summary>Raised when <see cref="Game.IsPaused"/> changes.</summary>
public readonly record struct PauseStateChanged(bool IsPaused);

/// <summary>Raised by a host on the game's first frame with the renderer it chose, and again when frames start reaching the window another way.</summary>
/// <param name="Previous">The renderer used before, or null when this is the first; the same as <paramref name="Current"/> when only the way frames reach the window changed.</param>
/// <param name="Reason">Why the backend was chosen or changed, for logs and diagnostics.</param>
public readonly record struct RenderBackendChanged(RendererInfo? Previous, RendererInfo Current, string Reason);
