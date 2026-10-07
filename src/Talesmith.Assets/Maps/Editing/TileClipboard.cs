using Talesmith.Grids;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>A tile under the pointer and the layer it was found on.</summary>
public readonly record struct PickedTile(TileLayer Layer, TileCell Tile);

/// <summary>Picks tiles from the map into the brush, as an eyedropper does.</summary>
public static class TilePicker
{
    /// <summary>Finds the tile at a cell on <paramref name="preferred"/>, or else on the topmost visible tile layer; null when every layer is empty there.</summary>
    public static PickedTile? Pick(TileMap map, GridCoord cell, TileLayer? preferred = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (preferred is not null && preferred.GetCell(cell) is { IsEmpty: false } own)
            return new PickedTile(preferred, own);

        var layers = map.TileLayers;
        for (var i = layers.Count - 1; i >= 0; i--)
        {
            if (layers[i].IsVisible && layers[i].GetCell(cell) is { IsEmpty: false } tile)
                return new PickedTile(layers[i], tile);
        }

        return null;
    }
}

/// <summary>Copies, cuts, pastes, deletes and moves the tiles of a selection on one layer, through a <see cref="TileEdit"/> so each step undoes at once.</summary>
public static class TileClipboard
{
    /// <summary>Copies the non-empty tiles of the selection into a stamp centered on the selection.</summary>
    public static TileStamp Copy(TileLayer layer, CellSet selection, GridTopology topology) => TileStamp.Capture(layer, selection, topology);

    /// <summary>Copies the selection, then clears it.</summary>
    public static TileStamp Cut(TileEdit edit, TileLayer layer, CellSet selection)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var stamp = Copy(layer, selection, edit.Map.Layout.Topology);
        Delete(edit, layer, selection);
        return stamp;
    }

    /// <summary>Writes a stamp with its origin on <paramref name="target"/>; its empty cells leave the map alone.</summary>
    public static void Paste(TileEdit edit, TileLayer layer, TileStamp stamp, GridCoord target)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        stamp.Paste(edit, layer, target);
    }

    /// <summary>Clears every selected cell.</summary>
    public static void Delete(TileEdit edit, TileLayer layer, CellSet selection)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit.Fill(layer, selection, TileCell.Empty);
    }

    /// <summary>Moves the selected tiles by <paramref name="delta"/> and returns the moved selection.</summary>
    public static CellSet Move(TileEdit edit, TileLayer layer, CellSet selection, GridCoord delta)
    {
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(selection);
        var moved = selection.Translate(delta);
        if (delta == GridCoord.Zero)
            return moved;

        var tiles = new List<PlacedTile>(selection.Count);
        foreach (var cell in selection)
        {
            var tile = layer.GetCell(cell);
            if (!tile.IsEmpty)
                tiles.Add(new PlacedTile(cell + delta, tile));
        }

        edit.Fill(layer, selection, TileCell.Empty);
        edit.SetMany(layer, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tiles));
        return moved;
    }
}
