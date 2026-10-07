namespace Talesmith.Rendering.Vulkan;

/// <summary>The pixels of a completed frame: premultiplied BGRA8 rows top to bottom, valid until the next render call.</summary>
/// <remarks>The layout matches Avalonia's <c>PixelFormat.Bgra8888</c> with <c>AlphaFormat.Premul</c>.</remarks>
public readonly ref struct ReadbackImage
{
    internal ReadbackImage(int width, int height, ReadOnlySpan<byte> pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Bytes per row.</summary>
    public int Stride => Width * 4;

    public ReadOnlySpan<byte> Pixels { get; }
}
