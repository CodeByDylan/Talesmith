using System.Numerics;

namespace Talesmith.Rendering.Lighting;

/// <summary>Turns a frame's occluder edges into one-dimensional shadow maps, one row per shadow-casting light, for renderers to sample.</summary>
/// <remarks>
/// <para>A point or spot light's row stores, for each direction around the light, where its shadow starts as a distance divided by the
/// light's radius; direction <c>s</c> from 0 to 1 is the angle <c>atan2(y, x) / 2π + 0.5</c>. A directional light's row stores, for
/// each column across its area, the depth along the light's direction where the shadow starts, both mapped from the area to 0..1.
/// 1 means nothing is in the way. Shaders compare a pixel's own distance with the row, filtering neighboring entries for soft edges.</para>
/// <para>The shadow starts where the light leaves the occluders it passed through, or meets the near side of one that shadows itself.
/// Occluders that touch act as one shape. What lies inside occluders that do not shadow themselves stays lit, which
/// <see cref="OccluderMaskBuilder"/> tells shaders.</para>
/// <para>Building is backend-independent and runs on the render thread; buffers are reused between frames.</para>
/// </remarks>
public sealed class ShadowMapBuilder
{
    private const float Tau = MathF.PI * 2;

    private readonly ShadowCrossings _crossings = new();
    private Half[] _data = [];
    private int[] _rows = [];
    private Vector2[] _directions = [];

    /// <summary>The entries per row.</summary>
    public int Width { get; private set; }

    /// <summary>The rows written by the last <see cref="Build"/>; at least 1, so the map can always be bound.</summary>
    public int RowCount { get; private set; } = 1;

    /// <summary>The rows, <see cref="Width"/> entries each.</summary>
    public ReadOnlySpan<Half> Data => _data.AsSpan(0, Width * RowCount);

    /// <summary>The row of a light, by its index in <see cref="LightingFrame.Lights"/>, or -1 when it casts no shadows.</summary>
    public int RowOf(int light) => _rows[light];

    /// <summary>The texture coordinate of the center of a row, for a map exactly <see cref="RowCount"/> rows high.</summary>
    public float RowCoordinate(int row) => (row + 0.5f) / RowCount;

    public void Build(LightingFrame lighting)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        var lights = lighting.Lights;
        var width = lighting.Settings.ShadowResolution;
        Prepare(width, lights.Length);

        var rows = 0;
        for (var i = 0; i < lights.Length; i++)
        {
            ref readonly var light = ref lights[i];
            if (!light.CastsShadows || light.Radius <= 0 || light.ShadowStrength <= 0)
            {
                _rows[i] = -1;
                continue;
            }

            _rows[i] = rows;
            EnsureRows(rows + 1);
            _crossings.Reset(width);
            if (light.Type == LightType.Directional)
                RasterizeDirectional(lighting, light);
            else
                RasterizeRadial(lighting, light);

            var target = _data.AsSpan(rows * width, width);
            for (var x = 0; x < width; x++)
                target[x] = (Half)_crossings.ShadowStart(x);
            rows++;
        }

        RowCount = Math.Max(1, rows);
        if (rows == 0)
            _data.AsSpan(0, width).Fill(Half.One);
    }

    private void Prepare(int width, int lights)
    {
        if (_rows.Length < lights)
            _rows = new int[Math.Max(lights, _rows.Length * 2)];
        if (Width == width)
            return;

        Width = width;
        _directions = new Vector2[width];
        for (var i = 0; i < width; i++)
        {
            var angle = (i + 0.5f) / width * Tau - MathF.PI;
            _directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        _data = new Half[width * 8];
    }

    private void EnsureRows(int rows)
    {
        if (_data.Length < rows * Width)
            Array.Resize(ref _data, Math.Max(rows * Width, _data.Length * 2));
    }

    private void RasterizeRadial(LightingFrame lighting, in FrameLight light)
    {
        var center = light.Position;
        var inverseRadius = 1 / light.Radius;
        var radiusSquared = light.Radius * light.Radius;
        var bounds = light.Bounds;
        var edges = lighting.Edges;
        foreach (ref readonly var group in lighting.Occluders)
        {
            if ((group.Layers & light.ShadowLayers) == 0 || !group.Bounds.Intersects(bounds))
                continue;
            foreach (ref readonly var edge in edges.Slice(group.FirstEdge, group.EdgeCount))
            {
                var a = edge.Start - center;
                var b = edge.End - center;
                if (DistanceSquaredToSegment(a, b) >= radiusSquared)
                    continue;
                var kind = group.SelfShadows ? CrossingKind.Block : edge.Faces(center) ? CrossingKind.Enter : CrossingKind.Exit;
                RasterizeRadialEdge(a, b, inverseRadius, kind);
            }
        }
    }

    private void RasterizeRadialEdge(Vector2 a, Vector2 b, float inverseRadius, CrossingKind kind)
    {
        var width = Width;
        var angleA = MathF.Atan2(a.Y, a.X);
        var delta = MathF.Atan2(b.Y, b.X) - angleA;
        if (delta > MathF.PI)
            delta -= Tau;
        else if (delta < -MathF.PI)
            delta += Tau;
        if (MathF.Abs(delta) > MathF.PI - 1e-4f)
            return;

        // The directions whose centers lie in [start, end), so a direction through the corner between two edges crosses only one of them.
        var start = delta >= 0 ? angleA : angleA + delta;
        var binAngle = Tau / width;
        var first = (int)MathF.Ceiling((start + MathF.PI) / binAngle - 0.5f);
        var last = (int)MathF.Ceiling((start + MathF.Abs(delta) + MathF.PI) / binAngle - 0.5f) - 1;

        var edge = b - a;
        var crossAEdge = Cross(a, edge);
        for (var k = first; k <= last; k++)
        {
            var index = ((k % width) + width) % width;
            var denominator = Cross(_directions[index], edge);
            if (MathF.Abs(denominator) < 1e-12f)
                continue;
            var distance = crossAEdge / denominator;
            if (distance > 0)
                _crossings.Add(index, distance * inverseRadius, kind);
        }
    }

    private void RasterizeDirectional(LightingFrame lighting, in FrameLight light)
    {
        var width = Width;
        var center = light.Position;
        var direction = light.Direction;
        var across = new Vector2(-direction.Y, direction.X);
        var scale = 0.5f / light.Radius;
        var bounds = light.Bounds;
        var edges = lighting.Edges;
        foreach (ref readonly var group in lighting.Occluders)
        {
            if ((group.Layers & light.ShadowLayers) == 0 || !group.Bounds.Intersects(bounds))
                continue;
            foreach (ref readonly var edge in edges.Slice(group.FirstEdge, group.EdgeCount))
            {
                var along = edge.End - edge.Start;
                var kind = group.SelfShadows ? CrossingKind.Block
                    : along.X * direction.Y - along.Y * direction.X > 0 ? CrossingKind.Enter : CrossingKind.Exit;
                var a = edge.Start - center;
                var b = edge.End - center;
                var uA = Vector2.Dot(a, across) * scale + 0.5f;
                var uB = Vector2.Dot(b, across) * scale + 0.5f;
                var zA = Vector2.Dot(a, direction) * scale + 0.5f;
                var zB = Vector2.Dot(b, direction) * scale + 0.5f;
                if (uA > uB)
                {
                    (uA, uB) = (uB, uA);
                    (zA, zB) = (zB, zA);
                }

                // The columns whose centers lie in [uA, uB), as for radial edges.
                var first = Math.Max(0, (int)MathF.Ceiling(uA * width - 0.5f));
                var last = Math.Min(width - 1, (int)MathF.Ceiling(uB * width - 0.5f) - 1);
                var span = uB - uA;
                for (var k = first; k <= last; k++)
                {
                    var t = span > 1e-6f ? ((k + 0.5f) / width - uA) / span : 0;
                    _crossings.Add(k, MathF.Max(0, zA + (zB - zA) * t), kind);
                }
            }
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>The squared distance from the origin to the segment between two points.</summary>
    private static float DistanceSquaredToSegment(Vector2 a, Vector2 b)
    {
        var edge = b - a;
        var lengthSquared = edge.LengthSquared();
        var t = lengthSquared > 0 ? Math.Clamp(-Vector2.Dot(a, edge) / lengthSquared, 0, 1) : 0;
        return (a + edge * t).LengthSquared();
    }
}
