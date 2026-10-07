using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talesmith.VFX;

/// <summary>A small, fast, seedable random number generator (SplitMix64), so effects replay identically from the same seed.</summary>
public struct ParticleRandom(ulong seed)
{
    private ulong _state = seed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (uint)((z ^ (z >> 31)) >> 32);
    }

    /// <summary>A number from 0 (inclusive) to 1 (exclusive).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>A number from 0 (inclusive) to <paramref name="count"/> (exclusive).</summary>
    public int Next(int count) => (int)((ulong)NextUInt() * (uint)count >> 32);

    /// <summary>A unit vector in a uniformly random direction.</summary>
    public Vector2 OnUnitCircle()
    {
        var (sin, cos) = MathF.SinCos(NextFloat() * MathF.Tau);
        return new Vector2(cos, sin);
    }
}
