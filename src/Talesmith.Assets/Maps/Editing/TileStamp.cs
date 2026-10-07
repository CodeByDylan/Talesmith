using Talesmith.Grids;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>A pattern of tiles relative to an origin cell, for copy and paste, stamps and moving selections.</summary>
/// <remarks>
/// Rotating and flipping move the cells around the origin and turn each tile with them, in 60° steps on hex grids and 90° steps on
/// square grids, so a rotated stamp looks like the rotated picture of the original. Immutable.
/// </remarks>
public sealed class TileStamp
{
    private readonly PlacedTile[] _cells;

    /// <param name="cells">Tiles keyed by their offset from the origin; empty tiles are kept and erase when pasted.</param>
    public TileStamp(IEnumerable<PlacedTile> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        _cells = [.. cells];
    }

    private TileStamp(PlacedTile[] cells) => _cells = cells;

    /// <summary>The tiles by offset from the origin.</summary>
    public IReadOnlyList<PlacedTile> Cells => _cells;

    public int Count => _cells.Length;

    public bool IsEmpty => _cells.Length == 0;

    /// <summary>Copies the non-empty tiles of a layer within <paramref name="cells"/>, with the origin at the center of their bounds, as Hexy does.</summary>
    public static TileStamp Capture(TileLayer layer, CellSet cells, GridTopology topology)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(topology);
        var tiles = new List<PlacedTile>();
        var bounds = GridBounds.Empty;
        foreach (var cell in cells)
        {
            var tile = layer.GetCell(cell);
            if (tile.IsEmpty)
                continue;
            tiles.Add(new PlacedTile(cell, tile));
            bounds = bounds.Include(cell);
        }

        if (tiles.Count == 0)
            return new TileStamp([]);

        var origin = topology.Round((bounds.MinX + bounds.MaxX) / 2.0, (bounds.MinY + bounds.MaxY) / 2.0);
        var result = new PlacedTile[tiles.Count];
        for (var i = 0; i < result.Length; i++)
            result[i] = tiles[i] with { Cell = tiles[i].Cell - origin };
        return new TileStamp(result);
    }

    /// <summary>Gets the stamp rotated clockwise around its origin by whole grid steps.</summary>
    public TileStamp Rotate(int clockwiseSteps, GridTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return Transform(cell => topology.Rotate(cell, clockwiseSteps), tile => tile.Rotate(clockwiseSteps, topology.RotationSteps));
    }

    /// <summary>Gets the stamp mirrored left to right.</summary>
    public TileStamp FlipHorizontal(GridTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return Transform(topology.MirrorHorizontal, tile => tile.FlipHorizontal(topology.RotationSteps));
    }

    /// <summary>Gets the stamp mirrored top to bottom.</summary>
    public TileStamp FlipVertical(GridTopology topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        return Transform(topology.MirrorVertical, tile => tile.FlipVertical(topology.RotationSteps));
    }

    /// <summary>Writes the stamp with its origin on <paramref name="target"/>.</summary>
    public void Paste(TileEdit edit, TileLayer layer, GridCoord target)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var placed = new PlacedTile[_cells.Length];
        for (var i = 0; i < placed.Length; i++)
            placed[i] = _cells[i] with { Cell = _cells[i].Cell + target };
        edit.SetMany(layer, placed);
    }

    /// <summary>Adds the cells the stamp covers with its origin on <paramref name="target"/>, for previews.</summary>
    public void Footprint(GridCoord target, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        foreach (var cell in _cells)
            output.Add(cell.Cell + target);
    }

    private TileStamp Transform(Func<GridCoord, GridCoord> move, Func<TileCell, TileCell> turn)
    {
        var result = new PlacedTile[_cells.Length];
        for (var i = 0; i < result.Length; i++)
            result[i] = new PlacedTile(move(_cells[i].Cell), turn(_cells[i].Tile));
        return new TileStamp(result);
    }
}
