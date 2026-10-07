using System.Numerics;
using System.Runtime.CompilerServices;
using Talesmith.Mathematics;

namespace Talesmith.VFX.Simulation;

/// <summary>A <see cref="Curve"/> sampled over a particle's life, rebaked only when a different curve instance is assigned.</summary>
/// <remarks>Curves are immutable, so comparing references detects every edit without hashing.</remarks>
internal sealed class CurveTable
{
    public const int Samples = 256;
    private const float Scale = Samples - 1;

    private readonly float[] _values = new float[Samples];
    private Curve? _source;

    public float Min { get; private set; } = 1;

    public float Max { get; private set; } = 1;

    /// <summary>Whether every sample is 1, so multiplying by the table can be skipped.</summary>
    public bool IsOne { get; private set; } = true;

    public float[] Values => _values;

    public void Update(Curve curve)
    {
        if (ReferenceEquals(curve, _source))
            return;
        _source = curve;
        float min = float.MaxValue, max = float.MinValue;
        for (var i = 0; i < Samples; i++)
        {
            var value = curve.Evaluate(i / Scale);
            _values[i] = value;
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        Min = min;
        Max = max;
        IsOne = min == 1 && max == 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(float age) => (int)(Math.Clamp(age, 0, 1) * Scale + 0.5f);
}

/// <summary>A <see cref="Gradient"/> sampled over a particle's life as straight-alpha colors from 0 to 255.</summary>
internal sealed class GradientTable
{
    private readonly Vector4[] _values = new Vector4[CurveTable.Samples];
    private Gradient? _source;

    public bool IsWhite { get; private set; } = true;

    public Vector4[] Values => _values;

    public void Update(Gradient gradient)
    {
        if (ReferenceEquals(gradient, _source))
            return;
        _source = gradient;
        var white = true;
        for (var i = 0; i < _values.Length; i++)
        {
            var color = gradient.Evaluate(i / (float)(_values.Length - 1));
            white &= color == Color.White;
            _values[i] = color.ToVector4() * 255f;
        }

        IsWhite = white;
    }
}
