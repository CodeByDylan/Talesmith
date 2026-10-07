using Talesmith.Grids;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>Which cells a fill or magic-wand selection treats as part of the clicked region.</summary>
public enum FillMatch
{
    /// <summary>Cells with exactly the clicked tile, including its orientation; empty cells when the clicked cell is empty.</summary>
    SameTile,

    /// <summary>Cells with the clicked tile in any orientation.</summary>
    SameTileAnyOrientation,

    /// <summary>Cells of the clicked cell's terrain; falls back to <see cref="SameTile"/> when the clicked tile belongs to no terrain.</summary>
    SameTerrain
}

/// <summary>Finds the regions fill tools replace and magic wands select, bounded by a cell limit because empty space is unbounded.</summary>
public static class TileFill
{
    /// <summary>The largest region fill tools collect by default, matching Hexy.</summary>
    public const int DefaultLimit = 1_000_000;

    /// <summary>Adds the cells connected to <paramref name="start"/> that match its tile to <paramref name="output"/>.</summary>
    /// <param name="within">Limits the region to these cells, such as the current selection; null for no limit.</param>
    /// <returns>When <see cref="FloodFillResult.ReachedLimit"/> is set the region is incomplete and tools should not fill it.</returns>
    public static FloodFillResult Contiguous(TileMap map, TileLayer layer, GridCoord start, FillMatch match, int maxCells, CellSet output, CellSet? within = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layer);
        if (within is not null && !within.Contains(start))
            return default;
        var predicate = new Matcher(map, layer, layer.GetCell(start), match, within);
        return FloodFill.Collect(map.Layout.Topology, start, ref predicate, maxCells, output, cancellationToken);
    }

    /// <summary>Adds every cell of the layer that matches <paramref name="target"/>, connected or not.</summary>
    /// <remarks>Empty cells are only collected within a selection; without one the result is empty and reports the limit as reached.</remarks>
    public static FloodFillResult Global(TileMap map, TileLayer layer, TileCell target, FillMatch match, int maxCells, CellSet output, CellSet? within = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCells);
        var predicate = new Matcher(map, layer, target, match, within);
        var count = 0;
        if (target.IsEmpty)
        {
            if (within is null)
                return new FloodFillResult(0, true);
            foreach (var cell in within)
            {
                if (!predicate.Matches(cell))
                    continue;
                if (count >= maxCells)
                    return new FloodFillResult(count, true);
                output.Add(cell);
                count++;
            }

            return new FloodFillResult(count, false);
        }

        foreach (var placed in layer.Cells)
        {
            if (!predicate.Matches(placed.Cell))
                continue;
            if (count >= maxCells)
                return new FloodFillResult(count, true);
            output.Add(placed.Cell);
            count++;
        }

        return new FloodFillResult(count, false);
    }

    private readonly struct Matcher : ICellPredicate
    {
        private readonly TileLayer _layer;
        private readonly TileCell _target;
        private readonly FillMatch _match;
        private readonly Terrain? _terrain;
        private readonly CellSet? _within;

        public Matcher(TileMap map, TileLayer layer, TileCell target, FillMatch match, CellSet? within)
        {
            _layer = layer;
            _target = target;
            _within = within;
            _terrain = match == FillMatch.SameTerrain ? map.FindTerrain(target) : null;
            _match = match == FillMatch.SameTerrain && _terrain is null ? FillMatch.SameTile : match;
        }

        public bool Matches(GridCoord cell)
        {
            if (_within is not null && !_within.Contains(cell))
                return false;
            var tile = _layer.GetCell(cell);
            return _match switch
            {
                FillMatch.SameTileAnyOrientation => tile.WithoutTransform == _target.WithoutTransform,
                FillMatch.SameTerrain => _terrain!.Contains(tile),
                _ => tile == _target
            };
        }
    }
}
