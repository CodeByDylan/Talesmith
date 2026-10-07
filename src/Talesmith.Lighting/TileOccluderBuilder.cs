using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Lighting;

/// <summary>Builds the shadow outline of one chunk of a collision layer: the merged shapes of its solid cells.</summary>
/// <remarks>
/// A tile with <see cref="TileInfo.Collision"/> shapes contributes those, flipped and turned like its artwork. Other tiles contribute the
/// opaque pixels of their artwork inside their cell, or the whole cell when the artwork covers it or the tile is a color. Edges shared by
/// two solid cells cancel out and collinear edges are merged, so a wall of any length becomes a few long edges. Each chunk's outline is
/// closed: where solid cells continue into the next chunk, both chunks' outlines run along the border, and shadows treat shapes that
/// touch as one. Scratch buffers are reused.
/// </remarks>
public sealed class TileOccluderBuilder
{
    private const float KeyScale = 16;

    private readonly TileArtShapes _art = new();
    private readonly Dictionary<EdgeKey, ShadowEdge> _edges = [];
    private readonly List<ShadowEdge> _remaining = [];
    private readonly Dictionary<PointKey, int> _starts = [];
    private readonly Dictionary<PointKey, int> _ends = [];
    private readonly List<bool> _consumed = [];
    private readonly List<ShadowEdge> _merged = [];
    private readonly List<Vector2> _polygon = [];
    private Vector2[] _corners = [];

    /// <summary>Builds the outline of a chunk of <paramref name="layer"/> for a map whose cell (0, 0) is centered on <paramref name="origin"/>.</summary>
    /// <returns>The edges, clockwise around solid areas, and their bounds.</returns>
    public (ShadowEdge[] Edges, Rect2 Bounds) Build(TileMap map, TileLayer layer, TileChunk chunk, Vector2 origin)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(chunk);
        _edges.Clear();
        PrepareCorners(map.Layout);

        var cells = chunk.EnsureDecoded();
        var size = chunk.Size;
        var first = chunk.Coord.Origin(layer.ChunkShift);
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell.IsEmpty)
                continue;
            var coord = new GridCoord(first.X + i % size, first.Y + i / size);
            AddCell(map, cell, origin + map.Layout.CellToWorld(coord));
        }

        return (Merge(), Bounds());
    }

    /// <summary>Adds the shape a solid cell blocks light with.</summary>
    private void AddCell(TileMap map, TileCell cell, Vector2 center)
    {
        if (CollisionShapes(map, cell) is { Count: > 0 } shapes)
            AddShapes(map, cell, center, shapes);
        else if (_art.For(map, cell, _corners) is { } loops)
            AddLoops(loops, center);
        else
            AddOutline(center);
    }

    private static IReadOnlyList<IReadOnlyList<Vector2>>? CollisionShapes(TileMap map, TileCell cell) =>
        map.FindTileset(cell.TilesetId)?.Find(cell.TileId)?.Collision;

    private void AddShapes(TileMap map, TileCell cell, Vector2 center, IReadOnlyList<IReadOnlyList<Vector2>> shapes)
    {
        var rotation = cell.Rotation == 0 ? Matrix3x2.Identity : Matrix3x2.CreateRotation(MathHelper.ToRadians(cell.Rotation * map.RotationStepDegrees));
        foreach (var shape in shapes)
        {
            if (shape.Count < 3)
                continue;
            _polygon.Clear();
            foreach (var point in shape)
                _polygon.Add(Vector2.Transform(cell.FlipX ? new Vector2(-point.X, point.Y) : point, rotation) + center);

            var clockwise = SignedArea(_polygon) >= 0;
            for (var i = 0; i < _polygon.Count; i++)
            {
                var a = _polygon[i];
                var b = _polygon[(i + 1) % _polygon.Count];
                AddEdge(clockwise ? a : b, clockwise ? b : a);
            }
        }
    }

    private void AddLoops(IReadOnlyList<Vector2[]> loops, Vector2 center)
    {
        foreach (var loop in loops)
        {
            for (var i = 0; i < loop.Length; i++)
                AddEdge(center + loop[i], center + loop[(i + 1) % loop.Length]);
        }
    }

    private void AddOutline(Vector2 center)
    {
        for (var i = 0; i < _corners.Length; i++)
            AddEdge(center + _corners[i], center + _corners[(i + 1) % _corners.Length]);
    }

    /// <summary>Adds an edge, or removes the opposite edge when a neighboring solid cell already added it.</summary>
    private void AddEdge(Vector2 start, Vector2 end)
    {
        var a = PointKey.Of(start);
        var b = PointKey.Of(end);
        if (a == b || _edges.Remove(new EdgeKey(b, a)))
            return;
        _edges[new EdgeKey(a, b)] = new ShadowEdge(start, end);
    }

    /// <summary>Joins chains of collinear edges into single edges, starting from the first edge of each chain.</summary>
    private ShadowEdge[] Merge()
    {
        _remaining.Clear();
        _remaining.AddRange(_edges.Values);
        _starts.Clear();
        _ends.Clear();
        _consumed.Clear();
        for (var i = 0; i < _remaining.Count; i++)
        {
            Count(_starts, PointKey.Of(_remaining[i].Start), i);
            Count(_ends, PointKey.Of(_remaining[i].End), i);
            _consumed.Add(false);
        }

        _merged.Clear();
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < _remaining.Count; i++)
            {
                if (_consumed[i] || (pass == 0 && Predecessor(i) >= 0))
                    continue;
                _consumed[i] = true;
                var edge = _remaining[i];
                while (Successor(edge) is var next && next >= 0 && !_consumed[next])
                {
                    _consumed[next] = true;
                    edge = edge with { End = _remaining[next].End };
                }

                _merged.Add(edge);
            }
        }

        return [.. _merged];
    }

    /// <summary>The collinear edge that continues <paramref name="edge"/> when the chain does not branch there, or -1.</summary>
    private int Successor(ShadowEdge edge)
    {
        var key = PointKey.Of(edge.End);
        if (!_starts.TryGetValue(key, out var next) || next < 0 || !_ends.TryGetValue(key, out var previous) || previous < 0)
            return -1;
        return Collinear(edge, _remaining[next]) ? next : -1;
    }

    private int Predecessor(int index)
    {
        var edge = _remaining[index];
        var key = PointKey.Of(edge.Start);
        if (!_ends.TryGetValue(key, out var previous) || previous < 0 || !_starts.TryGetValue(key, out var next) || next < 0)
            return -1;
        return Collinear(_remaining[previous], edge) ? previous : -1;
    }

    /// <summary>Records the edge at a point, or -1 when several edges meet there and chains must not be joined.</summary>
    private static void Count(Dictionary<PointKey, int> map, PointKey key, int index) =>
        map[key] = map.ContainsKey(key) ? -1 : index;

    private static bool Collinear(ShadowEdge a, ShadowEdge b)
    {
        var first = Vector2.Normalize(a.End - a.Start);
        var second = Vector2.Normalize(b.End - b.Start);
        return MathF.Abs(first.X * second.Y - first.Y * second.X) < 1e-4f && Vector2.Dot(first, second) > 0;
    }

    private Rect2 Bounds()
    {
        if (_merged.Count == 0)
            return Rect2.Empty;
        var min = _merged[0].Start;
        var max = min;
        foreach (var edge in _merged)
        {
            min = Vector2.Min(min, Vector2.Min(edge.Start, edge.End));
            max = Vector2.Max(max, Vector2.Max(edge.Start, edge.End));
        }

        return Rect2.FromEdges(min.X, min.Y, max.X, max.Y);
    }

    private void PrepareCorners(IGridLayout layout)
    {
        if (_corners.Length != layout.CornerCount)
            _corners = new Vector2[layout.CornerCount];
        for (var i = 0; i < _corners.Length; i++)
            _corners[i] = layout.CornerOffset(i);
        if (SignedArea(_corners) < 0)
            Array.Reverse(_corners);
    }

    /// <summary>Twice the signed area; positive when the points run clockwise on screen.</summary>
    private static float SignedArea(IReadOnlyList<Vector2> points)
    {
        var area = 0f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area;
    }

    /// <summary>A point rounded to 1/16 world unit, so corners computed from neighboring cells match.</summary>
    private readonly record struct PointKey(int X, int Y)
    {
        public static PointKey Of(Vector2 point) => new((int)MathF.Round(point.X * KeyScale), (int)MathF.Round(point.Y * KeyScale));
    }

    private readonly record struct EdgeKey(PointKey Start, PointKey End);
}
