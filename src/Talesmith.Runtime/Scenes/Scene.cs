using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Scenes;

/// <summary>A self-contained part of a game, such as a map, a menu or a battle, with its own entities and systems.</summary>
/// <remarks>
/// Scenes are created through dependency injection in their own service scope, so their constructors can ask for services, and every
/// registered system gets a fresh instance per scene. Override <see cref="LoadAsync"/> to load assets and create entities; it runs on the
/// game thread and may await asset loading while the previous scene is still shown.
/// </remarks>
public abstract class Scene
{
    public World World { get; internal set; } = null!;

    /// <summary>The scene's service scope.</summary>
    public IServiceProvider Services { get; internal set; } = null!;

    public SceneRequest Request { get; internal set; } = null!;

    /// <summary>The color behind everything; null uses the game's clear color.</summary>
    public Color? ClearColor { get; protected set; }

    /// <summary>Scene-wide settings such as ambient light and gravity, read by modules like lighting and physics.</summary>
    public SceneEnvironment Environment { get; protected set; } = new();

    /// <summary>Where <see cref="LoadAsync"/> reports how far it has come, which loading screens show.</summary>
    public SceneLoadProgress LoadProgress { get; internal set; } = new();

    /// <summary>Loads assets and creates the scene's entities before it becomes active.</summary>
    /// <remarks>Report the assets it loads to <see cref="LoadProgress"/>, so a loading screen can show how far it has come.</remarks>
    protected internal virtual Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Called once the scene is active and its systems have started.</summary>
    protected internal virtual void OnStarted()
    {
    }

    /// <summary>Called before the scene's systems stop and its entities are released.</summary>
    protected internal virtual void OnUnloading()
    {
    }
}
