using System.Numerics;
using Talesmith.Grids;

namespace Talesmith.VFX.Simulation;

/// <summary>Picks start positions and directions inside a <see cref="ShapeModule"/>, caching the geometry of tiles and polygons.</summary>
internal sealed class ShapeSampler
{
    private static readonly Vector2 Up = new(0, -1);

    private readonly List<Vector2> _outline = [];
    private readonly List<float> _triangleAreas = [];
    private readonly List<int> _triangles = [];
    private readonly List<float> _edgeLengths = [];
    private Vector2 _centroid;
    private float _outlineExtent;
    private int _geometryKey;
    private bool _prepared;
    private bool _hasGeometry;

    /// <summary>Samples a start position and a unit direction in the emitter's space, before the emitter's transform.</summary>
    public void Sample(ShapeModule shape, ref ParticleRandom random, out Vector2 position, out Vector2 direction)
    {
        if (!shape.Enabled)
        {
            position = Vector2.Zero;
            direction = Direction(shape.Angle);
            ApplyDirectionMode(shape, ref random, ref direction);
            return;
        }

        var natural = SampleKind(shape, ref random, out position);
        if (shape.Rotation != 0)
        {
            var rotation = Matrix3x2.CreateRotation(shape.Rotation);
            position = Vector2.Transform(position, rotation);
            natural = Vector2.TransformNormal(natural, rotation);
        }

        position += shape.Offset;
        direction = shape.Direction == ParticleDirectionMode.Shape ? natural : Direction(shape.Angle);
        ApplyDirectionMode(shape, ref random, ref direction);
        if (shape.RandomizePosition > 0)
            position += random.OnUnitCircle() * (shape.RandomizePosition * MathF.Sqrt(random.NextFloat()));
    }

    /// <summary>The distance from the emitter to the furthest point the shape can emit from.</summary>
    public float Extent(ShapeModule shape)
    {
        if (!shape.Enabled)
            return 0;
        var kind = shape.Kind switch
        {
            ParticleShapeKind.Point => 0,
            ParticleShapeKind.Line => MathF.Abs(shape.Size.X) * 0.5f,
            ParticleShapeKind.Rectangle => (shape.Size * 0.5f).Length(),
            ParticleShapeKind.Circle or ParticleShapeKind.Cone => MathF.Abs(shape.Radius),
            _ => OutlineExtent(shape)
        };
        return shape.Offset.Length() + kind + shape.RandomizePosition;
    }

    private Vector2 SampleKind(ShapeModule shape, ref ParticleRandom random, out Vector2 position)
    {
        var edge = shape.EmitFrom == ParticleEmitFrom.Edge;
        switch (shape.Kind)
        {
            case ParticleShapeKind.Line:
                position = new Vector2((random.NextFloat() - 0.5f) * shape.Size.X, 0);
                return Up;
            case ParticleShapeKind.Rectangle:
                return SampleRectangle(shape.Size, edge, ref random, out position);
            case ParticleShapeKind.Circle:
                return SampleCircle(shape, edge, ref random, out position);
            case ParticleShapeKind.Cone:
                return SampleCone(shape, ref random, out position);
            case ParticleShapeKind.Tile:
            case ParticleShapeKind.Polygon:
                if (!_hasGeometry)
                    goto default;
                return edge ? SampleOutlineEdge(ref random, out position) : SampleOutlineArea(ref random, out position);
            default:
                position = Vector2.Zero;
                return Direction(shape.Angle);
        }
    }

    private static Vector2 SampleRectangle(Vector2 size, bool edge, ref ParticleRandom random, out Vector2 position)
    {
        var half = size * 0.5f;
        if (!edge)
        {
            position = new Vector2((random.NextFloat() * 2 - 1) * half.X, (random.NextFloat() * 2 - 1) * half.Y);
            return Outward(position);
        }

        var width = MathF.Abs(size.X);
        var height = MathF.Abs(size.Y);
        var along = random.NextFloat() * 2 * (width + height);
        if (along < width)
        {
            position = new Vector2(along - half.X, -half.Y);
            return Up;
        }

        along -= width;
        if (along < height)
        {
            position = new Vector2(half.X, along - half.Y);
            return Vector2.UnitX;
        }

        along -= height;
        if (along < width)
        {
            position = new Vector2(half.X - along, half.Y);
            return Vector2.UnitY;
        }

        position = new Vector2(-half.X, half.Y - (along - width));
        return -Vector2.UnitX;
    }

    private static Vector2 SampleCircle(ShapeModule shape, bool edge, ref ParticleRandom random, out Vector2 position)
    {
        var angle = random.NextFloat() * Math.Clamp(shape.Arc, 0, MathF.Tau);
        var (sin, cos) = MathF.SinCos(angle);
        var outward = new Vector2(cos, sin);
        var outer = MathF.Abs(shape.Radius);
        float radius;
        if (edge)
        {
            radius = outer;
        }
        else
        {
            var inner = Math.Clamp(shape.InnerRadius, 0, outer);
            radius = MathF.Sqrt(inner * inner + (outer * outer - inner * inner) * random.NextFloat());
        }

        position = outward * radius;
        return outward;
    }

    private static Vector2 SampleCone(ShapeModule shape, ref ParticleRandom random, out Vector2 position)
    {
        var axis = Direction(shape.Angle);
        var across = new Vector2(-axis.Y, axis.X);
        var u = random.NextFloat() * 2 - 1;
        position = across * (u * shape.Radius);
        var fan = shape.Radius > 0 ? u : random.NextFloat() * 2 - 1;
        var turn = fan * shape.ConeAngle * 0.5f;
        var (sin, cos) = MathF.SinCos(turn);
        return new Vector2(axis.X * cos - axis.Y * sin, axis.X * sin + axis.Y * cos);
    }

    private Vector2 SampleOutlineArea(ref ParticleRandom random, out Vector2 position)
    {
        var triangle = Pick(_triangleAreas, random.NextFloat());
        var a = _outline[_triangles[triangle * 3]];
        var b = _outline[_triangles[triangle * 3 + 1]];
        var c = _outline[_triangles[triangle * 3 + 2]];
        var u = random.NextFloat();
        var v = random.NextFloat();
        if (u + v > 1)
        {
            u = 1 - u;
            v = 1 - v;
        }

        position = a + (b - a) * u + (c - a) * v;
        return Outward(position - _centroid);
    }

    private Vector2 SampleOutlineEdge(ref ParticleRandom random, out Vector2 position)
    {
        var edge = Pick(_edgeLengths, random.NextFloat());
        var a = _outline[edge];
        var b = _outline[(edge + 1) % _outline.Count];
        position = Vector2.Lerp(a, b, random.NextFloat());
        var along = b - a;
        var normal = Vector2.Normalize(new Vector2(along.Y, -along.X));
        return float.IsFinite(normal.X) ? normal : Outward(position - _centroid);
    }

    /// <summary>Picks an index from cumulative weights, given a uniform number from 0 to 1.</summary>
    private static int Pick(List<float> cumulative, float uniform)
    {
        var target = uniform * cumulative[^1];
        int lo = 0, hi = cumulative.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (cumulative[mid] < target)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private float OutlineExtent(ShapeModule shape)
    {
        Prepare(shape);
        return _hasGeometry ? _outlineExtent : 0;
    }

    /// <summary>Rebuilds tile and polygon geometry when the shape changed; call once per step before sampling.</summary>
    public void Prepare(ShapeModule shape)
    {
        if (shape.Kind is not (ParticleShapeKind.Tile or ParticleShapeKind.Polygon))
            return;
        var key = GeometryKey(shape);
        if (_prepared && key == _geometryKey)
            return;
        _prepared = true;
        _geometryKey = key;
        _outline.Clear();
        if (shape.Kind == ParticleShapeKind.Tile)
        {
            if (shape.CellSize.X <= 0 || shape.CellSize.Y <= 0)
            {
                _hasGeometry = false;
                return;
            }

            IGridLayout layout = shape.Tile == GridKind.Square
                ? new SquareLayout(shape.CellSize.X, shape.CellSize.Y)
                : new HexLayout(shape.Tile == GridKind.HexPointyTop, shape.CellSize.X, shape.CellSize.Y);
            for (var i = 0; i < layout.CornerCount; i++)
                _outline.Add(layout.CornerOffset(i));
        }
        else
        {
            _outline.AddRange(shape.Points);
        }

        _hasGeometry = BuildGeometry();
    }

    private bool BuildGeometry()
    {
        _triangles.Clear();
        _triangleAreas.Clear();
        _edgeLengths.Clear();
        if (_outline.Count < 3)
            return false;

        var signedArea = 0f;
        for (var i = 0; i < _outline.Count; i++)
        {
            var a = _outline[i];
            var b = _outline[(i + 1) % _outline.Count];
            signedArea += a.X * b.Y - b.X * a.Y;
        }

        if (MathF.Abs(signedArea) < 1e-6f)
            return false;
        if (signedArea < 0)
            _outline.Reverse();

        Triangulate();
        if (_triangles.Count == 0)
            return false;

        var total = 0f;
        var centroid = Vector2.Zero;
        for (var t = 0; t < _triangles.Count; t += 3)
        {
            var a = _outline[_triangles[t]];
            var b = _outline[_triangles[t + 1]];
            var c = _outline[_triangles[t + 2]];
            var area = MathF.Abs(Cross(b - a, c - a)) * 0.5f;
            total += area;
            centroid += (a + b + c) / 3 * area;
            _triangleAreas.Add(total);
        }

        _centroid = total > 0 ? centroid / total : Vector2.Zero;
        var perimeter = 0f;
        _outlineExtent = 0;
        for (var i = 0; i < _outline.Count; i++)
        {
            perimeter += Vector2.Distance(_outline[i], _outline[(i + 1) % _outline.Count]);
            _edgeLengths.Add(perimeter);
            _outlineExtent = MathF.Max(_outlineExtent, _outline[i].Length());
        }

        return total > 0 && perimeter > 0;
    }

    /// <summary>Ear clipping for a simple counter-clockwise polygon (clockwise on screen, where Y points down).</summary>
    private void Triangulate()
    {
        var remaining = new List<int>(_outline.Count);
        for (var i = 0; i < _outline.Count; i++)
            remaining.Add(i);

        var guard = remaining.Count * remaining.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                var current = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];
                if (!IsEar(remaining, previous, current, next))
                    continue;
                _triangles.Add(previous);
                _triangles.Add(current);
                _triangles.Add(next);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
                break;
        }

        if (remaining.Count == 3)
        {
            _triangles.Add(remaining[0]);
            _triangles.Add(remaining[1]);
            _triangles.Add(remaining[2]);
        }
    }

    private bool IsEar(List<int> remaining, int previous, int current, int next)
    {
        var a = _outline[previous];
        var b = _outline[current];
        var c = _outline[next];
        if (Cross(b - a, c - b) <= 0)
            return false;
        foreach (var index in remaining)
        {
            if (index == previous || index == current || index == next)
                continue;
            if (InTriangle(_outline[index], a, b, c))
                return false;
        }

        return true;
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
        Cross(b - a, p - a) >= 0 && Cross(c - b, p - b) >= 0 && Cross(a - c, p - c) >= 0;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static int GeometryKey(ShapeModule shape)
    {
        var hash = new HashCode();
        hash.Add(shape.Kind);
        if (shape.Kind == ParticleShapeKind.Tile)
        {
            hash.Add(shape.Tile);
            hash.Add(shape.CellSize);
        }
        else
        {
            foreach (var point in shape.Points)
                hash.Add(point);
        }

        return hash.ToHashCode();
    }

    private static void ApplyDirectionMode(ShapeModule shape, ref ParticleRandom random, ref Vector2 direction)
    {
        if (shape.Direction == ParticleDirectionMode.Random)
            direction = random.OnUnitCircle();
        if (shape.Spread <= 0)
            return;
        var (sin, cos) = MathF.SinCos((random.NextFloat() - 0.5f) * shape.Spread);
        direction = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
    }

    private static Vector2 Outward(Vector2 offset)
    {
        var length = offset.Length();
        return length > 1e-5f ? offset / length : Up;
    }

    private static Vector2 Direction(float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);
        return new Vector2(cos, sin);
    }
}
