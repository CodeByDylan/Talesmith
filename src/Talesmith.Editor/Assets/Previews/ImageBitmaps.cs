using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Talesmith.Imaging;

namespace Talesmith.Editor.Assets.Previews;

/// <summary>Shows engine images in Avalonia.</summary>
public static class ImageBitmaps
{
    /// <summary>Copies premultiplied RGBA pixels into a bitmap Avalonia can draw.</summary>
    public static WriteableBitmap ToBitmap(ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Pixels, y * image.Width * 4, buffer.Address + y * buffer.RowBytes, image.Width * 4);
        return bitmap;
    }
}
