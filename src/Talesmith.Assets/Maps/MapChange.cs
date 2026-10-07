using Talesmith.Grids;

namespace Talesmith.Assets.Maps;

/// <summary>What part of a map changed.</summary>
public enum MapChangeKind
{
    /// <summary>Cells of <see cref="MapChange.Layer"/> within <see cref="MapChange.Cells"/> changed.</summary>
    Cells,

    /// <summary><see cref="MapChange.Layer"/> was inserted at <see cref="MapChange.Index"/>.</summary>
    LayerAdded,

    /// <summary><see cref="MapChange.Layer"/> was removed from <see cref="MapChange.Index"/>.</summary>
    LayerRemoved,

    /// <summary><see cref="MapChange.Layer"/> moved from <see cref="MapChange.PreviousIndex"/> to <see cref="MapChange.Index"/>.</summary>
    LayerMoved,

    /// <summary>The name, role, visibility, lock, opacity, color or properties of <see cref="MapChange.Layer"/> changed.</summary>
    LayerChanged,

    /// <summary>The object <see cref="MapChange.ObjectId"/> of the object layer <see cref="MapChange.Layer"/> was added, removed, moved or edited.</summary>
    Objects,

    /// <summary><see cref="MapChange.Tileset"/> was inserted at <see cref="MapChange.Index"/>.</summary>
    TilesetAdded,

    /// <summary><see cref="MapChange.Tileset"/> was removed from <see cref="MapChange.Index"/>.</summary>
    TilesetRemoved,

    /// <summary><see cref="MapChange.Tileset"/> changed: its settings, or the data of tile <see cref="MapChange.TileId"/> when that is not -1.</summary>
    TilesetChanged,

    /// <summary>A terrain was added, removed or replaced.</summary>
    Terrains,

    /// <summary>The map's properties or background color changed.</summary>
    Map
}

/// <summary>Describes one change to a <see cref="TileMap"/>, raised by <see cref="TileMap.Changed"/>.</summary>
/// <param name="Cells">For <see cref="MapChangeKind.Cells"/>, the bounds of every changed cell; otherwise empty.</param>
public readonly record struct MapChange(
    MapChangeKind Kind,
    MapLayer? Layer = null,
    Tileset? Tileset = null,
    GridBounds Cells = default,
    int Index = -1,
    int PreviousIndex = -1,
    int ObjectId = 0,
    int TileId = -1);
