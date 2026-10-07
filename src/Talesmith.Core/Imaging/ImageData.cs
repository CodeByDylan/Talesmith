using Talesmith.Mathematics;

namespace Talesmith.Imaging;

/// <summary>A decoded image in the engine's canonical format: 8-bit RGBA with premultiplied alpha, rows top to bottom.</summary>
public sealed class ImageData
{
    public ImageData(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for a {width}×{height} RGBA image, got {pixels.Length}.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Premultiplied RGBA bytes, <see cref="Width"/> × 4 bytes per row.</summary>
    public byte[] Pixels { get; }

    public int Stride => Width * 4;

    /// <summary>Creates an image filled with one color.</summary>
    public static ImageData Solid(int width, int height, Color color)
    {
        var pixels = new byte[width * height * 4];
        var a = color.A / 255f;
        byte r = (byte)(color.R * a + 0.5f), g = (byte)(color.G * a + 0.5f), b = (byte)(color.B * a + 0.5f);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
            pixels[i + 3] = color.A;
        }

        return new ImageData(width, height, pixels);
    }

    /// <summary>Gets the straight-alpha color of a pixel, mainly for tools and tests.</summary>
    public Color GetPixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        var a = Pixels[i + 3];
        if (a == 0)
            return Color.Transparent;
        return new Color((byte)Math.Min(255, Pixels[i] * 255 / a), (byte)Math.Min(255, Pixels[i + 1] * 255 / a), (byte)Math.Min(255, Pixels[i + 2] * 255 / a), a);
    }
}
