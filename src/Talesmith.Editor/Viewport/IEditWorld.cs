using Talesmith.Ecs;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Viewport;

/// <summary>The edit game's world, which mirrors the open scene: document entities map to runtime entities through their
/// <c>SceneEntityId</c>, and document changes are applied through the same component definitions the game uses.</summary>
/// <remarks>Use it on the UI thread. The world exists once the project opened and a scene was loaded into it.</remarks>
public interface IEditWorld
{
    /// <summary>The edit game whose world is shown, or null while the project is opening.</summary>
    Game? Game { get; }

    /// <summary>The world of the loaded scene, or null while none is loaded.</summary>
    World? World { get; }

    /// <summary>Whether a scene is being loaded into the world or changes are waiting for assets.</summary>
    bool IsBusy { get; }

    /// <summary>The number of entities in the world, including those created for prefab instances and tile map objects.</summary>
    int EntityCount { get; }

    /// <summary>Raised after the world changed: a scene loaded or document changes were applied.</summary>
    event EventHandler? Changed;

    /// <summary>Gets the runtime entity of a document entity.</summary>
    bool TryGetEntity(Guid documentId, out Entity entity);

    /// <summary>Gets the document entity a runtime entity belongs to: its own id, or that of the closest ancestor in the document, such as
    /// the instance root for entities of a prefab or the map entity for spawned map objects.</summary>
    bool TryGetDocumentId(Entity entity, out Guid documentId);

    /// <summary>Completes when the world reflects every change made so far.</summary>
    Task WhenIdle { get; }
}
