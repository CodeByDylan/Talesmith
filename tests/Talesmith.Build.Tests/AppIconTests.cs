using SkiaSharp;
using Talesmith.Build.Packaging;

namespace Talesmith.Build.Tests;

public sealed class AppIconTests
{
    [Fact]
    public void WindowsIconsHoldEverySizeAsPng()
    {
        var ico = AppIcons.ToIco(Png(40));

        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));
        var count = BitConverter.ToUInt16(ico, 4);
        Assert.Equal(7, count);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (i * 16);
            var size = ico[entry] == 0 ? 256 : ico[entry];
            var length = BitConverter.ToInt32(ico, entry + 8);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            using var image = SKBitmap.Decode(ico.AsSpan(offset, length));
            Assert.Equal(size, image.Width);
        }
    }

    private static byte[] Png(int size)
    {
        using var bitmap = new SKBitmap(size, size);
        bitmap.Erase(SKColors.Coral);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
