using System.Runtime.InteropServices;
using SkiaSharp;
using Talesmith.Imaging;

namespace Talesmith.Assets.Textures;

/// <summary>Decodes PNG, JPEG, WebP, BMP, GIF and ICO images into <see cref="ImageData"/>.</summary>
public static class TextureDecoder
{
    /// <summary>Decodes an image; animated images yield their first frame.</summary>
    /// <exception cref="AssetException">The data is not a supported image or is damaged.</exception>
    public static ImageData Decode(Stream stream) => Decode(stream, premultipliedSource: false);

    /// <summary>Decodes an image whose colors may already be multiplied by alpha.</summary>
    /// <param name="premultipliedSource">Keeps the stored colors as they are instead of multiplying them by alpha.</param>
    /// <exception cref="AssetException">The data is not a supported image or is damaged.</exception>
    public static ImageData Decode(Stream stream, bool premultipliedSource)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var data = SKData.Create(stream) ?? throw new AssetException("The image is empty.");
        using var codec = SKCodec.Create(data) ?? throw new AssetException("The data is not a supported image format.");

        var alpha = premultipliedSource ? SKAlphaType.Unpremul : SKAlphaType.Premul;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, alpha);
        var pixels = new byte[info.BytesSize];
        var result = codec.GetPixels(info, pixels);
        if (result != SKCodecResult.Success)
            throw new AssetException($"The {codec.EncodedFormat} image could not be decoded: {result}.");
        return new ImageData(info.Width, info.Height, pixels);
    }

    /// <summary>Scales an image to a new size with high-quality filtering.</summary>
    public static ImageData Resize(ImageData image, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width == image.Width && height == image.Height)
            return image;

        var target = new byte[width * height * 4];
        var sourceHandle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        var targetHandle = GCHandle.Alloc(target, GCHandleType.Pinned);
        try
        {
            using var source = new SKPixmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul), sourceHandle.AddrOfPinnedObject(), image.Stride);
            using var destination = new SKPixmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), targetHandle.AddrOfPinnedObject(), width * 4);
            if (!source.ScalePixels(destination, new SKSamplingOptions(SKCubicResampler.Mitchell)))
                throw new AssetException($"The {image.Width}×{image.Height} image could not be scaled to {width}×{height}.");
        }
        finally
        {
            sourceHandle.Free();
            targetHandle.Free();
        }

        return new ImageData(width, height, target);
    }
}
