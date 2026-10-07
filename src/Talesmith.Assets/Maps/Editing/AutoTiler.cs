using Talesmith.Grids;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>Paints terrains with automatic transitions: each terrain cell picks its tile from which neighbors share its terrain.</summary>
/// <remarks>
/// Gives the same tiles as the Hexy editor's auto-tiler. Painting writes the terrain's base tile
/// into the painted cells, then re-resolves them and their neighbors with the rules of whatever terrain each cell belongs to. Rules
/// written for another grid never match. Works on hex grids of either orientation and on square grids.
/// </remarks>
public static class AutoTiler
{
    /// <summary>Paints <paramref name="terrain"/>, or erases terrain when it is null, into <paramref name="cells"/> through <paramref name="edit"/>.</summary>
    public static void Paint(TileEdit edit, TileLayer layer, IEnumerable<GridCoord> cells, Terrain? terrain)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(layer);
        var changes = ComputeChanges(layer, edit.Map.Terrains, edit.Map.Layout.Topology, cells, terrain);
        if (changes.Count > 0)
            edit.SetMany(layer, changes);
    }

    /// <summary>Computes every cell whose tile changes when painting or erasing terrain, including transitions in surrounding cells.</summary>
    /// <param name="terrains">Every terrain of the map, used to re-resolve neighbors of other terrains.</param>
    public static Dictionary<GridCoord, TileCell> ComputeChanges(TileLayer layer, IReadOnlyList<Terrain> terrains, GridTopology topology, IEnumerable<GridCoord> cells,
        Terrain? terrain)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(terrains);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(cells);

        var painted = terrain?.BaseTile ?? TileCell.Empty;
        var overlay = new Dictionary<GridCoord, TileCell>();
        foreach (var cell in cells)
            overlay[cell] = painted;

        var affected = new CellSet();
        foreach (var cell in overlay.Keys)
        {
            affected.Add(cell);
            foreach (var direction in topology.Directions)
                affected.Add(cell + direction);
        }

        var changes = new Dictionary<GridCoord, TileCell>();
        foreach (var cell in affected)
        {
            var current = Lookup(layer, overlay, cell);
            var owner = terrain is not null && terrain.Contains(current) ? terrain : FindByTile(terrains, current);
            var resolved = owner is null ? current : Resolve(owner, topology, cell, layer, overlay);
            if (resolved != layer.GetCell(cell))
                changes[cell] = resolved;
        }

        return changes;
    }

    /// <summary>Resolves the tile of a terrain cell from the tiles around it.</summary>
    public static TileCell Resolve(Terrain terrain, GridTopology topology, GridCoord cell, Func<GridCoord, TileCell> tileAt)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(tileAt);
        var mask = 0;
        for (var d = 0; d < topology.NeighborCount; d++)
        {
            if (terrain.Contains(tileAt(cell + topology.Direction(d))))
                mask |= 1 << d;
        }

        return Choose(terrain, topology, cell, mask);
    }

    private static TileCell Resolve(Terrain terrain, GridTopology topology, GridCoord cell, TileLayer layer, Dictionary<GridCoord, TileCell> overlay)
    {
        var mask = 0;
        for (var d = 0; d < topology.NeighborCount; d++)
        {
            if (terrain.Contains(Lookup(layer, overlay, cell + topology.Direction(d))))
                mask |= 1 << d;
        }

        return Choose(terrain, topology, cell, mask);
    }

    private static TileCell Choose(Terrain terrain, GridTopology topology, GridCoord cell, int mask)
    {
        foreach (var rule in terrain.Rules)
        {
            if (rule.NeighborCount == topology.NeighborCount && rule.TryMatch(mask, out var rotation))
                return new TileCell(terrain.TilesetId, rule.PickTile(Sample(cell)), rotation % topology.RotationSteps);
        }

        return terrain.BaseTile;
    }

    private static Terrain? FindByTile(IReadOnlyList<Terrain> terrains, TileCell tile)
    {
        if (tile.IsEmpty)
            return null;
        foreach (var terrain in terrains)
        {
            if (terrain.Contains(tile))
                return terrain;
        }

        return null;
    }

    private static TileCell Lookup(TileLayer layer, Dictionary<GridCoord, TileCell> overlay, GridCoord cell) =>
        overlay.TryGetValue(cell, out var tile) ? tile : layer.GetCell(cell);

    /// <summary>The same per-cell sample as Hexy, so random rule tiles match between the editors.</summary>
    private static double Sample(GridCoord cell)
    {
        var h = (uint)(cell.X * 73856093) ^ (uint)(cell.Y * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }
}
