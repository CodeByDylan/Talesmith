using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets.Maps;
using Talesmith.Grids;

namespace Talesmith.Assets.Hexy.Tests;

public sealed class HexyMapImporterTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("talesmith-hexy-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task OrthogonalMapsLoadWithASquareLayout()
    {
        var map = await LoadAsync(Map("orthogonal", 64, 48));

        var layout = Assert.IsType<SquareLayout>(map.Layout);
        Assert.Equal(GridKind.Square, layout.Kind);
        Assert.Equal(new Vector2(64, 48), layout.CellSize);
        Assert.Equal(90, map.RotationStepDegrees);
    }

    [Theory]
    [InlineData("pointy", GridKind.HexPointyTop)]
    [InlineData("flat", GridKind.HexFlatTop)]
    public async Task HexMapsStillLoadWithAHexLayout(string orientation, GridKind kind)
    {
        var map = await LoadAsync(Map(orientation, 128, 148));

        Assert.IsType<HexLayout>(map.Layout);
        Assert.Equal(kind, map.Layout.Kind);
        Assert.Equal(60, map.RotationStepDegrees);
    }

    [Fact]
    public async Task OrthogonalTileCellsKeepTheirQuarterTurns()
    {
        var cells = new TileCell[4 * 4];
        cells[1 * 4 + 2] = new TileCell(1, 7, rotation: 3, flipX: true);
        var chunk = Convert.ToBase64String(ChunkCodec.Encode(cells));
        var map = await LoadAsync(Map("orthogonal", 64, 64, chunkSize: 4,
            layers: $$"""[{ "type": "tiles", "name": "Ground", "encoding": "base64-zlib-u32le", "chunks": [{ "q": 0, "r": 0, "data": "{{chunk}}" }] }]"""));

        var tile = map.TileLayers[0].GetCell(new GridCoord(2, 1));
        Assert.Equal(7, tile.TileId);
        Assert.Equal(3, tile.Rotation);
        Assert.True(tile.FlipX);
    }

    [Fact]
    public async Task ObjectsOnOrthogonalMapsUseColumnsAndRowsAndQuarterTurns()
    {
        var map = await LoadAsync(Map("orthogonal", 64, 48, layers: """
            [{ "type": "objects", "name": "Objects", "objects": [
              { "id": 1, "name": "Crate", "shape": "tile", "q": 3, "r": 2, "offset": [0.5, -0.5], "tile": { "tileset": 1, "id": 0, "rotation": 5 } }
            ] }]
            """));

        var crate = Assert.Single(map.ObjectLayers[0].Objects);
        Assert.Equal(new GridCoord(3, 2), crate.Cell);
        Assert.Equal(new Vector2(3.5f * 64, 1.5f * 48), crate.Position);
        Assert.Equal(1, crate.Tile.Rotation);
    }

    [Fact]
    public async Task UnknownOrientationsAreRejected()
    {
        var error = await Assert.ThrowsAsync<AssetException>(() => LoadAsync(Map("isometric", 64, 32)));

        Assert.Contains("'orthogonal'", error.Message, StringComparison.Ordinal);
    }

    private static string Map(string orientation, int width, int height, int chunkSize = 32, string layers = "[]") => $$"""
        {
          "format": "hexy",
          "version": 2,
          "orientation": "{{orientation}}",
          "hexWidth": {{width}},
          "hexHeight": {{height}},
          "chunkSize": {{chunkSize}},
          "tilesets": [{ "id": 1, "name": "Colors", "tileWidth": {{width}}, "tileHeight": {{height}}, "columns": 1, "tileCount": 8 }],
          "layers": {{layers}}
        }
        """;

    private async Task<TileMap> LoadAsync(string json)
    {
        var name = $"map-{Guid.NewGuid():N}.hexy";
        await File.WriteAllTextAsync(Path.Combine(_folder, name), json);
        var assets = new AssetManager(new FileSystemAssetSource(_folder), [new HexyMapImporter()], NullLogger<AssetManager>.Instance);
        return await assets.LoadAsync<TileMap>(name);
    }
}
