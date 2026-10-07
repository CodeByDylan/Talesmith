using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Tests.Maps;

public sealed class TileMapTests
{
    [Theory]
    [InlineData(GridKind.HexPointyTop, typeof(HexLayout))]
    [InlineData(GridKind.HexFlatTop, typeof(HexLayout))]
    [InlineData(GridKind.Square, typeof(SquareLayout))]
    public void NewMapsHaveTheirGridAndOneGroundLayer(GridKind kind, Type layoutType)
    {
        var map = TileMap.Create(kind, 64, 64);

        Assert.IsType(layoutType, map.Layout);
        Assert.Equal(kind, map.Layout.Kind);
        Assert.Equal(TileMap.DefaultChunkShift, map.ChunkShift);
        var layer = Assert.IsType<TileLayer>(Assert.Single(map.Layers));
        Assert.Equal(("Ground", LayerRole.Ground), (layer.Name, layer.Role));
        Assert.Same(map, layer.Map);
    }

    [Fact]
    public void LayersCanBeAddedMovedAndRemoved()
    {
        var map = TestMaps.Create();
        var changes = TestMaps.Record(map);
        var objects = new ObjectLayer("Objects");
        var details = map.CreateTileLayer("Details", LayerRole.Decoration);

        map.AddLayer(objects);
        map.InsertLayer(1, details);
        map.MoveLayer(0, 2);
        var removed = map.RemoveLayer(details);

        Assert.Equal(0, removed);
        Assert.Equal(["Objects", "Ground"], map.Layers.Select(l => l.Name));
        Assert.Equal([map.Ground()], map.TileLayers);
        Assert.Equal([objects], map.ObjectLayers);
        Assert.Null(details.Map);
        Assert.Equal(
            [MapChangeKind.LayerAdded, MapChangeKind.LayerAdded, MapChangeKind.LayerMoved, MapChangeKind.LayerRemoved],
            changes.Select(c => c.Kind));
        Assert.Equal((2, 0), (changes[2].Index, changes[2].PreviousIndex));
    }

    [Fact]
    public void ALayerBelongsToOneMapAndSharesItsChunkSize()
    {
        var map = TestMaps.Create();

        Assert.Throws<InvalidOperationException>(() => TestMaps.Create().AddLayer(map.Ground()));
        Assert.Throws<InvalidOperationException>(() => map.AddLayer(new TileLayer("Big chunks", 6)));
    }

    [Fact]
    public void ChangingALayerRaisesOneChangeOnlyWhenTheValueDiffers()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        var changes = TestMaps.Record(map);
        var version = map.Version;

        layer.Name = "Terrain";
        layer.Name = "Terrain";
        layer.Role = LayerRole.Collision;
        layer.Opacity = -3;
        layer.IsLocked = true;
        layer.Properties = layer.Properties.With("speed", PropertyValue.FromFloat(1.5f));

        Assert.Equal(5, changes.Count);
        Assert.All(changes, c => Assert.Equal((MapChangeKind.LayerChanged, layer), (c.Kind, c.Layer)));
        Assert.Equal(0, layer.Opacity);
        Assert.Equal(version + 5, map.Version);
    }

    [Fact]
    public void SettingACellNotifiesItsBoundsAndBumpsOnlyItsChunk()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        layer.SetCell(new GridCoord(0, 0), TestMaps.Tile(1));
        layer.SetCell(new GridCoord(20, 0), TestMaps.Tile(1));
        var changes = TestMaps.Record(map);
        var other = layer.Chunks[new ChunkCoord(2, 0)].Version;

        layer.SetCell(new GridCoord(3, 4), TestMaps.Tile(2));
        layer.SetCell(new GridCoord(3, 4), TestMaps.Tile(2));

        var change = Assert.Single(changes);
        Assert.Equal((MapChangeKind.Cells, layer, GridBounds.Of(new GridCoord(3, 4))), (change.Kind, change.Layer, change.Cells));
        Assert.Equal(2, layer.Chunks[new ChunkCoord(0, 0)].Version);
        Assert.Equal(other, layer.Chunks[new ChunkCoord(2, 0)].Version);
    }

    [Fact]
    public void TilesetsGetFreeIdsAndBumpTheTilesetVersion()
    {
        var map = TestMaps.Create();
        var version = map.TilesetVersion;
        var second = Tileset.FromColors("More", [("Red", new Color(255, 0, 0))]);
        var clash = new Tileset(1, "Clash", 32, 32, null, 0, 0, 1, 1, new Dictionary<int, TileInfo>());

        map.AddTileset(second);
        map.AddTileset(clash);
        second.SetTile(0, TileInfo.Blank(0) with { Name = "Crimson" });

        Assert.Equal([1, 2, 3], map.Tilesets.Select(t => t.Id));
        Assert.Same(clash, map.FindTileset(3));
        Assert.Equal("Crimson", map.FindTileset(2)!.Find(0)!.Name);
        Assert.Equal(version + 3, map.TilesetVersion);
    }

    [Fact]
    public void TileDataWithoutContentIsDropped()
    {
        var tileset = TestMaps.Create().Tilesets[0];

        var previous = tileset.SetTile(3, TileInfo.Blank(3));

        Assert.Equal("Color 3", previous!.Name);
        Assert.Null(tileset.Find(3));
        Assert.Throws<ArgumentException>(() => tileset.SetTile(4, TileInfo.Blank(5)));
    }

    [Fact]
    public void AnimationsAreTrackedAsTilesChange()
    {
        var tileset = TestMaps.Create().Tilesets[0];

        tileset.SetTile(0, TileInfo.Blank(0) with { Animation = [new TileFrame(0, 100), new TileFrame(1, 100)] });

        Assert.True(tileset.HasAnimations);
        tileset.SetTile(0, null);
        Assert.False(tileset.HasAnimations);
    }

    [Fact]
    public void ObjectIdsStayUniqueAcrossLayers()
    {
        var map = TestMaps.Create();
        var layer = new ObjectLayer("Objects", [Point(41)]);

        map.AddLayer(layer);
        var next = map.AllocateObjectId();
        layer.Add(Point(90));

        Assert.Equal(42, next);
        Assert.Equal(91, map.NextObjectId);
    }

    [Fact]
    public void ObjectsAreReplacedByIdAndKeepTheirPlace()
    {
        var map = TestMaps.Create();
        var layer = new ObjectLayer("Objects", [Point(1), Point(2), Point(3)]);
        map.AddLayer(layer);
        var changes = TestMaps.Record(map);

        var previous = layer.Replace(Point(2) with { Name = "Moved" });
        layer.Move(0, 2);

        Assert.Equal(string.Empty, previous.Name);
        Assert.Equal([2, 3, 1], layer.Objects.Select(o => o.Id));
        Assert.Equal("Moved", layer.Find(2)!.Name);
        Assert.All(changes, c => Assert.Equal(MapChangeKind.Objects, c.Kind));
        Assert.Throws<ArgumentException>(() => layer.Replace(Point(9)));
    }

    [Fact]
    public void TerrainsGetIdsAndAreFoundByTheirTiles()
    {
        var map = TestMaps.Create();
        var grass = new Terrain("Grass", 1, 4, [new AutoTileRule("++++++", [new WeightedTile(5)])]);
        var sand = new Terrain("Sand", 1, 8, [], id: 7);

        map.AddTerrain(grass);
        map.AddTerrain(sand);
        var renamed = map.ReplaceTerrain(grass.WithName("Meadow"));

        Assert.Equal([1, 7], map.Terrains.Select(t => t.Id));
        Assert.Same(grass, renamed);
        Assert.Equal("Meadow", map.FindTerrain(TestMaps.Tile(5, rotation: 2))!.Name);
        Assert.Same(sand, map.FindTerrain(TestMaps.Tile(8)));
        Assert.Null(map.FindTerrain(TestMaps.Tile(6)));
    }

    [Fact]
    public void UniqueLayerNamesCountUp()
    {
        var map = TestMaps.Create();
        map.AddLayer(map.CreateTileLayer("Layer"));
        map.AddLayer(map.CreateTileLayer("Layer 2"));

        Assert.Equal("Layer 3", map.UniqueLayerName("Layer"));
        Assert.Equal("Fog", map.UniqueLayerName("Fog"));
    }

    [Fact]
    public void ClonedTileLayersCopyCellsAndSettings()
    {
        var map = TestMaps.Create();
        var layer = map.Ground();
        layer.SetCell(new GridCoord(-3, 9), TestMaps.Tile(4));
        layer.Opacity = 0.5f;

        var copy = layer.Clone("Copy");
        copy.SetCell(new GridCoord(-3, 9), TestMaps.Tile(5));

        Assert.NotEqual(layer.Id, copy.Id);
        Assert.Equal(0.5f, copy.Opacity);
        Assert.Equal(TestMaps.Tile(4), layer.GetCell(new GridCoord(-3, 9)));
        Assert.Equal(TestMaps.Tile(5), copy.GetCell(new GridCoord(-3, 9)));
    }

    private static MapObject Point(int id) =>
        new(id, string.Empty, "spawn", MapObjectShape.Point, default, GridCoord.Zero, [], TileCell.Empty, PropertySet.Empty);
}
