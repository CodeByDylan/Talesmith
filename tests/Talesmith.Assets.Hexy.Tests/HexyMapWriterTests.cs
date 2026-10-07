using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SkiaSharp;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Assets.Textures;
using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Hexy.Tests;

public sealed class HexyMapWriterTests : IDisposable
{
    private readonly HexyTestFiles _files = new();

    public void Dispose() => _files.Dispose();

    public static TheoryData<GridKind> Kinds => [GridKind.HexPointyTop, GridKind.HexFlatTop, GridKind.Square];

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task NewMapsOfEveryGridReadBackTheSame(GridKind kind)
    {
        var map = CreateMap(kind);

        var reread = await _files.RoundTripAsync(map);

        MapAssert.Equivalent(map, reread);
        Assert.Equal(new Vector2(64, 32), new Vector2(reread.Tilesets[1].Texture!.Width, reread.Tilesets[1].Texture!.Height));
    }

    [Fact]
    public async Task NewMapsAreWrittenTheWayHexyWritesThem()
    {
        var map = TileMap.Create(GridKind.HexFlatTop, 148, 128);
        map.AddTileset(Tileset.FromColors("Land", [("Grass", new Color(0x4A, 0xDE, 0x80))]));
        map.Ground().SetCell(GridCoord.Zero, new TileCell(1, 0));
        var objects = new ObjectLayer("Objects") { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        objects.Add(new MapObject(map.AllocateObjectId(), "Start", "spawn", MapObjectShape.Point, map.Layout.CellToWorld(new GridCoord(1, 2)),
            new GridCoord(1, 2), [], TileCell.Empty, PropertySet.Empty));
        map.AddLayer(objects);
        var groundId = map.Ground().Id;

        var json = Json(await HexyTestFiles.WriteAsync(map));

        var chunk = Convert.ToBase64String(ChunkCodec.Encode([new TileCell(1, 0), .. new TileCell[(1 << TileMap.DefaultChunkShift << TileMap.DefaultChunkShift) - 1]]))
            .Replace("+", "\\u002B", StringComparison.Ordinal);
        Assert.Equal($$"""
            {
              "format": "hexy",
              "version": 2,
              "orientation": "flat",
              "hexWidth": 148,
              "hexHeight": 128,
              "chunkSize": 32,
              "nextObjectId": 2,
              "tilesets": [
                {
                  "id": 1,
                  "name": "Land",
                  "tileWidth": 64,
                  "tileHeight": 64,
                  "margin": 0,
                  "spacing": 0,
                  "columns": 1,
                  "tileCount": 1,
                  "tiles": [
                    {
                      "id": 0,
                      "name": "Grass",
                      "color": "#4ADE80"
                    }
                  ]
                }
              ],
              "layers": [
                {
                  "type": "tiles",
                  "encoding": "base64-zlib-u32le",
                  "chunks": [
                    {
                      "q": 0,
                      "r": 0,
                      "data": "{{chunk}}"
                    }
                  ],
                  "id": "{{groundId}}",
                  "name": "Ground",
                  "visible": true,
                  "locked": false,
                  "opacity": 1
                },
                {
                  "type": "objects",
                  "color": "#F59E0B",
                  "objects": [
                    {
                      "id": 1,
                      "name": "Start",
                      "type": "spawn",
                      "shape": "point",
                      "q": 1,
                      "r": 2
                    }
                  ],
                  "id": "00000000-0000-0000-0000-000000000002",
                  "name": "Objects",
                  "visible": true,
                  "locked": false,
                  "opacity": 1
                }
              ]
            }
            """.ReplaceLineEndings(Environment.NewLine), json);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task MapsAreWrittenBackWithTheLineEndingsTheyWereSavedWith(string newLine)
    {
        var map = TileMap.Create(GridKind.HexFlatTop, 148, 128);
        map.AddTileset(Tileset.FromColors("Land", [("Grass", new Color(0x4A, 0xDE, 0x80))]));
        map.Ground().SetCell(GridCoord.Zero, new TileCell(1, 0));
        var saved = Json(await HexyTestFiles.WriteAsync(map)).ReplaceLineEndings(newLine);
        var loaded = await _files.LoadJsonAsync(saved);

        var json = Json(await HexyTestFiles.WriteAsync(loaded));

        Assert.Equal(saved, json);
    }

    [Fact]
    public async Task RolesAndCollisionShapesAreStoredAsPropertiesHexyKeeps()
    {
        var map = TestMap(GridKind.Square);
        map.Ground().Role = LayerRole.Collision;
        var named = map.CreateTileLayer("Collision", LayerRole.Collision);
        map.AddLayer(named);
        Vector2[] triangle = [new(-32, 32), new(32, 32), new(0, -32.5f)];
        map.Tilesets[0].SetTile(2, TileInfo.Blank(2) with { Collision = [triangle, triangle] });

        var package = await HexyTestFiles.WriteAsync(map);
        var reread = await _files.RoundTripAsync(map);

        using var json = JsonDocument.Parse(HexyTestFiles.ReadEntry(package, "map.json"));
        var layers = json.RootElement.GetProperty("layers");
        Assert.Equal("""[{"name":"role","type":"string","value":"collision"}]""", Compact(layers[0].GetProperty("properties")));
        Assert.False(layers[1].TryGetProperty("properties", out _));
        var tile = json.RootElement.GetProperty("tilesets")[0].GetProperty("tiles")[2];
        Assert.Equal("""[{"name":"collision","type":"string","value":"-32,32 32,32 0,-32.5; -32,32 32,32 0,-32.5"}]""", Compact(tile.GetProperty("properties")));

        Assert.Equal([LayerRole.Collision, LayerRole.Collision], reread.TileLayers.Select(l => l.Role));
        Assert.Equal(0, reread.TileLayers[0].Properties.Count);
        var collision = reread.Tilesets[0].Find(2)!.Collision;
        Assert.Equal(2, collision.Count);
        Assert.Equal(triangle, collision[1]);
        Assert.Equal(0, reread.Tilesets[0].Find(2)!.Properties.Count);
    }

    [Fact]
    public async Task StoredRolesAndShapesAreWrittenBackWhereTheyWere()
    {
        var map = await _files.LoadJsonAsync(Document(
            tiles: """[{ "id": 0, "properties": [{ "name": "a", "type": "int", "value": "1" }, { "name": "collision", "type": "string", "value": "0,0 10,0 0,10" }, { "name": "b", "type": "bool", "value": "true" }] }]""",
            layers: """[{ "type": "tiles", "name": "Ground", "encoding": "base64-zlib-u32le", "chunks": [], "properties": [{ "name": "role", "type": "string", "value": "Navigation" }, { "name": "cost", "type": "float", "value": "2" }] }]"""));

        Assert.Equal(LayerRole.Navigation, map.Ground().Role);
        Assert.Equal(["a", "b"], map.Tilesets[0].Find(0)!.Properties.Values.Keys);
        var json = Json(await HexyTestFiles.WriteAsync(map));

        Assert.Contains("\"name\": \"a\"", json, StringComparison.Ordinal);
        Assert.True(json.IndexOf("\"a\"", StringComparison.Ordinal) < json.IndexOf("\"collision\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"collision\"", StringComparison.Ordinal) < json.IndexOf("\"b\"", StringComparison.Ordinal));
        Assert.Contains("\"value\": \"Navigation\"", json, StringComparison.Ordinal);
        Assert.True(json.IndexOf("\"role\"", StringComparison.Ordinal) < json.IndexOf("\"cost\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PropertiesThatDoNotParseStayOrdinaryProperties()
    {
        var map = await _files.LoadJsonAsync(Document(
            tiles: """[{ "id": 0, "properties": [{ "name": "collision", "type": "bool", "value": "true" }] }]""",
            layers: """[{ "type": "tiles", "name": "Walls", "encoding": "base64-zlib-u32le", "properties": [{ "name": "role", "type": "string", "value": "lava" }] }]"""));

        Assert.True(map.Tilesets[0].Find(0)!.Properties.GetBool("collision"));
        Assert.Empty(map.Tilesets[0].Find(0)!.Collision);
        Assert.Equal("lava", map.Ground().Properties.GetString("role"));
        Assert.Equal(LayerRole.Collision, map.Ground().Role);
    }

    [Fact]
    public async Task FieldsTalesmithDoesNotKnowAreKept()
    {
        var map = await _files.LoadJsonAsync(Document(
            extra: """ "editorCamera": { "x": 10, "zoom": 2.5 }, "notes": ["a", "b"], """,
            tilesetExtra: """ "license": "CC0", """,
            tiles: """[{ "id": 0, "name": "Grass", "sound": "step.ogg" }]""",
            terrains: """[{ "id": 1, "name": "Grass", "tileset": 1, "baseTile": 0, "rules": [], "tint": "#FF0000" }]""",
            layers: """
                [{ "type": "objects", "name": "Objects", "parallax": [0.5, 0.25], "objects": [{ "id": 1, "shape": "point", "q": 0, "r": 0, "rotation": 45 }] },
                 { "type": "mystery", "name": "Unknown" }]
                """));

        var json = Json(await HexyTestFiles.WriteAsync(map));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("""{"x":10,"zoom":2.5}""", Compact(root.GetProperty("editorCamera")));
        Assert.Equal("""["a","b"]""", Compact(root.GetProperty("notes")));
        Assert.Equal("CC0", root.GetProperty("tilesets")[0].GetProperty("license").GetString());
        Assert.Equal("step.ogg", root.GetProperty("tilesets")[0].GetProperty("tiles")[0].GetProperty("sound").GetString());
        Assert.Equal("#FF0000", root.GetProperty("terrains")[0].GetProperty("tint").GetString());
        var layer = Assert.Single(root.GetProperty("layers").EnumerateArray());
        Assert.Equal("[0.5,0.25]", Compact(layer.GetProperty("parallax")));
        Assert.Equal(45, layer.GetProperty("objects")[0].GetProperty("rotation").GetInt32());
    }

    [Fact]
    public async Task ObjectsKeepTheirStoredPositionsUntilTheyMove()
    {
        var map = await _files.LoadJsonAsync(Document(layers: """
            [{ "type": "objects", "name": "Objects", "objects": [
              { "id": 1, "shape": "point", "q": 3, "r": -2, "offset": [0.13, 0.15] },
              { "id": 2, "shape": "polygon", "q": 0, "r": 0, "offset": [0.1, 0.2], "vertices": [[-1.5, 0], [0, -1.5], [1.5, 0.3]] }
            ] }]
            """));
        var layer = map.ObjectLayers[0];
        var moved = layer.Objects[1].MoveTo(layer.Objects[1].Position + map.Layout.CellToWorld(new Vector2(2, 0)), map.Layout);
        layer.Replace(moved);

        var root = JsonDocument.Parse(Json(await HexyTestFiles.WriteAsync(map))).RootElement;
        var objects = root.GetProperty("layers")[0].GetProperty("objects");

        Assert.Equal("[0.13,0.15]", Compact(objects[0].GetProperty("offset")));
        Assert.Equal((2, 0), (objects[1].GetProperty("q").GetInt32(), objects[1].GetProperty("r").GetInt32()));
        Assert.Equal("[0.1,0.2]", Compact(objects[1].GetProperty("offset")));
        Assert.Equal("[[-1.5,0],[0,-1.5],[1.5,0.3]]", Compact(objects[1].GetProperty("vertices")));
    }

    [Fact]
    public async Task EditedCellsAreWrittenAndEmptiedChunksDropped()
    {
        var map = TestMap(GridKind.HexPointyTop);
        var layer = map.Ground();
        layer.SetCell(new GridCoord(40, 40), new TileCell(1, 3));
        var edit = new TileEdit(map);
        edit.Fill(layer, [new GridCoord(0, 0), new GridCoord(-1, 5), new GridCoord(31, 31)], new TileCell(1, 2, 4, true));
        edit.Set(layer, new GridCoord(40, 40), TileCell.Empty);
        edit.Commit();

        var reread = await _files.RoundTripAsync(map);

        Assert.Equal(
            [new PlacedTile(new GridCoord(0, 0), new TileCell(1, 2, 4, true)), new PlacedTile(new GridCoord(-1, 5), new TileCell(1, 2, 4, true)), new PlacedTile(new GridCoord(31, 31), new TileCell(1, 2, 4, true))],
            Cells(reread.Ground()));
        Assert.Equal(2, reread.Ground().Chunks.Count);
    }

    [Fact]
    public async Task LayerOpacityAndCellSizesAreWrittenAsTheirShortestDecimals()
    {
        var map = TileMap.Create(GridKind.HexPointyTop, 110.85125f, 128);
        map.Ground().Opacity = 0.7f;

        var root = JsonDocument.Parse(Json(await HexyTestFiles.WriteAsync(map))).RootElement;

        Assert.Equal("110.85125", root.GetProperty("hexWidth").GetRawText());
        Assert.Equal("0.7", root.GetProperty("layers")[0].GetProperty("opacity").GetRawText());
    }

    [Fact]
    public async Task SavingReplacesTheFileAndLeavesNothingBehind()
    {
        var map = TestMap(GridKind.Square);
        var path = Path.Combine(_files.Folder, "level.hexy");
        await HexyMapWriter.SaveAsync(map, path, TestContext.Current.CancellationToken);
        map.Ground().SetCell(new GridCoord(2, 2), new TileCell(1, 1));

        await HexyMapWriter.SaveAsync(map, path, TestContext.Current.CancellationToken);
        var reread = await _files.LoadAsync("level.hexy");

        Assert.Equal(new TileCell(1, 1), reread.Ground().GetCell(new GridCoord(2, 2)));
        Assert.Equal(["level.hexy"], Directory.EnumerateFiles(_files.Folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AFailedSaveKeepsTheOriginalFile()
    {
        var map = TestMap(GridKind.Square);
        var path = Path.Combine(_files.Folder, "level.hexy");
        await HexyMapWriter.SaveAsync(map, path, TestContext.Current.CancellationToken);
        var original = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        map.Ground().SetCell(new GridCoord(2, 2), new TileCell(1, 1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HexyMapWriter.SaveAsync(map, path, new CancellationToken(canceled: true)));

        Assert.Equal(original, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["level.hexy"], Directory.EnumerateFiles(_files.Folder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task MapsSavedOverThemselvesKeepTheirImagesEvenAfterTilesetsAreRenamed()
    {
        var map = CreateMap(GridKind.Square);
        await HexyMapWriter.SaveAsync(map, Path.Combine(_files.Folder, "own.hexy"), TestContext.Current.CancellationToken);
        var loaded = await _files.LoadAsync("own.hexy");

        loaded.Tilesets[1].Name = "Renamed art";
        await HexyMapWriter.SaveAsync(loaded, Path.Combine(_files.Folder, "own.hexy"), TestContext.Current.CancellationToken);
        loaded.Tilesets[1].Name = "Renamed again";
        var result = await HexyMapWriter.SaveAsync(loaded, Path.Combine(_files.Folder, "own.hexy"), TestContext.Current.CancellationToken);
        var package = await File.ReadAllBytesAsync(Path.Combine(_files.Folder, "own.hexy"), TestContext.Current.CancellationToken);

        Assert.Empty(result.Warnings);
        Assert.Contains(HexyTestFiles.Entries(package), e => e.StartsWith("assets/renamed-again-", StringComparison.Ordinal));
        var reread = await HexyTestFiles.LoadAsync(_files.Folder, "own.hexy");
        Assert.Equal(map.Tilesets[1].Texture!.Image.Pixels, reread.Tilesets[1].Texture!.Image.Pixels);
    }

    [Fact]
    public async Task VersionOneMapsArePackagedWithTheirImages()
    {
        await File.WriteAllBytesAsync(Path.Combine(_files.Folder, "art.png"), Png(16, 8), TestContext.Current.CancellationToken);
        var map = await _files.LoadJsonAsync(Document(version: 1, image: "art.png"));

        var package = await HexyTestFiles.WriteAsync(map);
        var root = JsonDocument.Parse(HexyTestFiles.ReadEntry(package, "map.json")).RootElement;

        var tileset = root.GetProperty("tilesets")[0];
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.StartsWith("assets/colors-", tileset.GetProperty("image").GetString(), StringComparison.Ordinal);
        Assert.Equal("art.png", tileset.GetProperty("source").GetString());
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(_files.Folder, "art.png"), TestContext.Current.CancellationToken),
            HexyTestFiles.ReadEntry(package, tileset.GetProperty("image").GetString()!));
    }

    [Fact]
    public async Task MissingImagesAreReportedAndKeptAsReferences()
    {
        var map = await _files.LoadJsonAsync(Document(image: "nowhere/missing.png"));

        using var stream = new MemoryStream();
        var result = await HexyMapWriter.WriteAsync(map, stream, TestContext.Current.CancellationToken);

        Assert.Contains("missing.png", Assert.Single(result.Warnings), StringComparison.Ordinal);
        var root = JsonDocument.Parse(HexyTestFiles.ReadEntry(stream.ToArray(), "map.json")).RootElement;
        Assert.Equal("nowhere/missing.png", root.GetProperty("tilesets")[0].GetProperty("image").GetString());
        Assert.Equal(["map.json"], HexyTestFiles.Entries(stream.ToArray()));
    }

    [Fact]
    public async Task TerrainsArePersistedLikeHexy()
    {
        var map = TestMap(GridKind.HexFlatTop);
        map.AddTerrain(new Terrain("Grass", 1, 2,
        [
            new AutoTileRule("+-*---", [new WeightedTile(3, 2.5), new WeightedTile(4)], matchRotations: true),
            new AutoTileRule("******", [new WeightedTile(5)])
        ]));

        var root = JsonDocument.Parse(Json(await HexyTestFiles.WriteAsync(map))).RootElement;
        var reread = await _files.RoundTripAsync(map);

        Assert.Equal(
            """[{"id":1,"name":"Grass","tileset":1,"baseTile":2,"rules":[{"pattern":"+-*---","rotations":true,"tiles":[{"tile":3,"weight":2.5},{"tile":4,"weight":1}]},{"pattern":"******","rotations":false,"tiles":[{"tile":5,"weight":1}]}]}]""",
            Compact(root.GetProperty("terrains")));
        MapAssert.Equivalent(map, reread);
    }

    /// <summary>A map with every kind of content: two tile layers, objects of each shape, a color and an image tileset, a terrain and properties.</summary>
    private TileMap CreateMap(GridKind kind)
    {
        var map = TestMap(kind);
        var imagePath = Path.Combine(_files.Folder, $"art-{kind}.png");
        File.WriteAllBytes(imagePath, Png(64, 32));
        var texture = new TextureAsset(imagePath, TextureDecoder.Decode(new MemoryStream(Png(64, 32))));
        var art = Tileset.FromImage("Art & props", texture, TilesetImageFile.FromFile(imagePath), 16, 16, margin: 0, spacing: 0);
        map.AddTileset(art);
        art.SetTile(1, TileInfo.Blank(1) with
        {
            Name = "Torch",
            Animation = [new TileFrame(1, 120), new TileFrame(2, 80)],
            Properties = PropertySet.Empty.With("light", PropertyValue.FromColor(new Color(255, 200, 80)))
        });

        map.Properties = map.Properties.With("title", PropertyValue.FromString("Test map")).With("level", PropertyValue.FromInt(3));
        map.BackgroundColor = new Color(10, 20, 30);
        var ground = map.Ground();
        for (var i = -40; i < 40; i += 3)
            ground.SetCell(new GridCoord(i, i / 2), new TileCell(1, Math.Abs(i) % 8, Math.Abs(i) % map.Layout.RotationSteps, i % 2 == 0));
        var details = map.CreateTileLayer("Details", LayerRole.Decoration);
        details.SetCell(new GridCoord(3, 3), new TileCell(art.Id, 1));
        details.Opacity = 0.35f;
        details.IsLocked = true;
        map.AddLayer(details);

        var objects = new ObjectLayer("Things") { Color = new Color(1, 2, 3) };
        objects.Properties = objects.Properties.With("note", PropertyValue.FromString("hello"));
        map.AddLayer(objects);
        objects.Add(new MapObject(map.AllocateObjectId(), "Spawn", "spawn", MapObjectShape.Point, new Vector2(100, 37.5f), map.Layout.WorldToCell(new Vector2(100, 37.5f)),
            [], TileCell.Empty, PropertySet.Empty.With("team", PropertyValue.FromInt(2))));
        objects.Add(new MapObject(map.AllocateObjectId(), "Zone", "trigger", MapObjectShape.Polygon, map.Layout.CellToWorld(new GridCoord(-2, 1)), new GridCoord(-2, 1),
            [new Vector2(-40, -40), new Vector2(40, -40), new Vector2(0, 50)], TileCell.Empty, PropertySet.Empty));
        objects.Add(new MapObject(map.AllocateObjectId(), "Tree", "prop", MapObjectShape.Tile, map.Layout.CellToWorld(new GridCoord(5, -3)), new GridCoord(5, -3),
            [], new TileCell(art.Id, 3, 1, true), PropertySet.Empty));
        map.AddTerrain(new Terrain("Grass", 1, 4, [new AutoTileRule(new string('*', map.Layout.Topology.NeighborCount), [new WeightedTile(5)])]));
        return map;
    }

    private static TileMap TestMap(GridKind kind)
    {
        var map = TileMap.Create(kind, kind == GridKind.HexFlatTop ? 148 : 128, kind == GridKind.HexFlatTop ? 128 : 148);
        map.AddTileset(Tileset.FromColors("Colors", [.. Enumerable.Range(0, 8).Select(i => ($"Color {i}", new Color((byte)(i * 30), 100, 200)))]));
        return map;
    }

    private static string Document(int version = 2, string? image = null, string extra = "", string tilesetExtra = "", string tiles = "[]", string terrains = "[]",
        string layers = "[]") => $$"""
        {
          "format": "hexy",
          "version": {{version}},
          {{extra}}
          "orientation": "orthogonal",
          "hexWidth": 64,
          "hexHeight": 64,
          "chunkSize": 32,
          "tilesets": [{ {{tilesetExtra}} "id": 1, "name": "Colors", {{(image is null ? string.Empty : $"\"image\": \"{image}\",")}} "tileWidth": 16, "tileHeight": 16, "columns": 4, "tileCount": 8, "tiles": {{tiles}} }],
          "terrains": {{terrains}},
          "layers": {{layers}}
        }
        """;

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 8), 128));
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static readonly JsonSerializerOptions CompactOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string Json(byte[] package) => Encoding.UTF8.GetString(HexyTestFiles.ReadEntry(package, "map.json"));

    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element, CompactOptions);

    private static PlacedTile[] Cells(TileLayer layer)
    {
        var cells = new List<PlacedTile>();
        foreach (var placed in layer.Cells)
            cells.Add(placed);
        return [.. cells.OrderBy(c => c.Cell.Y).ThenBy(c => c.Cell.X)];
    }
}

internal static class MapExtensions
{
    public static TileLayer Ground(this TileMap map) => map.TileLayers[0];
}
