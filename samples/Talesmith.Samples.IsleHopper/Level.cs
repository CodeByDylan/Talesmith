using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;

namespace Talesmith.Samples.IsleHopper;

/// <summary>What a tile does to the hero, from the bool properties "solid", "oneWay" and "hazard" set on tiles in Hexy.</summary>
[Flags]
public enum TileTraits
{
    None = 0,

    /// <summary>Blocks movement from every side.</summary>
    Solid = 1,

    /// <summary>Can be stood on and jumped through from below, like a plank.</summary>
    OneWay = 2,

    /// <summary>Hurts when touched, like water and spikes.</summary>
    Hazard = 4
}

/// <summary>The playable map: the one with the hero's spawn point, placed where its map entity puts it.</summary>
/// <remarks>Other maps in the scene, such as the backdrop, are only scenery. Collision uses the level's rectangular cells.</remarks>
public sealed class Level
{
    public const string SpawnType = "spawn";

    private readonly Dictionary<(int Tileset, int Tile), TileTraits> _traits = [];

    private Level(Entity entity, TileMap map, Vector2 origin)
    {
        Entity = entity;
        Map = map;
        Origin = origin;
        Bounds = map.MeasureContentBounds().Offset(origin);
    }

    public Entity Entity { get; }

    public TileMap Map { get; }

    /// <summary>The world position of the center of cell (0, 0).</summary>
    public Vector2 Origin { get; }

    /// <summary>The world area covered by the level's tiles.</summary>
    public Rect2 Bounds { get; }

    public IGridLayout Layout => Map.Layout;

    /// <summary>Finds the map with a spawn point among the scene's maps, or null while none is loaded or when it does not use square cells.</summary>
    public static Level? Find(World world)
    {
        foreach (var archetype in world.Query<TileMapComponent, Transform>())
        {
            var entities = archetype.Entities;
            var maps = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < entities.Length; i++)
            {
                var map = maps[i].Map;
                if (map.Layout.Kind == GridKind.Square && map.ObjectLayers.Any(l => l.Objects.Any(o => o.Type == SpawnType)))
                    return new Level(entities[i], map, transforms[i].Position);
            }
        }

        return null;
    }

    public MapObject? FindObject(string type) => Map.ObjectLayers.SelectMany(l => l.Objects).FirstOrDefault(o => o.Type == type);

    /// <summary>The world rectangle of a cell.</summary>
    public Rect2 CellRect(GridCoord cell) => Layout.CellBounds(cell).Offset(Origin);

    /// <summary>The cell containing a world position.</summary>
    public GridCoord CellAt(Vector2 world) => Layout.WorldToCell(world - Origin);

    /// <summary>The combined traits of every visible tile in a cell.</summary>
    public TileTraits TraitsAt(GridCoord cell)
    {
        var traits = TileTraits.None;
        foreach (var layer in Map.TileLayers)
        {
            if (layer.IsVisible && layer.GetCell(cell) is { IsEmpty: false } tile)
                traits |= TraitsOf(tile);
        }

        return traits;
    }

    /// <summary>The cells overlapping a world rectangle, as an inclusive range.</summary>
    public (GridCoord Min, GridCoord Max) CellsIn(in Rect2 world)
    {
        const float inset = 0.01f;
        return (CellAt(new Vector2(world.Left + inset, world.Top + inset)), CellAt(new Vector2(world.Right - inset, world.Bottom - inset)));
    }

    private TileTraits TraitsOf(TileCell tile)
    {
        var key = (tile.TilesetId, tile.TileId);
        if (_traits.TryGetValue(key, out var traits))
            return traits;

        var properties = Map.FindTileset(tile.TilesetId)?.Find(tile.TileId)?.Properties ?? PropertySet.Empty;
        traits = (properties.GetBool("solid") ? TileTraits.Solid : 0) | (properties.GetBool("oneWay") ? TileTraits.OneWay : 0)
                | (properties.GetBool("hazard") ? TileTraits.Hazard : 0);
        _traits[key] = traits;
        return traits;
    }
}
