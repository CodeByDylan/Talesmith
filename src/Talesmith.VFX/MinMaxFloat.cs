using Talesmith.Authoring;

namespace Talesmith.VFX;

/// <summary>A number that is either constant or picked at random between two values, once per particle.</summary>
/// <remarks>Equal <see cref="Min"/> and <see cref="Max"/> make the value constant.</remarks>
public struct MinMaxFloat(float min, float max) : IEquatable<MinMaxFloat>
{
    [Tooltip("The smallest value, or the value when both are equal.")]
    public float Min = min;

    [Tooltip("The largest value; equal to Min for a constant.")]
    public float Max = max;

    public MinMaxFloat(float value) : this(value, value)
    {
    }

    public static MinMaxFloat Constant(float value) => new(value);

    public static MinMaxFloat Between(float min, float max) => new(min, max);

    public readonly bool IsConstant() => Min == Max;

    public readonly float Average() => (Min + Max) * 0.5f;

    /// <summary>The value furthest from zero, for bounds estimates.</summary>
    public readonly float LargestMagnitude() => MathF.Max(MathF.Abs(Min), MathF.Abs(Max));

    public readonly float Sample(ref ParticleRandom random) => Min == Max ? Min : Min + (Max - Min) * random.NextFloat();

    public readonly bool Equals(MinMaxFloat other) => Min.Equals(other.Min) && Max.Equals(other.Max);

    public override readonly bool Equals(object? obj) => obj is MinMaxFloat other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Min, Max);

    public static bool operator ==(MinMaxFloat left, MinMaxFloat right) => left.Equals(right);

    public static bool operator !=(MinMaxFloat left, MinMaxFloat right) => !left.Equals(right);

    public static implicit operator MinMaxFloat(float value) => new(value);

    public override readonly string ToString() => Min == Max ? $"{Min}" : $"{Min}..{Max}";
}
