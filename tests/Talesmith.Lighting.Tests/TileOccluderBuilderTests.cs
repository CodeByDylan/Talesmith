using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Grids;
using Talesmith.Imaging;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Lighting.Tests;

public sealed class TileOccluderBuilderTests
{
    private const int Size = 32;

    [Fact]
    public void AWallBecomesOneRectangle()
    {
        var (map, layer) = Map(4);
        for (var x = 2; x <= 6; x++)
            layer.SetCell(new GridCoord(x, 3), new TileCell(1, 0));

        var (edges, bounds) = new TileOccluderBuilder().Build(map, layer, layer.Chunks[new ChunkCoord(0, 0)], Vector2.Zero);

        Assert.Equal(4, edges.Length);
        Assert.Equal(2 * Size - Size / 2, bounds.X);
        Assert.Equal(5 * Size, bounds.Width);
        Assert.Equal(Size, bounds.Height);
        AssertClockwise(edges);
    }

    [Fact]
    public void ChunksAreOutlinedOnTheirOwn()
    {
        var (map, layer) = Map(2);
        for (var x = 0; x < 8; x++)
            layer.SetCell(new GridCoord(x, 1), new TileCell(1, 0));

        var builder = new TileOccluderBuilder();
        var (first, _) = builder.Build(map, layer, layer.Chunks[new ChunkCoord(0, 0)], Vector2.Zero);
        var (second, _) = builder.Build(map, layer, layer.Chunks[new ChunkCoord(1, 0)], Vector2.Zero);

        // Each chunk's part of the wall is a closed rectangle, so both run along the border between them.
        Assert.Equal(4, first.Length);
        Assert.Equal(4, second.Length);
        Assert.Contains(first, e => e.Start.X == e.End.X && e.Start.X == 3.5f * Size);
        Assert.Contains(second, e => e.Start.X == e.End.X && e.Start.X == 3.5f * Size);
        AssertClosed(first);
        AssertClosed(second);
    }

    [Fact]
    public void TilesWithCollisionShapesUseThemTurnedLikeTheirArt()
    {
        var shape = new[] { new Vector2(-16, 0), new Vector2(16, 0), new Vector2(16, 16), new Vector2(-16, 16) };
        var (map, layer) = Map(4, new TileInfo(0, "half", null, [], PropertySet.Empty) { Collision = [shape] });
        layer.SetCell(new GridCoord(1, 1), new TileCell(1, 0, rotation: 2));

        var (edges, bounds) = new TileOccluderBuilder().Build(map, layer, layer.Chunks[new ChunkCoord(0, 0)], new Vector2(100, 0));

        Assert.Equal(4, edges.Length);
        Assert.Equal(100 + Size - 16, bounds.X, 3);
        Assert.Equal(Size - 16, bounds.Y, 3);
        Assert.Equal(16, bounds.Height, 3);
    }

    [Fact]
    public void TilesBlockLightOnlyWithTheOpaquePixelsOfTheirArtwork()
    {
        // A plank: only the top quarter of the tile is drawn.
        var (map, layer) = Map(4, art: Art(Size, Size, (_, y) => y < Size / 4));
        layer.SetCell(new GridCoord(1, 1), new TileCell(1, 0));

        var (edges, bounds) = Build(map, layer);

        Assert.Equal(4, edges.Length);
        Assert.Equal(Size / 2, bounds.Y);
        Assert.Equal(Size / 4, bounds.Height);
        Assert.Equal(Size, bounds.Width);
        AssertClockwise(edges);
    }

    [Fact]
    public void PlanksSideBySideMergeIntoOneStrip()
    {
        var (map, layer) = Map(4, art: Art(Size, Size, (_, y) => y < Size / 4));
        for (var x = 2; x <= 4; x++)
            layer.SetCell(new GridCoord(x, 3), new TileCell(1, 0));

        var (edges, bounds) = Build(map, layer);

        Assert.Equal(4, edges.Length);
        Assert.Equal(3 * Size, bounds.Width);
        Assert.Equal(Size / 4, bounds.Height);
    }

    [Fact]
    public void TilesWhoseArtworkFillsTheirCellBlockTheWholeCell()
    {
        var (map, layer) = Map(4, art: Art(Size, Size, (_, _) => true));
        for (var x = 2; x <= 6; x++)
            layer.SetCell(new GridCoord(x, 3), new TileCell(1, 0));

        var (edges, bounds) = Build(map, layer);

        Assert.Equal(4, edges.Length);
        Assert.Equal(5 * Size, bounds.Width);
        Assert.Equal(Size, bounds.Height);
    }

    [Fact]
    public void BlocksWithATransparentRimSealAgainstTheirNeighbors()
    {
        // Stones drawn with one transparent pixel all around: light must not leak through the seams between them.
        var (map, layer) = Map(4, art: Art(Size, Size, (x, y) => x is >= 1 and <= Size - 2 && y is >= 1 and <= Size - 2));
        for (var x = 2; x <= 3; x++)
            layer.SetCell(new GridCoord(x, 3), new TileCell(1, 0));

        var (edges, bounds) = Build(map, layer);

        Assert.Equal(4, edges.Length);
        Assert.Equal(2 * Size, bounds.Width);
        Assert.Equal(Size, bounds.Height);
    }

    [Fact]
    public void RoundedBlocksSideBySideShareTheirStraightSides()
    {
        // Crates with corners cut off: only small notches stay between them, and no edge along the seam.
        var (map, layer) = Map(4, art: Art(Size, Size, (x, y) =>
            Math.Min(x, Size - 1 - x) + Math.Min(y, Size - 1 - y) >= 3));
        for (var x = 2; x <= 3; x++)
            layer.SetCell(new GridCoord(x, 3), new TileCell(1, 0));

        var (edges, bounds) = Build(map, layer);

        var seam = 2.5f * Size;
        Assert.DoesNotContain(edges, e => e.Start.X == seam && e.End.X == seam);
        Assert.Equal(2 * Size, bounds.Width);
        AssertClosed(edges);
        AssertClockwise(edges);
    }

    [Fact]
    public void TransparentTilesBlockNothing()
    {
        var (map, layer) = Map(4, art: Art(Size, Size, (_, _) => false));
        layer.SetCell(new GridCoord(1, 1), new TileCell(1, 0));

        Assert.Empty(Build(map, layer).Edges);
    }

    [Fact]
    public void ArtworkAboveTheCellDoesNotBlockLight()
    {
        // Ground standing on its cell with grass tufts in the 16 rows above it, as tiles taller than their cell are drawn.
        var (map, layer) = Map(4, art: Art(Size, Size + 16, (x, y) => y >= 16 || x % 4 == 0), tileHeight: Size + 16);
        layer.SetCell(new GridCoord(1, 1), new TileCell(1, 0));

        var (edges, bounds) = Build(map, layer);

        Assert.Equal(4, edges.Length);
        Assert.Equal(Size / 2, bounds.Y);
        Assert.Equal(Size, bounds.Height);
    }

    [Fact]
    public void FlippedTilesBlockLightWithTheirFlippedArtwork()
    {
        var (map, layer) = Map(4, art: Art(Size, Size, (x, _) => x < Size / 2));
        layer.SetCell(new GridCoord(1, 1), new TileCell(1, 0, flipX: true));

        var (edges, bounds) = Build(map, layer);

        Assert.Equal(4, edges.Length);
        Assert.Equal(Size, bounds.X);
        Assert.Equal(Size / 2, bounds.Width);
        AssertClockwise(edges);
    }

    private static (ShadowEdge[] Edges, Talesmith.Mathematics.Rect2 Bounds) Build(TileMap map, TileLayer layer) =>
        new TileOccluderBuilder().Build(map, layer, layer.Chunks[new ChunkCoord(0, 0)], Vector2.Zero);

    /// <summary>A tile image whose pixels are opaque where <paramref name="opaque"/> says so.</summary>
    private static TextureAsset Art(int width, int height, Func<int, int, bool> opaque)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                pixels[(y * width + x) * 4 + 3] = opaque(x, y) ? (byte)255 : (byte)0;
        }

        return new TextureAsset("tiles.png", new ImageData(width, height, pixels));
    }

    /// <summary>Asserts that every edge ends where another one starts, as closed outlines do.</summary>
    private static void AssertClosed(ShadowEdge[] edges) =>
        Assert.All(edges, edge => Assert.Contains(edges, other => Vector2.Distance(other.Start, edge.End) < 1e-3f));

    private static void AssertClockwise(ShadowEdge[] edges)
    {
        var center = Vector2.Zero;
        foreach (var edge in edges)
            center += edge.Start;
        center /= edges.Length;
        Assert.All(edges, edge => Assert.False(edge.Faces(center)));
    }

    private static (TileMap Map, TileLayer Layer) Map(int chunkShift, TileInfo? tile = null, TextureAsset? art = null, int tileHeight = Size)
    {
        var tiles = tile is null ? new Dictionary<int, TileInfo>() : new Dictionary<int, TileInfo> { [tile.Id] = tile };
        var tileset = new Tileset(1, "walls", Size, tileHeight, art, 0, 0, 1, 1, tiles);
        var layer = new TileLayer("Walls", chunkShift) { Role = LayerRole.Collision };
        return (new TileMap("test", new SquareLayout(Size, Size), chunkShift, [tileset], [layer], PropertySet.Empty), layer);
    }
}
