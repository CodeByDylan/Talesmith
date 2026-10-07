namespace Talesmith.Lighting;

/// <summary>Evaluates <see cref="LightAnimation"/>s as a factor applied to a light's intensity.</summary>
public static class LightAnimations
{
    /// <summary>The intensity factor, from 1 - <paramref name="amount"/> to 1, after <paramref name="time"/> seconds of animation.</summary>
    /// <param name="seed">Offsets flickers so lights do not flicker in step.</param>
    public static float Factor(LightAnimation animation, float time, float amount, int seed)
    {
        if (animation == LightAnimation.None || time <= 0 || amount <= 0)
            return 1;
        amount = Math.Clamp(amount, 0, 1);
        return animation switch
        {
            LightAnimation.Pulse => 1 - amount * (0.5f - 0.5f * MathF.Cos(time * MathF.Tau)),
            LightAnimation.Flicker => 1 - amount * (0.6f * Noise(time * 7 + seed * 0.618f) + 0.4f * Noise(time * 17.3f + seed * 1.37f)),
            _ => 1
        };
    }

    /// <summary>Smooth value noise from 0 to 1.</summary>
    private static float Noise(float x)
    {
        var cell = MathF.Floor(x);
        var t = x - cell;
        t = t * t * (3 - 2 * t);
        var i = (int)cell;
        return Hash(i) + (Hash(i + 1) - Hash(i)) * t;
    }

    private static float Hash(int n)
    {
        var h = (uint)n * 0x9E3779B1u;
        h ^= h >> 15;
        h *= 0x85EBCA77u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }
}
