using Talesmith.Assets.Textures;
using Talesmith.Editor.Assets.SpriteEditor;
using Talesmith.Imaging;

namespace Talesmith.Editor.Tests.Assets;

public sealed class SpriteSlicingTests
{
    [Fact]
    public void GridCutsCellsInReadingOrderWithOffsetAndPadding()
    {
        var image = Opaque(36, 20);

        var cells = SpriteSlicing.Grid(image, new GridSliceOptions(10, 8) { OffsetX = 2, OffsetY = 1, PaddingX = 2, PaddingY = 3 });

        Assert.Equal(
            [new PixelRect(2, 1, 10, 8), new PixelRect(14, 1, 10, 8), new PixelRect(26, 1, 10, 8), new PixelRect(2, 12, 10, 8), new PixelRect(14, 12, 10, 8), new PixelRect(26, 12, 10, 8)],
            cells);
    }

    [Fact]
    public void GridByCountDividesTheImage()
    {
        var cells = SpriteSlicing.Grid(Opaque(64, 32), new GridSliceOptions(1, 1, Columns: 4, Rows: 2));

        Assert.Equal(8, cells.Count);
        Assert.All(cells, c => Assert.Equal((16, 16), (c.Width, c.Height)));
        Assert.Equal(new PixelRect(48, 16, 16, 16), cells[^1]);
    }

    [Fact]
    public void GridSkipsTransparentCellsOnlyWhenAsked()
    {
        var image = Transparent(32, 16);
        Paint(image, 20, 4, 2, 2);

        Assert.Equal([new PixelRect(16, 0, 16, 16)], SpriteSlicing.Grid(image, new GridSliceOptions(16, 16)));
        Assert.Equal(2, SpriteSlicing.Grid(image, new GridSliceOptions(16, 16) { SkipEmpty = false }).Count);
    }

    [Fact]
    public void AlphaIslandsFindTouchingPixelsInRows()
    {
        var image = Transparent(40, 30);
        Paint(image, 20, 2, 6, 5);
        Paint(image, 2, 3, 4, 4);
        Paint(image, 6, 7, 2, 2);
        Paint(image, 5, 20, 3, 3);
        Paint(image, 35, 25, 1, 1);

        var islands = SpriteSlicing.AlphaIslands(image);

        Assert.Equal([new PixelRect(2, 3, 6, 6), new PixelRect(20, 2, 6, 5), new PixelRect(5, 20, 3, 3)], islands);
    }

    [Fact]
    public void AlphaIslandsMergeWithinADistance()
    {
        var image = Transparent(30, 10);
        Paint(image, 2, 2, 4, 4);
        Paint(image, 8, 2, 3, 3);
        Paint(image, 24, 2, 3, 3);

        var islands = SpriteSlicing.AlphaIslands(image, mergeDistance: 3);

        Assert.Equal([new PixelRect(2, 2, 9, 4), new PixelRect(24, 2, 3, 3)], islands);
    }

    private static ImageData Opaque(int width, int height) => ImageData.Solid(width, height, new Mathematics.Color(255, 255, 255, 255));

    private static ImageData Transparent(int width, int height) => new(width, height, new byte[width * height * 4]);

    private static void Paint(ImageData image, int x, int y, int width, int height)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
                image.Pixels[(row * image.Width + column) * 4 + 3] = 255;
        }
    }
}
