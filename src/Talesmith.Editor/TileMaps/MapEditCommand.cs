using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.Undo;

namespace Talesmith.Editor.TileMaps;

/// <summary>An undo step for a map edit: keeps the edit that reverses the current state and swaps it on every undo and redo.</summary>
/// <remarks>The map itself is the document, so its unsaved state follows the undo history.</remarks>
internal sealed class MapEditCommand : IUndoableCommand
{
    private readonly TileMap _map;
    private IMapEdit _next;
    private bool _applied;

    private MapEditCommand(string description, TileMap map, IMapEdit next, bool applied)
    {
        Description = description;
        _map = map;
        _next = next;
        _applied = applied;
    }

    public string Description { get; }

    public object? Document => _map;

    /// <summary>Wraps an edit that was already applied, given the edit that reverts it.</summary>
    public static MapEditCommand Applied(string description, TileMap map, IMapEdit inverse) => new(description, map, inverse, applied: true);

    /// <summary>Wraps an edit that applies when the command is executed.</summary>
    public static MapEditCommand Pending(string description, TileMap map, IMapEdit edit) => new(description, map, edit, applied: false);

    public void Apply()
    {
        if (_applied)
            return;
        _next = _next.Apply(_map);
        _applied = true;
    }

    public void Revert()
    {
        if (!_applied)
            return;
        _next = _next.Apply(_map);
        _applied = false;
    }
}

/// <summary>Marks the open scene as changed along with a map it shows, so the scene's unsaved state, title and saving include its maps.</summary>
internal sealed class SceneTouch(object scene) : IUndoableCommand
{
    public string Description => "";

    public object? Document { get; } = scene;

    public void Apply()
    {
    }

    public void Revert()
    {
    }
}
