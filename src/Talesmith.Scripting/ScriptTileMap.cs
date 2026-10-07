using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Runtime.Components;

namespace Talesmith.Scripting;

/// <summary>A tile map shown by an entity, with conversions between world positions and cells; see <see cref="Script.GetTileMap()"/>.</summary>
/// <remarks>
/// Positions are in world units and account for where the map entity is placed. Changing cells updates rendering and collision of the
/// changed chunks on the next frame.
/// </remarks>
public readonly struct ScriptTileMap
{
    private readonly World _world;

    internal ScriptTileMap(World world, Entity entity, TileMap map)
    {
        _world = world;
        Entity = entity;
        Map = map;
    }

    /// <summary>The entity showing the map.</summary>
    public Entity Entity { get; }

    /// <summary>The map itself, for layers, tilesets, objects and properties.</summary>
    public TileMap Map { get; }

    /// <summary>The world position of cell (0, 0), from the map entity's transform.</summary>
    public Vector2 Origin => _world.IsAlive(Entity) && _world.TryGet<Transform>(Entity, out var transform) ? transform.Position : Vector2.Zero;

    /// <summary>The cell containing a world position.</summary>
    public GridCoord WorldToCell(Vector2 world) => Map.Layout.WorldToCell(world - Origin);

    /// <summary>The world position of a cell's center.</summary>
    public Vector2 CellToWorld(GridCoord cell) => Map.Layout.CellToWorld(cell) + Origin;

    /// <summary>Finds a tile layer by name.</summary>
    /// <exception cref="ArgumentException">The map has no tile layer with this name.</exception>
    public TileLayer Layer(string name) =>
        Map.FindLayer(name) as TileLayer ?? throw new ArgumentException($"The map {Map.Path} has no tile layer named '{name}'.", nameof(name));

    /// <summary>The tile in a cell of a layer, or <see cref="TileCell.Empty"/>.</summary>
    public TileCell GetCell(string layer, GridCoord cell) => Layer(layer).GetCell(cell);

    /// <summary>Changes a cell of a layer; <see cref="TileCell.Empty"/> clears it.</summary>
    public void SetCell(string layer, GridCoord cell, TileCell tile) => Layer(layer).SetCell(cell, tile);

    /// <summary>The topmost non-empty tile of a cell across all layers, or <see cref="TileCell.Empty"/>.</summary>
    public TileCell TopTileAt(GridCoord cell) => Map.TopTileAt(cell);
}
