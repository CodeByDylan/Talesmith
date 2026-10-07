using System.Numerics;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Tiles;

/// <summary>A convex collision shape of a tile map chunk, in map space.</summary>
internal readonly record struct TileShape(Shape Shape, bool OneWay);

/// <summary>The collision of one tile: convex pieces relative to the cell center, or none for a full cell.</summary>
internal sealed record TileCollision(Vector2[][]? Pieces, bool OneWay);

/// <summary>Builds the convex shapes of one chunk of a map's collision layers.</summary>
/// <remarks>
/// A non-empty cell is solid. Its tile's <see cref="TileInfo.Collision"/> polygons, when it has any, replace the full cell; concave
/// polygons are split into convex pieces. On square grids, full cells are merged into as few rectangles as possible; on hex grids every
/// cell is a hexagon. Edges shared with neighboring full cells are flagged internal so bodies slide across seams, and shapes enclosed on
/// every side are left out because nothing can reach them. Cells of tiles or layers with the bool property <c>oneWay</c> become
/// platforms, merged along rows on square grids.
/// </remarks>
internal sealed class TileCollisionBuilder(ILogger logger)
{
    public const string OneWayProperty = "oneWay";

    private const byte Empty = 0;
    private const byte Full = 1;
    private const byte OneWayFull = 2;
    private const byte Custom = 3;

    private readonly Dictionary<long, TileCollision> _tiles = new();
    private byte[] _kinds = [];
    private bool[] _used = [];
    private readonly List<(GridCoord Cell, TileCell Tile, TileCollision Collision)> _custom = [];

    public void Build(TileMap map, IReadOnlyList<TileLayer> layers, ChunkCoord coord, List<TileShape> output)
    {
        output.Clear();
        _custom.Clear();
        var size = 1 << map.ChunkShift;
        var stride = size + 2;
        if (_kinds.Length < stride * stride)
        {
            _kinds = new byte[stride * stride];
            _used = new bool[size * size];
        }

        Array.Clear(_kinds, 0, stride * stride);
        var origin = coord.Origin(map.ChunkShift);
        var any = false;
        foreach (var layer in layers)
        {
            if (!layer.Chunks.TryGetValue(coord, out var chunk))
                continue;
            var cells = chunk.EnsureDecoded();
            var layerOneWay = layer.Properties.GetBool(OneWayProperty);
            for (var i = 0; i < cells.Length; i++)
            {
                if (cells[i].IsEmpty)
                    continue;
                any = true;
                var x = i % size;
                var y = i / size;
                ref var kind = ref _kinds[(y + 1) * stride + x + 1];
                var collision = Resolve(map, cells[i]);
                var oneWay = layerOneWay || collision.OneWay;
                if (collision.Pieces is null)
                {
                    kind = oneWay ? kind == Full ? Full : OneWayFull : Full;
                }
                else
                {
                    if (kind == Empty || kind == OneWayFull)
                        kind = Custom;
                    _custom.Add((new GridCoord(origin.X + x, origin.Y + y), cells[i], oneWay ? collision with { OneWay = true } : collision));
                }
            }
        }

        if (!any)
            return;
        FillBorder(map, layers, origin, size, stride);

        if (map.Layout.Kind == GridKind.Square)
            BuildRectangles(map.Layout, origin, size, stride, output);
        else
            BuildCells(map.Layout, origin, size, stride, output);

        foreach (var (cell, tile, collision) in _custom)
        {
            if (_kinds[(cell.Y - origin.Y + 1) * stride + cell.X - origin.X + 1] == Full)
                continue;
            AddCustom(map, cell, tile, collision, output);
        }
    }

    private void FillBorder(TileMap map, IReadOnlyList<TileLayer> layers, GridCoord origin, int size, int stride)
    {
        for (var y = -1; y <= size; y++)
        {
            for (var x = -1; x <= size; x++)
            {
                if (x >= 0 && x < size && y >= 0 && y < size)
                    continue;
                var cell = new GridCoord(origin.X + x, origin.Y + y);
                foreach (var layer in layers)
                {
                    var tile = layer.GetCell(cell);
                    if (tile.IsEmpty || layer.Properties.GetBool(OneWayProperty))
                        continue;
                    var collision = Resolve(map, tile);
                    if (collision.Pieces is null && !collision.OneWay)
                    {
                        _kinds[(y + 1) * stride + x + 1] = Full;
                        break;
                    }
                }
            }
        }
    }

    private bool IsFull(int x, int y, int stride) => _kinds[(y + 1) * stride + x + 1] == Full;

    private void BuildRectangles(IGridLayout layout, GridCoord origin, int size, int stride, List<TileShape> output)
    {
        Array.Clear(_used, 0, size * size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (_used[y * size + x])
                    continue;
                var kind = _kinds[(y + 1) * stride + x + 1];
                if (kind != Full && kind != OneWayFull)
                    continue;

                var width = 1;
                while (x + width < size && !_used[y * size + x + width] && _kinds[(y + 1) * stride + x + width + 1] == kind)
                    width++;
                var height = 1;
                if (kind == Full)
                {
                    while (y + height < size && RowMatches(x, y + height, width, size, stride, kind))
                        height++;
                }

                for (var dy = 0; dy < height; dy++)
                {
                    for (var dx = 0; dx < width; dx++)
                        _used[(y + dy) * size + x + dx] = true;
                }

                AddRectangle(layout, origin, x, y, width, height, stride, kind == OneWayFull, output);
            }
        }
    }

    private bool RowMatches(int x, int y, int width, int size, int stride, byte kind)
    {
        for (var dx = 0; dx < width; dx++)
        {
            if (_used[y * size + x + dx] || _kinds[(y + 1) * stride + x + dx + 1] != kind)
                return false;
        }

        return true;
    }

    private void AddRectangle(IGridLayout layout, GridCoord origin, int x, int y, int width, int height, int stride, bool oneWay, List<TileShape> output)
    {
        byte internalEdges = 0;
        if (!oneWay)
        {
            if (AllFull(x, y - 1, width, 1, stride))
                internalEdges |= 1;
            if (AllFull(x + width, y, 1, height, stride))
                internalEdges |= 2;
            if (AllFull(x, y + height, width, 1, stride))
                internalEdges |= 4;
            if (AllFull(x - 1, y, 1, height, stride))
                internalEdges |= 8;
            if (internalEdges == 15)
                return;
        }

        var half = layout.CellSize * 0.5f;
        var min = layout.CellToWorld(new GridCoord(origin.X + x, origin.Y + y)) - half;
        var max = layout.CellToWorld(new GridCoord(origin.X + x + width - 1, origin.Y + y + height - 1)) + half;
        Span<Vector2> corners = [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)];
        var shape = Shape.Polygon(corners);
        shape.InternalEdges = internalEdges;
        output.Add(new TileShape(shape, oneWay));
    }

    private bool AllFull(int x, int y, int width, int height, int stride)
    {
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                if (!IsFull(x + dx, y + dy, stride))
                    return false;
            }
        }

        return true;
    }

    private void BuildCells(IGridLayout layout, GridCoord origin, int size, int stride, List<TileShape> output)
    {
        Span<Vector2> corners = stackalloc Vector2[layout.CornerCount];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var kind = _kinds[(y + 1) * stride + x + 1];
                if (kind != Full && kind != OneWayFull)
                    continue;
                var cell = new GridCoord(origin.X + x, origin.Y + y);
                var center = layout.CellToWorld(cell);
                for (var i = 0; i < corners.Length; i++)
                    corners[i] = center + layout.CornerOffset(i);
                var shape = Shape.Polygon(corners);
                if (kind == Full)
                {
                    for (var edge = 0; edge < shape.Count; edge++)
                    {
                        var middle = (shape.Points[edge] + shape.Points[edge + 1 < shape.Count ? edge + 1 : 0]) * 0.5f;
                        var neighbor = layout.WorldToCell(2 * middle - center);
                        var nx = neighbor.X - origin.X;
                        var ny = neighbor.Y - origin.Y;
                        if (nx >= -1 && nx <= size && ny >= -1 && ny <= size && IsFull(nx, ny, stride))
                            shape.InternalEdges |= (byte)(1 << edge);
                    }

                    if (shape.InternalEdges == (1 << shape.Count) - 1)
                        continue;
                }

                output.Add(new TileShape(shape, kind == OneWayFull));
            }
        }
    }

    private void AddCustom(TileMap map, GridCoord cell, TileCell tile, TileCollision collision, List<TileShape> output)
    {
        var center = map.Layout.CellToWorld(cell);
        var rotation = Rot.FromAngle(MathHelper.ToRadians(tile.Rotation * map.RotationStepDegrees));
        Span<Vector2> points = stackalloc Vector2[Shape.MaxVertices];
        foreach (var piece in collision.Pieces!)
        {
            for (var i = 0; i < piece.Length; i++)
            {
                var p = piece[i];
                if (tile.FlipX)
                    p.X = -p.X;
                points[i] = center + rotation.Rotate(p);
            }

            try
            {
                output.Add(new TileShape(Shape.Polygon(points[..piece.Length]), collision.OneWay));
            }
            catch (ArgumentException)
            {
                // A sliver that degenerates after rotating is skipped.
            }
        }
    }

    private TileCollision Resolve(TileMap map, TileCell cell)
    {
        var key = (long)cell.TilesetId << 32 | (uint)cell.TileId;
        if (_tiles.TryGetValue(key, out var collision))
            return collision;

        var info = map.FindTileset(cell.TilesetId)?.Find(cell.TileId);
        var oneWay = info?.Properties.GetBool(OneWayProperty) ?? false;
        Vector2[][]? pieces = null;
        if (info is { Collision.Count: > 0 })
        {
            var list = new List<Vector2[]>();
            foreach (var polygon in info.Collision)
            {
                try
                {
                    var points = new Vector2[polygon.Count];
                    for (var i = 0; i < points.Length; i++)
                        points[i] = polygon[i];
                    list.AddRange(PolygonTools.Decompose(points));
                }
                catch (ArgumentException ex)
                {
                    logger.InvalidTileCollision(cell.TilesetId, cell.TileId, ex.Message);
                }
            }

            pieces = list.Count > 0 ? [.. list] : null;
        }

        collision = new TileCollision(pieces, oneWay);
        _tiles[key] = collision;
        return collision;
    }
}
