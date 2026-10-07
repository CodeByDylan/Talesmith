using System.Collections.Immutable;

namespace Talesmith.Mathematics;

/// <summary>How a curve moves from one key to the next.</summary>
public enum CurveInterpolation : byte
{
    /// <summary>Cubic Hermite through the keys' tangents.</summary>
    Smooth,
    Linear,

    /// <summary>Holds the key's value until the next key.</summary>
    Constant
}

/// <summary>A point on a <see cref="Curve"/>.</summary>
/// <param name="InTangent">The slope arriving at the key.</param>
/// <param name="OutTangent">The slope leaving the key.</param>
/// <param name="Interpolation">How the segment that starts at this key is drawn.</param>
public readonly record struct CurveKey(float Time, float Value, float InTangent = 0, float OutTangent = 0, CurveInterpolation Interpolation = CurveInterpolation.Smooth);

/// <summary>An immutable keyframed function of one value, such as size over a particle's lifetime.</summary>
/// <remarks>Keys are kept sorted by time. Before the first key and after the last the curve holds their values.</remarks>
public sealed record Curve
{
    private readonly ImmutableArray<CurveKey> _keys;

    public Curve(IEnumerable<CurveKey> keys)
    {
        _keys = [.. keys.OrderBy(k => k.Time)];
    }

    public static Curve Constant(float value) => new([new CurveKey(0, value)]);

    public static Curve Linear(float from, float to) =>
        new([new CurveKey(0, from, to - from, to - from), new CurveKey(1, to, to - from, to - from)]);

    public static Curve One { get; } = Constant(1);

    public ImmutableArray<CurveKey> Keys => _keys;

    public float Evaluate(float time)
    {
        var keys = _keys.AsSpan();
        if (keys.Length == 0)
            return 0;
        if (time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[^1].Time)
            return keys[^1].Value;

        var hi = 1;
        while (keys[hi].Time < time)
            hi++;
        ref readonly var a = ref keys[hi - 1];
        ref readonly var b = ref keys[hi];
        var span = b.Time - a.Time;
        if (span <= 0)
            return b.Value;
        var t = (time - a.Time) / span;
        switch (a.Interpolation)
        {
            case CurveInterpolation.Constant:
                return a.Value;
            case CurveInterpolation.Linear:
                return MathHelper.Lerp(a.Value, b.Value, t);
            default:
                var t2 = t * t;
                var t3 = t2 * t;
                return (2 * t3 - 3 * t2 + 1) * a.Value + (t3 - 2 * t2 + t) * span * a.OutTangent
                    + (-2 * t3 + 3 * t2) * b.Value + (t3 - t2) * span * b.InTangent;
        }
    }

    /// <summary>Samples the curve evenly from <paramref name="start"/> to <paramref name="end"/>, for lookups in hot loops.</summary>
    public float[] Bake(int samples, float start = 0, float end = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);
        var table = new float[samples];
        for (var i = 0; i < samples; i++)
            table[i] = Evaluate(MathHelper.Lerp(start, end, i / (float)(samples - 1)));
        return table;
    }

    public bool Equals(Curve? other) => other is not null && _keys.AsSpan().SequenceEqual(other._keys.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var key in _keys)
            hash.Add(key);
        return hash.ToHashCode();
    }
}
