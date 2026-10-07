using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>Edits immutable <see cref="Curve"/>s one key at a time, as the curve editor does.</summary>
public static class CurveEditing
{
    /// <summary>The smallest time between neighbouring keys.</summary>
    public const float MinimumKeySpacing = 0.001f;

    private const float MaximumSlope = 10_000;

    /// <summary>Adds a key at <paramref name="time"/> whose tangent follows the curve there, and returns its index.</summary>
    public static Curve AddKey(Curve curve, float time, float value, out int index)
    {
        var keys = curve.Keys;
        foreach (var existing in keys)
        {
            if (Math.Abs(existing.Time - time) < MinimumKeySpacing)
            {
                index = keys.IndexOf(existing);
                return Replace(curve, index, existing with { Value = value });
            }
        }

        var slope = keys.Length < 2 ? 0 : Slope(curve, time);
        var previous = keys.LastOrDefault(k => k.Time < time);
        var interpolation = keys.Any(k => k.Time < time) ? previous.Interpolation : keys.Length > 0 ? keys[0].Interpolation : CurveInterpolation.Smooth;
        var added = new CurveKey(time, value, slope, slope, interpolation);
        var result = new Curve(keys.Add(added));
        index = result.Keys.IndexOf(added);
        return result;
    }

    /// <summary>Removes the key at <paramref name="index"/>; the last remaining key cannot be removed.</summary>
    public static Curve RemoveKey(Curve curve, int index)
    {
        var keys = curve.Keys;
        if (keys.Length <= 1 || (uint)index >= (uint)keys.Length)
            return curve;
        return new Curve(keys.RemoveAt(index));
    }

    /// <summary>Moves the key at <paramref name="index"/>, keeping it between its neighbours so its index never changes.</summary>
    public static Curve MoveKey(Curve curve, int index, float time, float value)
    {
        var keys = curve.Keys;
        if ((uint)index >= (uint)keys.Length)
            return curve;

        var min = index > 0 ? keys[index - 1].Time + MinimumKeySpacing : float.NegativeInfinity;
        var max = index < keys.Length - 1 ? keys[index + 1].Time - MinimumKeySpacing : float.PositiveInfinity;
        return Replace(curve, index, keys[index] with { Time = Math.Clamp(time, min, Math.Max(min, max)), Value = value });
    }

    /// <summary>Sets the slopes arriving at and leaving the key at <paramref name="index"/>.</summary>
    public static Curve SetTangents(Curve curve, int index, float inTangent, float outTangent)
    {
        if ((uint)index >= (uint)curve.Keys.Length)
            return curve;
        return Replace(curve, index, curve.Keys[index] with
        {
            InTangent = Math.Clamp(inTangent, -MaximumSlope, MaximumSlope),
            OutTangent = Math.Clamp(outTangent, -MaximumSlope, MaximumSlope)
        });
    }

    /// <summary>Sets how the segment starting at the key at <paramref name="index"/> is drawn.</summary>
    public static Curve SetInterpolation(Curve curve, int index, CurveInterpolation interpolation)
    {
        if ((uint)index >= (uint)curve.Keys.Length)
            return curve;
        return Replace(curve, index, curve.Keys[index] with { Interpolation = interpolation });
    }

    /// <summary>Gives the key at <paramref name="index"/> horizontal tangents.</summary>
    public static Curve Flatten(Curve curve, int index) => SetTangents(curve, index, 0, 0);

    /// <summary>Gives the key at <paramref name="index"/> the slope between its neighbours, for a smooth pass through it.</summary>
    public static Curve AutoTangents(Curve curve, int index)
    {
        var keys = curve.Keys;
        if ((uint)index >= (uint)keys.Length || keys.Length < 2)
            return curve;

        var previous = keys[Math.Max(0, index - 1)];
        var next = keys[Math.Min(keys.Length - 1, index + 1)];
        var span = next.Time - previous.Time;
        var slope = span <= 0 ? 0 : (next.Value - previous.Value) / span;
        return SetTangents(curve, index, slope, slope);
    }

    /// <summary>Gets the slope a tangent handle at <paramref name="handle"/> gives the key at <paramref name="key"/>.</summary>
    /// <param name="key">The key's time and value.</param>
    /// <param name="handle">The handle's time and value.</param>
    public static float SlopeFromHandle((float Time, float Value) key, (float Time, float Value) handle, bool isOutHandle)
    {
        var dt = handle.Time - key.Time;
        var dv = handle.Value - key.Value;
        if (isOutHandle ? dt <= 1e-6f : dt >= -1e-6f)
            return (dv >= 0) == isOutHandle ? MaximumSlope : -MaximumSlope;
        return Math.Clamp(dv / dt, -MaximumSlope, MaximumSlope);
    }

    /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="increment"/>.</summary>
    public static float Snap(float value, float increment) =>
        increment > 0 ? MathF.Round(value / increment) * increment : value;

    /// <summary>Gets the range of values the curve reaches between <paramref name="start"/> and <paramref name="end"/>, including overshoot.</summary>
    public static (float Min, float Max) ValueRange(Curve curve, float start, float end, int samples = 64)
    {
        var min = float.PositiveInfinity;
        var max = float.NegativeInfinity;
        foreach (var key in curve.Keys)
        {
            min = Math.Min(min, key.Value);
            max = Math.Max(max, key.Value);
        }

        for (var i = 0; i <= samples; i++)
        {
            var value = curve.Evaluate(start + (end - start) * i / samples);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        return float.IsFinite(min) ? (min, max) : (0, 1);
    }

    private static float Slope(Curve curve, float time)
    {
        const float h = 0.0005f;
        return (curve.Evaluate(time + h) - curve.Evaluate(time - h)) / (2 * h);
    }

    private static Curve Replace(Curve curve, int index, CurveKey key) => new(curve.Keys.SetItem(index, key));
}
