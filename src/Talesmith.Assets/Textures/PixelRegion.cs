using System.Security.Cryptography;
using Talesmith.Imaging;

namespace Talesmith.Assets.Textures;

/// <summary>A rectangle of whole pixels inside an image.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Intersects(PixelRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public bool Contains(PixelRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>The part of this rectangle inside <paramref name="bounds"/>; empty when they do not overlap.</summary>
    public PixelRect Clip(PixelRect bounds)
    {
        var left = Math.Max(X, bounds.X);
        var top = Math.Max(Y, bounds.Y);
        var right = Math.Min(Right, bounds.Right);
        var bottom = Math.Min(Bottom, bounds.Bottom);
        return right <= left || bottom <= top ? default : new PixelRect(left, top, right - left, bottom - top);
    }
}

/// <summary>Pixel operations on regions of <see cref="ImageData"/>, for slicing and atlas packing.</summary>
internal static class PixelRegion
{
    public static bool IsTransparent(ImageData image, PixelRect rect)
    {
        for (var y = rect.Y; y < rect.Bottom; y++)
        {
            var row = image.Pixels.AsSpan(y * image.Stride + rect.X * 4, rect.Width * 4);
            for (var i = 3; i < row.Length; i += 4)
            {
                if (row[i] != 0)
                    return false;
            }
        }

        return true;
    }

    /// <summary>The smallest rectangle inside <paramref name="rect"/> holding every pixel that is not fully transparent; empty when all are.</summary>
    public static PixelRect FindOpaqueBounds(ImageData image, PixelRect rect)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (var y = rect.Y; y < rect.Bottom; y++)
        {
            var row = image.Pixels.AsSpan(y * image.Stride + rect.X * 4, rect.Width * 4);
            for (var x = 0; x < rect.Width; x++)
            {
                if (row[x * 4 + 3] == 0)
                    continue;
                left = Math.Min(left, rect.X + x);
                right = Math.Max(right, rect.X + x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return right < left ? default : new PixelRect(left, top, right - left, bottom - top);
    }

    public static void Copy(ImageData source, PixelRect rect, byte[] target, int targetStride, int x, int y)
    {
        for (var row = 0; row < rect.Height; row++)
        {
            source.Pixels.AsSpan((rect.Y + row) * source.Stride + rect.X * 4, rect.Width * 4)
                .CopyTo(target.AsSpan((y + row) * targetStride + x * 4));
        }
    }

    /// <summary>A hash of the size and pixels of a region, for finding identical images.</summary>
    public static string Hash(ImageData image, PixelRect rect)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[8];
        BitConverter.TryWriteBytes(size, rect.Width);
        BitConverter.TryWriteBytes(size[4..], rect.Height);
        hash.AppendData(size);
        for (var y = rect.Y; y < rect.Bottom; y++)
            hash.AppendData(image.Pixels.AsSpan(y * image.Stride + rect.X * 4, rect.Width * 4));
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
