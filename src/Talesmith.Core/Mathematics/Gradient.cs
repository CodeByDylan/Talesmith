using System.Collections.Immutable;

namespace Talesmith.Mathematics;

/// <summary>A color at a position from 0 to 1 along a <see cref="Gradient"/>.</summary>
public readonly record struct GradientStop(float Position, Color Color);

/// <summary>An immutable blend between colors, such as color over a particle's lifetime.</summary>
public sealed record Gradient
{
    private readonly ImmutableArray<GradientStop> _stops;

    public Gradient(IEnumerable<GradientStop> stops)
    {
        _stops = [.. stops.OrderBy(s => s.Position)];
    }

    public static Gradient Solid(Color color) => new([new GradientStop(0, color)]);

    public static Gradient Between(Color from, Color to) => new([new GradientStop(0, from), new GradientStop(1, to)]);

    public static Gradient White { get; } = Solid(Color.White);

    public ImmutableArray<GradientStop> Stops => _stops;

    public Color Evaluate(float position)
    {
        var stops = _stops.AsSpan();
        if (stops.Length == 0)
            return Color.White;
        if (position <= stops[0].Position)
            return stops[0].Color;
        if (position >= stops[^1].Position)
            return stops[^1].Color;

        var hi = 1;
        while (stops[hi].Position < position)
            hi++;
        ref readonly var a = ref stops[hi - 1];
        ref readonly var b = ref stops[hi];
        var span = b.Position - a.Position;
        return span <= 0 ? b.Color : Color.Lerp(a.Color, b.Color, (position - a.Position) / span);
    }

    /// <summary>Samples the gradient evenly from 0 to 1, for lookups in hot loops.</summary>
    public Color[] Bake(int samples)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);
        var table = new Color[samples];
        for (var i = 0; i < samples; i++)
            table[i] = Evaluate(i / (float)(samples - 1));
        return table;
    }

    public bool Equals(Gradient? other) => other is not null && _stops.AsSpan().SequenceEqual(other._stops.AsSpan());

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var stop in _stops)
            hash.Add(stop);
        return hash.ToHashCode();
    }
}
