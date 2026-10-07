using Talesmith.Assets.Maps;
using Talesmith.Grids;

namespace Talesmith.Samples.HexQuest;

/// <summary>Decides where the hero may walk: onto any tile whose tileset entry does not set the bool property "blocked".</summary>
public static class Walkability
{
    public const string BlockedProperty = "blocked";

    public static bool CanEnter(TileMap map, GridCoord cell)
    {
        var tile = map.TopTileAt(cell);
        if (tile.IsEmpty)
            return false;
        return map.FindTileset(tile.TilesetId)?.Find(tile.TileId)?.Properties.GetBool(BlockedProperty) != true;
    }

    /// <summary>The cost function for <see cref="GridPathfinder"/>.</summary>
    public static CellCost CostOn(TileMap map) => cell => CanEnter(map, cell) ? 1 : float.PositiveInfinity;
}
