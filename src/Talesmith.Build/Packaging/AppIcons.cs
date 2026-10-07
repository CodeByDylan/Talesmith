using SkiaSharp;

namespace Talesmith.Build.Packaging;

/// <summary>Converts a PNG icon into a Windows icon (<c>.ico</c>) that holds PNG images.</summary>
internal static class AppIcons
{
    private static readonly int[] WindowsSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <exception cref="InvalidDataException">The image is not a readable PNG.</exception>
    public static byte[] ToIco(byte[] png)
    {
        var images = Resize(png, WindowsSizes);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);
        var offset = 6 + (16 * images.Count);
        foreach (var (size, bytes) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(bytes.Length);
            writer.Write(offset);
            offset += bytes.Length;
        }

        foreach (var (_, bytes) in images)
            writer.Write(bytes);
        writer.Flush();
        return output.ToArray();
    }

    private static List<(int Size, byte[] Png)> Resize(byte[] png, IReadOnlyList<int> sizes)
    {
        using var source = SKBitmap.Decode(png) ?? throw new InvalidDataException("The icon is not a readable PNG image.");
        var images = new List<(int, byte[])>();
        foreach (var size in sizes)
        {
            using var scaled = source.Resize(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var data = (scaled ?? source).Encode(SKEncodedImageFormat.Png, 100);
            images.Add((size, data.ToArray()));
        }

        return images;
    }
}
