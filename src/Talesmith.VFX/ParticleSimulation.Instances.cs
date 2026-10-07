using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.VFX.Simulation;

namespace Talesmith.VFX;

public sealed partial class ParticleSimulation
{
    private float[] _sortKeys = [];
    private int[] _sortOrder = [];
    private bool _rotated;

    /// <summary>Writes one sprite per visible particle in draw order and returns how many were written.</summary>
    /// <param name="destination">At least <see cref="AliveCount"/> long.</param>
    /// <param name="source">The texture region drawn for each particle, in pixels.</param>
    /// <param name="localToWorld">The emitter's transform, applied to particles simulated in local space.</param>
    public int WriteInstances(Span<SpriteInstance> destination, in Rect2 source, in Matrix3x2 localToWorld)
    {
        var settings = Settings;
        var count = _particles.Count;
        if (settings is null || count == 0)
            return 0;
        if (destination.Length < count)
            throw new ArgumentException($"The destination holds {destination.Length} sprites but {count} particles are alive.", nameof(destination));

        var renderer = settings.Renderer;
        var p = _particles;
        var px = p.PositionXArray;
        var py = p.PositionYArray;
        var vx = p.VelocityXArray;
        var vy = p.VelocityYArray;
        var age = p.AgeArray;
        var rate = p.AgeRateArray;
        var size = p.SizeArray;
        var rotation = p.RotationArray;
        var colors = p.ColorArray;
        var seeds = p.SeedArray;
        var sizes = settings.SizeOverLifetime.Enabled && !_sizeTable.IsOne ? _sizeTable.Values : null;
        var tints = settings.ColorOverLifetime.Enabled && !_colorTable.IsWhite ? _colorTable.Values : null;
        var local = settings.SimulationSpace == ParticleSimulationSpace.Local;
        var alignment = renderer.Alignment;
        var rotates = HasRotation(settings);
        var stretchLength = renderer.StretchLengthScale;
        var stretchSpeed = renderer.StretchSpeedScale;
        var order = Sort(settings, count, localToWorld);

        var sheet = settings.TextureSheet;
        var animated = sheet.Enabled && sheet.Columns * sheet.Rows > 1;
        var columns = Math.Max(1, sheet.Columns);
        var rows = Math.Max(1, sheet.Rows);
        var frames = sheet.FrameCount > 0 ? Math.Min(sheet.FrameCount, columns * rows) : columns * rows;
        var cell = new Vector2(source.Width / columns, source.Height / rows);
        var overLifetime = sheet.Mode == TextureSheetMode.OverLifetime;
        var cycles = MathF.Max(sheet.Cycles, 0.01f) * frames;

        var written = 0;
        for (var k = 0; k < count; k++)
        {
            var i = order.IsEmpty ? k : order[k];
            var t = age[i];
            var index = CurveTable.Index(t);
            var color = tints is null ? colors[i] * 255f : colors[i] * tints[index];
            if (color.W < 0.5f)
                continue;
            var length = sizes is null ? size[i] : size[i] * sizes[index];
            if (length == 0)
                continue;

            var width = length;
            float cos = 1, sin = 0;
            if (alignment == ParticleAlignment.Rotation)
            {
                if (rotates)
                    (sin, cos) = FastMath.SinCos(rotation[i]);
            }
            else
            {
                var speedSquared = vx[i] * vx[i] + vy[i] * vy[i];
                if (speedSquared > 1e-8f)
                {
                    var speed = MathF.Sqrt(speedSquared);
                    cos = vx[i] / speed;
                    sin = vy[i] / speed;
                    if (alignment == ParticleAlignment.Stretch)
                        length *= stretchLength + speed * stretchSpeed;
                }
            }

            var m11 = cos * length;
            var m12 = sin * length;
            var m21 = -sin * width;
            var m22 = cos * width;
            var transform = new Matrix3x2(m11, m12, m21, m22, px[i] - 0.5f * (m11 + m21), py[i] - 0.5f * (m12 + m22));
            if (local)
                transform *= localToWorld;

            var region = source;
            if (animated)
            {
                var frame = overLifetime ? (int)(t * cycles) : (int)(t / rate[i] * sheet.FramesPerSecond);
                if (sheet.RandomStartFrame)
                    frame += (int)(seeds[i] * frames);
                frame %= frames;
                region = new Rect2(source.X + frame % columns * cell.X, source.Y + frame / columns * cell.Y, cell.X, cell.Y);
            }

            destination[written++] = new SpriteInstance(transform, region,
                new Color((byte)(color.X + 0.5f), (byte)(color.Y + 0.5f), (byte)(color.Z + 0.5f), (byte)(color.W + 0.5f)));
        }

        return written;
    }

    /// <summary>Whether any particle may be turned, so the common unrotated case skips the sine and cosine per particle.</summary>
    private bool HasRotation(ParticleSettings settings)
    {
        if (_particles.Count == 0)
            _rotated = false;
        return _rotated || settings.Initial.AngularVelocity != default || settings.RotationOverLifetime.Enabled || settings.CustomModules.Count > 0;
    }

    private ReadOnlySpan<int> Sort(ParticleSettings settings, int count, in Matrix3x2 localToWorld)
    {
        var mode = settings.Renderer.SortMode;
        if (mode == ParticleSortMode.None)
            return default;
        if (_sortKeys.Length < count)
        {
            _sortKeys = new float[Math.Max(count, _sortKeys.Length * 2)];
            _sortOrder = new int[_sortKeys.Length];
        }

        var age = _particles.AgeArray;
        var local = settings.SimulationSpace == ParticleSimulationSpace.Local;
        for (var i = 0; i < count; i++)
        {
            _sortOrder[i] = i;
            _sortKeys[i] = mode switch
            {
                ParticleSortMode.OldestInFront => age[i],
                ParticleSortMode.YoungestInFront => -age[i],
                _ => local
                    ? Vector2.Transform(new Vector2(_particles.PositionXArray[i], _particles.PositionYArray[i]), localToWorld).Y
                    : _particles.PositionYArray[i]
            };
        }

        Array.Sort(_sortKeys, _sortOrder, 0, count);
        return _sortOrder.AsSpan(0, count);
    }
}
