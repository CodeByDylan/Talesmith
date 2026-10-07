using System.Numerics;
using Talesmith.Authoring;
using Talesmith.Mathematics;

namespace Talesmith.VFX;

/// <summary>A color that is either constant or blended at random between two colors, once per particle.</summary>
public struct MinMaxColor(Color from, Color to) : IEquatable<MinMaxColor>
{
    [Label("Color")]
    [Tooltip("The color, or one end of the random range.")]
    public Color From = from;

    [Label("Or")]
    [Tooltip("The other end of the random range; equal to the first color for a constant.")]
    public Color To = to;

    public MinMaxColor(Color color) : this(color, color)
    {
    }

    public static MinMaxColor White => new(Color.White);

    public readonly bool IsConstant() => From == To;

    /// <summary>A straight-alpha color with components from 0 to 1.</summary>
    public readonly Vector4 Sample(ref ParticleRandom random) =>
        From == To ? From.ToVector4() : Vector4.Lerp(From.ToVector4(), To.ToVector4(), random.NextFloat());

    public readonly bool Equals(MinMaxColor other) => From == other.From && To == other.To;

    public override readonly bool Equals(object? obj) => obj is MinMaxColor other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(From, To);

    public static bool operator ==(MinMaxColor left, MinMaxColor right) => left.Equals(right);

    public static bool operator !=(MinMaxColor left, MinMaxColor right) => !left.Equals(right);

    public static implicit operator MinMaxColor(Color color) => new(color);
}
