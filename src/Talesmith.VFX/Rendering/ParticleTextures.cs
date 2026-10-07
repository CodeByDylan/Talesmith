using Talesmith.Imaging;

namespace Talesmith.VFX.Rendering;

/// <summary>Generates the <see cref="BuiltInParticleTexture"/> images: white shapes whose alpha carries the shape, tinted per particle.</summary>
public static class ParticleTextures
{
    public static ImageData Create(BuiltInParticleTexture texture) => texture switch
    {
        BuiltInParticleTexture.SoftCircle => Generate(64, (x, y, r) => Squared(MathF.Max(0, 1 - r * r))),
        BuiltInParticleTexture.Glow => Generate(128, (x, y, r) => MathF.Exp(-r * r * 22) + 0.4f * MathF.Pow(MathF.Max(0, 1 - r), 2.5f)),
        BuiltInParticleTexture.Disc => Generate(64, (x, y, r) => (1 - r) * 32),
        BuiltInParticleTexture.Smoke => Generate(128, Smoke),
        BuiltInParticleTexture.Sparkle => Generate(64, Sparkle),
        BuiltInParticleTexture.Streak => Generate(64, (x, y, r) => MathF.Exp(-y * y * 30) * Squared(MathF.Max(0, 1 - x * x))),
        _ => Generate(8, (x, y, r) => 1)
    };

    /// <summary>Fills a square image; <paramref name="alpha"/> receives coordinates from -1 to 1 and the distance from the center.</summary>
    private static ImageData Generate(int size, Func<float, float, float, float> alpha)
    {
        var pixels = new byte[size * size * 4];
        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                var x = (px + 0.5f) / size * 2 - 1;
                var y = (py + 0.5f) / size * 2 - 1;
                var r = MathF.Sqrt(x * x + y * y);
                var value = (byte)(Math.Clamp(alpha(x, y, r), 0, 1) * 255 + 0.5f);
                var i = (py * size + px) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = value;
            }
        }

        return new ImageData(size, size, pixels);
    }

    private static float Smoke(float x, float y, float r)
    {
        var angle = MathF.Atan2(y, x);
        var billow = 0.07f * MathF.Sin(angle * 3 + 1.3f) + 0.04f * MathF.Sin(angle * 5 + 0.4f);
        var edge = r / (0.88f + billow + 0.12f * (Fbm(x * 1.6f + 7.3f, y * 1.6f + 2.9f) - 0.5f));
        var body = Squared(MathF.Max(0, 1 - edge * edge));
        var texture = 0.55f + 0.45f * Fbm(x * 2.2f + 3.1f, y * 2.2f + 1.7f);
        return body * texture;
    }

    private static float Sparkle(float x, float y, float r)
    {
        var arms = MathF.Exp(-MathF.Abs(x) * 16) * MathF.Exp(-MathF.Abs(y) * 2.2f) + MathF.Exp(-MathF.Abs(y) * 16) * MathF.Exp(-MathF.Abs(x) * 2.2f);
        var diagonal = 0.35f * (MathF.Exp(-MathF.Abs(x - y) * 14) + MathF.Exp(-MathF.Abs(x + y) * 14)) * MathF.Exp(-r * 4);
        var core = MathF.Exp(-r * r * 40);
        return (arms + diagonal) * MathF.Max(0, 1 - r) + core;
    }

    private static float Fbm(float x, float y)
    {
        var sum = 0f;
        var amplitude = 0.5f;
        for (var octave = 0; octave < 4; octave++)
        {
            sum += amplitude * ValueNoise(x, y);
            x *= 2.03f;
            y *= 2.03f;
            amplitude *= 0.5f;
        }

        return sum / 0.9375f;
    }

    private static float ValueNoise(float x, float y)
    {
        var ix = MathF.Floor(x);
        var iy = MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var d = Hash(ix + 1, iy + 1);
        return float.Lerp(float.Lerp(a, b, fx), float.Lerp(c, d, fx), fy);
    }

    private static float Hash(float x, float y)
    {
        var h = (uint)(int)x * 374761393u + (uint)(int)y * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static float Squared(float value) => value * value;
}
