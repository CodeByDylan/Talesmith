using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talesmith.VFX.Simulation;

/// <summary>A tiling, divergence-free 2D flow (the curl of smooth noise), baked once so turbulence costs two table lookups per particle.</summary>
/// <remarks>Divergence-free flow swirls particles around without bunching them up or tearing them apart, which reads as smoke and air.</remarks>
internal static class FlowField
{
    public const int Size = 64;
    private const int Mask = Size - 1;
    private const int Lattice = 8;

    private static readonly Vector2[] Field = Bake();

    /// <summary>Samples the flow at a position in field cells; the field repeats every <see cref="Size"/> cells.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Sample(float x, float y)
    {
        var fx = MathF.Floor(x);
        var fy = MathF.Floor(y);
        var tx = x - fx;
        var ty = y - fy;
        var x0 = (int)fx & Mask;
        var y0 = (int)fy & Mask;
        var x1 = (x0 + 1) & Mask;
        var y1 = (y0 + 1) & Mask;
        var field = Field;
        var top = Vector2.Lerp(field[y0 * Size + x0], field[y0 * Size + x1], tx);
        var bottom = Vector2.Lerp(field[y1 * Size + x0], field[y1 * Size + x1], tx);
        return Vector2.Lerp(top, bottom, ty);
    }

    private static Vector2[] Bake()
    {
        var random = new ParticleRandom(0x5EED_F10Eu);
        var potential = new float[Size * Size];
        for (var octave = 0; octave < 3; octave++)
        {
            var cells = Lattice << octave;
            var lattice = new float[cells * cells];
            for (var i = 0; i < lattice.Length; i++)
                lattice[i] = random.NextFloat() * 2 - 1;
            var amplitude = 1f / (1 << octave);
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                    potential[y * Size + x] += amplitude * ValueNoise(lattice, cells, x * cells / (float)Size, y * cells / (float)Size);
            }
        }

        var field = new Vector2[Size * Size];
        var largest = 0f;
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var dx = potential[y * Size + ((x + 1) & Mask)] - potential[y * Size + ((x - 1) & Mask)];
                var dy = potential[((y + 1) & Mask) * Size + x] - potential[((y - 1) & Mask) * Size + x];
                var flow = new Vector2(dy, -dx);
                field[y * Size + x] = flow;
                largest = MathF.Max(largest, flow.Length());
            }
        }

        for (var i = 0; i < field.Length; i++)
            field[i] /= largest;
        return field;
    }

    private static float ValueNoise(float[] lattice, int cells, float x, float y)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var tx = Smooth(x - x0);
        var ty = Smooth(y - y0);
        float At(int ix, int iy) => lattice[((iy % cells + cells) % cells) * cells + (ix % cells + cells) % cells];
        var top = float.Lerp(At(x0, y0), At(x0 + 1, y0), tx);
        var bottom = float.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx);
        return float.Lerp(top, bottom, ty);
    }

    private static float Smooth(float t) => t * t * t * (t * (t * 6 - 15) + 10);
}
