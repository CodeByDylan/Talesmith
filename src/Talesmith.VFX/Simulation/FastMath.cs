using System.Runtime.CompilerServices;

namespace Talesmith.VFX.Simulation;

/// <summary>Approximations that are accurate enough for drawing and several times faster than <see cref="MathF"/>.</summary>
internal static class FastMath
{
    private const float B = 4 / MathF.PI;
    private const float C = -4 / (MathF.PI * MathF.PI);
    private const float P = 0.225f;

    /// <summary>Sine and cosine within about 0.001 of the exact values, for any angle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (float Sin, float Cos) SinCos(float angle)
    {
        var x = Wrap(angle);
        var c = Wrap(x + MathF.PI / 2);
        return (Sine(x), Sine(c));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Wrap(float angle) => angle - MathF.Tau * MathF.Round(angle * (1 / MathF.Tau));

    /// <summary>A parabola refined by one correction step; valid from -π to π.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Sine(float x)
    {
        var y = B * x + C * x * MathF.Abs(x);
        return P * (y * MathF.Abs(y) - y) + y;
    }
}
