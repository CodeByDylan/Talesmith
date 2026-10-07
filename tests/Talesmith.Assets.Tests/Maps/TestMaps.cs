using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Tests.Maps;

internal static class TestMaps
{
    public static TheoryData<GridKind> Kinds => [GridKind.HexPointyTop, GridKind.HexFlatTop, GridKind.Square];

    /// <summary>A map with a 16-color tileset (id 1) and one "Ground" tile layer, using small chunks so edits cross chunk borders.</summary>
    public static TileMap Create(GridKind kind = GridKind.HexPointyTop)
    {
        var (width, height) = kind switch
        {
            GridKind.HexPointyTop => (128f, 148f),
            GridKind.HexFlatTop => (148f, 128f),
            _ => (64f, 64f)
        };
        var map = TileMap.Create(kind, width, height, chunkShift: 3);
        var colors = Enumerable.Range(0, 16).Select(i => ($"Color {i}", new Color((byte)(i * 16), 128, 200))).ToArray();
        map.AddTileset(Tileset.FromColors("Colors", colors));
        return map;
    }

    public static TileLayer Ground(this TileMap map) => map.TileLayers[0];

    public static TileCell Tile(int id, int rotation = 0, bool flip = false) => new(1, id, rotation, flip);

    /// <summary>Records every change a map raises.</summary>
    public static List<MapChange> Record(TileMap map)
    {
        var changes = new List<MapChange>();
        map.Changed += (_, change) => changes.Add(change);
        return changes;
    }

    public static PlacedTile[] Cells(TileLayer layer)
    {
        var cells = new List<PlacedTile>();
        foreach (var placed in layer.Cells)
            cells.Add(placed);
        return [.. cells.OrderBy(c => c.Cell.Y).ThenBy(c => c.Cell.X)];
    }

    public static GridCoord[] Sorted(this IEnumerable<GridCoord> cells) => [.. cells.OrderBy(c => c.Y).ThenBy(c => c.X)];
}
