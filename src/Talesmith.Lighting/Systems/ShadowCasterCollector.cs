using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;
using Talesmith.Runtime.Components;

namespace Talesmith.Lighting.Systems;

/// <summary>Turns enabled <see cref="ShadowCaster2D"/>s within reach of shadowed lights into world-space outlines, nearest to the camera first.</summary>
internal sealed class ShadowCasterCollector
{
    private const int CircleSegments = 20;

    private static readonly Vector2[] UnitCircle = CreateUnitCircle();

    private Vector2[] _points = new Vector2[256];
    private Candidate[] _candidates = new Candidate[32];
    private float[] _keys = new float[32];
    private int _pointCount;
    private int _count;

    /// <summary>Adds the outlines of casters overlapping <paramref name="region"/> to the frame and returns how many were added.</summary>
    public int Collect(World world, in Rect2 region, Vector2 center, int max, LightingFrame lighting)
    {
        _pointCount = 0;
        _count = 0;
        foreach (var archetype in world.Query<ShadowCaster2D, Transform>())
        {
            var casters = archetype.GetSpan<ShadowCaster2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < casters.Length; i++)
            {
                ref readonly var caster = ref casters[i];
                if (!caster.Enabled)
                    continue;
                var first = _pointCount;
                AppendOutline(caster, transforms[i]);
                var outline = _points.AsSpan(first, _pointCount - first);
                var bounds = Rect2.Bounding(outline);
                if (outline.Length < 2 || !region.Intersects(bounds))
                {
                    _pointCount = first;
                    continue;
                }

                AddCandidate(new Candidate(first, outline.Length, 1u << Math.Clamp(caster.Layer, 0, 31), caster.SelfShadows),
                    Vector2.DistanceSquared(bounds.Center, center));
            }
        }

        if (_count > max)
            _keys.AsSpan(0, _count).Sort(_candidates.AsSpan(0, _count));
        var added = Math.Min(_count, Math.Max(0, max));
        for (var i = 0; i < added; i++)
        {
            var candidate = _candidates[i];
            lighting.AddOccluder(_points.AsSpan(candidate.First, candidate.Count), candidate.Layers, candidate.SelfShadows);
        }

        return added;
    }

    private void AppendOutline(in ShadowCaster2D caster, in Transform transform)
    {
        var matrix = Matrix3x2.CreateScale(transform.Scale) * Matrix3x2.CreateRotation(transform.Rotation) * Matrix3x2.CreateTranslation(transform.Position);
        switch (caster.Shape)
        {
            case ShadowCasterShape.Box:
                var half = caster.Size / 2;
                Append(caster.Offset + new Vector2(-half.X, -half.Y), matrix);
                Append(caster.Offset + new Vector2(half.X, -half.Y), matrix);
                Append(caster.Offset + new Vector2(half.X, half.Y), matrix);
                Append(caster.Offset + new Vector2(-half.X, half.Y), matrix);
                break;
            case ShadowCasterShape.Circle:
                foreach (var point in UnitCircle)
                    Append(caster.Offset + point * caster.Radius, matrix);
                break;
            case ShadowCasterShape.Polygon when caster.Points is { Length: >= 3 } points:
                foreach (var point in points)
                    Append(caster.Offset + point, matrix);
                break;
        }
    }

    private void Append(Vector2 local, in Matrix3x2 matrix)
    {
        if (_pointCount == _points.Length)
            Array.Resize(ref _points, _points.Length * 2);
        _points[_pointCount++] = Vector2.Transform(local, matrix);
    }

    private void AddCandidate(in Candidate candidate, float key)
    {
        if (_count == _candidates.Length)
        {
            Array.Resize(ref _candidates, _count * 2);
            Array.Resize(ref _keys, _count * 2);
        }

        _keys[_count] = key;
        _candidates[_count++] = candidate;
    }

    private static Vector2[] CreateUnitCircle()
    {
        var points = new Vector2[CircleSegments];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i * MathF.Tau / CircleSegments;
            points[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return points;
    }

    private readonly record struct Candidate(int First, int Count, uint Layers, bool SelfShadows);
}
