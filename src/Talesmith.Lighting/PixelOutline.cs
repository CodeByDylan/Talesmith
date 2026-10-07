using System.Numerics;

namespace Talesmith.Lighting;

/// <summary>Traces the outlines of the set pixels of a mask, for shadow shapes that follow artwork.</summary>
/// <remarks>
/// Loops are in pixel coordinates, with (0, 0) at the mask's top-left corner. They run clockwise on screen around set pixels and
/// counter-clockwise around holes, so the outside is to the left of each edge, as with <see cref="Rendering.Lighting.ShadowEdge"/>. Pixels
/// that only touch at a corner belong to separate loops. Straight runs of at least <see cref="StraightRun"/> pixels keep their exact
/// positions; the staircases between them are straightened to within the tolerance.
/// </remarks>
public static class PixelOutline
{
    /// <summary>The length from which a straight run of pixel edges is kept as it is.</summary>
    public const int StraightRun = 4;

    /// <summary>Traces the set pixels of <paramref name="mask"/>, <paramref name="width"/> × <paramref name="height"/> row by row.</summary>
    /// <param name="tolerance">How far, in pixels, a simplified outline may stray from the pixel edges.</param>
    public static IReadOnlyList<Vector2[]> Trace(ReadOnlySpan<bool> mask, int width, int height, float tolerance = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if (mask.Length != width * height)
            throw new ArgumentException($"Expected {width * height} pixels for a {width}×{height} mask, got {mask.Length}.", nameof(mask));

        var edges = Edges(mask, width, height);
        var loops = new List<Vector2[]>();
        var used = new bool[edges.Count];
        var corners = new List<Vector2>();
        for (var start = 0; start < edges.Count; start++)
        {
            if (used[start])
                continue;
            corners.Clear();
            for (var edge = start; !used[edge]; edge = edges.Next(edge))
            {
                used[edge] = true;
                if (edges.Direction(edges.Previous(edge)) != edges.Direction(edge))
                    corners.Add(edges.Start(edge));
            }

            if (Simplify(corners, tolerance) is { Length: >= 3 } loop)
                loops.Add(loop);
        }

        return loops;
    }

    /// <summary>The unit edges between set and unset pixels, linked into loops.</summary>
    private static PixelEdges Edges(ReadOnlySpan<bool> mask, int width, int height)
    {
        var edges = new PixelEdges(width);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!mask[y * width + x])
                    continue;
                if (y == 0 || !mask[(y - 1) * width + x])
                    edges.Add(x, y, PixelEdges.Right);
                if (x == width - 1 || !mask[y * width + x + 1])
                    edges.Add(x + 1, y, PixelEdges.Down);
                if (y == height - 1 || !mask[(y + 1) * width + x])
                    edges.Add(x + 1, y + 1, PixelEdges.Left);
                if (x == 0 || !mask[y * width + x - 1])
                    edges.Add(x, y + 1, PixelEdges.Up);
            }
        }

        edges.Link();
        return edges;
    }

    /// <summary>Straightens a closed outline with Douglas–Peucker between its long straight runs, or between its two most distant corners
    /// when it has none; null when nothing is left.</summary>
    private static Vector2[]? Simplify(List<Vector2> corners, float tolerance)
    {
        if (corners.Count < 3)
            return null;
        if (corners.Count <= 4 || tolerance <= 0)
            return [.. corners];

        var count = corners.Count;
        var keep = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var next = (i + 1) % count;
            var run = corners[next] - corners[i];
            if (MathF.Abs(run.X) + MathF.Abs(run.Y) >= StraightRun)
                keep[i] = keep[next] = true;
        }

        var first = Array.IndexOf(keep, true);
        if (first < 0)
        {
            first = 0;
            var far = 0;
            for (var i = 1; i < count; i++)
            {
                if (Vector2.DistanceSquared(corners[i], corners[0]) > Vector2.DistanceSquared(corners[far], corners[0]))
                    far = i;
            }

            keep[0] = keep[far] = true;
        }

        // Straighten each stretch between kept corners, going once around the loop.
        for (var start = first; start < first + count;)
        {
            var end = start + 1;
            while (!keep[end % count])
                end++;
            Mark(corners, start, end, tolerance, keep);
            start = end;
        }

        var loop = new List<Vector2>();
        for (var i = 0; i < corners.Count; i++)
        {
            if (keep[i])
                loop.Add(corners[i]);
        }

        return loop.Count >= 3 ? [.. loop] : null;
    }

    /// <summary>Keeps the corners between <paramref name="first"/> and <paramref name="last"/>, counted on around the loop past its end,
    /// that the outline needs.</summary>
    private static void Mark(List<Vector2> corners, int first, int last, float tolerance, bool[] keep)
    {
        var count = corners.Count;
        var pending = new Stack<(int First, int Last)>();
        pending.Push((first, last));
        while (pending.TryPop(out var range))
        {
            var a = corners[range.First % count];
            var b = corners[range.Last % count];
            var farthest = -1;
            var distance = tolerance;
            for (var i = range.First + 1; i < range.Last; i++)
            {
                var d = DistanceToSegment(corners[i % count], a, b);
                if (d > distance)
                {
                    distance = d;
                    farthest = i;
                }
            }

            if (farthest < 0)
                continue;
            keep[farthest % count] = true;
            pending.Push((range.First, farthest));
            pending.Push((farthest, range.Last));
        }
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var segment = b - a;
        var lengthSquared = segment.LengthSquared();
        var t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(point - a, segment) / lengthSquared, 0, 1) : 0;
        return Vector2.Distance(point, a + segment * t);
    }

    /// <summary>Unit edges on the pixel grid, each starting at a grid point and running right, down, left or up.</summary>
    private sealed class PixelEdges(int width)
    {
        public const int Right = 0;
        public const int Down = 1;
        public const int Left = 2;
        public const int Up = 3;

        private static readonly (int X, int Y)[] Steps = [(1, 0), (0, 1), (-1, 0), (0, -1)];

        private readonly List<(int X, int Y, int Direction)> _edges = [];
        private readonly Dictionary<int, (int First, int Second)> _outgoing = [];
        private int[] _next = [];
        private int[] _previous = [];

        public int Count => _edges.Count;

        public void Add(int x, int y, int direction)
        {
            var key = Key(x, y);
            var index = _edges.Count;
            _edges.Add((x, y, direction));
            _outgoing[key] = _outgoing.TryGetValue(key, out var existing) ? (existing.First, index) : (index, -1);
        }

        /// <summary>Joins each edge to the one leaving its end; where two leave, the one turning right, so pixels touching at a corner stay apart.</summary>
        public void Link()
        {
            _next = new int[_edges.Count];
            _previous = new int[_edges.Count];
            for (var i = 0; i < _edges.Count; i++)
            {
                var (x, y, direction) = _edges[i];
                var (dx, dy) = Steps[direction];
                var (first, second) = _outgoing[Key(x + dx, y + dy)];
                var next = second < 0 || _edges[first].Direction == (direction + 1) % 4 ? first : second;
                _next[i] = next;
                _previous[next] = i;
            }
        }

        public int Next(int edge) => _next[edge];

        public int Previous(int edge) => _previous[edge];

        public int Direction(int edge) => _edges[edge].Direction;

        public Vector2 Start(int edge) => new(_edges[edge].X, _edges[edge].Y);

        private int Key(int x, int y) => y * (width + 1) + x;
    }
}
