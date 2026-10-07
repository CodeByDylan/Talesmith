using System.Runtime.InteropServices;
using SkiaSharp;
using Talesmith.Imaging;

namespace Talesmith.Editor.Assets.Thumbnails;

/// <summary>Conversions and scaling between engine images, Skia bitmaps and PNG files, for thumbnails and previews.</summary>
internal static class SkiaImages
{
    /// <summary>Copies premultiplied RGBA pixels into a Skia bitmap.</summary>
    public static SKBitmap ToBitmap(ImageData image)
    {
        var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        Marshal.Copy(image.Pixels, 0, bitmap.GetPixels(), image.Pixels.Length);
        return bitmap;
    }

    /// <summary>Draws a region of an image centered into a square of <paramref name="size"/> pixels: small pixel art is enlarged by whole
    /// factors with sharp pixels, large images are reduced smoothly.</summary>
    public static SKBitmap Fit(SKBitmap source, SKRectI region, int size)
    {
        var scale = Math.Min((float)size / region.Width, (float)size / region.Height);
        if (scale >= 1)
            scale = MathF.Max(1, MathF.Floor(scale));
        var width = Math.Max(1, (int)MathF.Round(region.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(region.Height * scale));
        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        using var image = SKImage.FromBitmap(source);
        var sampling = scale >= 1 ? new SKSamplingOptions(SKFilterMode.Nearest) : new SKSamplingOptions(SKCubicResampler.Mitchell);
        canvas.DrawImage(image, SKRect.Create(region.Left, region.Top, region.Width, region.Height), SKRect.Create(0, 0, width, height), sampling);
        return target;
    }

    public static byte[] EncodePng(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
