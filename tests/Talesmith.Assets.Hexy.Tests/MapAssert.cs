using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Hexy.Tests;

/// <summary>Compares everything a map holds, so a map read back after writing can be checked against the original.</summary>
internal static class MapAssert
{
    public static void Equivalent(TileMap expected, TileMap actual)
    {
        Assert.Equal(expected.Layout.Kind, actual.Layout.Kind);
        Assert.Equal(expected.Layout.CellSize, actual.Layout.CellSize);
        Assert.Equal(expected.ChunkShift, actual.ChunkShift);
        Assert.Equal(expected.BackgroundColor, actual.BackgroundColor);
        Assert.Equal(expected.NextObjectId, actual.NextObjectId);
        Properties(expected.Properties, actual.Properties);

        Assert.Equal(expected.Tilesets.Count, actual.Tilesets.Count);
        for (var i = 0; i < expected.Tilesets.Count; i++)
            Tileset(expected.Tilesets[i], actual.Tilesets[i]);

        Assert.Equal(expected.Terrains.Count, actual.Terrains.Count);
        for (var i = 0; i < expected.Terrains.Count; i++)
            Terrain(expected.Terrains[i], actual.Terrains[i]);

        Assert.Equal(expected.Layers.Count, actual.Layers.Count);
        for (var i = 0; i < expected.Layers.Count; i++)
            Layer(expected.Layers[i], actual.Layers[i]);
    }

    public static void Properties(PropertySet expected, PropertySet actual) =>
        Assert.Equal(expected.Values.ToArray(), actual.Values.ToArray());

    private static void Tileset(Tileset expected, Tileset actual)
    {
        Assert.Equal(
            (expected.Id, expected.Name, expected.TileWidth, expected.TileHeight, expected.Margin, expected.Spacing, expected.Columns, expected.TileCount),
            (actual.Id, actual.Name, actual.TileWidth, actual.TileHeight, actual.Margin, actual.Spacing, actual.Columns, actual.TileCount));
        Assert.Equal(expected.IsColorTileset, actual.IsColorTileset);
        Assert.Equal(expected.ImageFile is null, actual.ImageFile is null);
        Assert.Equal(expected.ImageFile?.Source, actual.ImageFile?.Source);
        if (expected.Texture is not null)
            Assert.Equal(expected.Texture.Image.Pixels, actual.Texture!.Image.Pixels);

        Assert.Equal(expected.Tiles.Keys.Order(), actual.Tiles.Keys.Order());
        foreach (var (id, tile) in expected.Tiles)
        {
            var other = actual.Tiles[id];
            Assert.Equal((tile.Id, tile.Name, tile.Color), (other.Id, other.Name, other.Color));
            Assert.Equal(tile.Animation, other.Animation);
            Properties(tile.Properties, other.Properties);
            Assert.Equal(tile.Collision.Count, other.Collision.Count);
            for (var p = 0; p < tile.Collision.Count; p++)
                Assert.Equal(tile.Collision[p], other.Collision[p]);
        }
    }

    private static void Terrain(Terrain expected, Terrain actual)
    {
        Assert.Equal((expected.Id, expected.Name, expected.TilesetId, expected.BaseTileId), (actual.Id, actual.Name, actual.TilesetId, actual.BaseTileId));
        Assert.Equal(expected.Rules.Select(r => (r.Pattern, r.MatchRotations)), actual.Rules.Select(r => (r.Pattern, r.MatchRotations)));
        Assert.Equal(expected.Rules.SelectMany(r => r.Tiles), actual.Rules.SelectMany(r => r.Tiles));
    }

    private static void Layer(MapLayer expected, MapLayer actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(
            (expected.Id, expected.Name, expected.Role, expected.IsVisible, expected.IsLocked, expected.Opacity),
            (actual.Id, actual.Name, actual.Role, actual.IsVisible, actual.IsLocked, actual.Opacity));
        Properties(expected.Properties, actual.Properties);
        switch (expected)
        {
            case TileLayer tiles:
                Assert.Equal(Cells(tiles), Cells((TileLayer)actual));
                break;
            case ObjectLayer objects:
                var other = (ObjectLayer)actual;
                Assert.Equal(objects.Color ?? ObjectLayer.DefaultColor, other.Color);
                Assert.Equal(objects.Objects.Count, other.Objects.Count);
                for (var i = 0; i < objects.Objects.Count; i++)
                    Object(objects.Objects[i], other.Objects[i]);
                break;
        }
    }

    private static void Object(MapObject expected, MapObject actual)
    {
        Assert.Equal(
            (expected.Id, expected.Name, expected.Type, expected.Shape, expected.Cell, expected.Tile),
            (actual.Id, actual.Name, actual.Type, actual.Shape, actual.Cell, actual.Tile));
        Assert.Equal(expected.Position.X, actual.Position.X, 0.01f);
        Assert.Equal(expected.Position.Y, actual.Position.Y, 0.01f);
        Assert.Equal(expected.Polygon.Count, actual.Polygon.Count);
        for (var i = 0; i < expected.Polygon.Count; i++)
        {
            Assert.Equal(expected.Polygon[i].X, actual.Polygon[i].X, 0.01f);
            Assert.Equal(expected.Polygon[i].Y, actual.Polygon[i].Y, 0.01f);
        }

        Properties(expected.Properties, actual.Properties);
    }

    private static PlacedTile[] Cells(TileLayer layer)
    {
        var cells = new List<PlacedTile>();
        foreach (var placed in layer.Cells)
            cells.Add(placed);
        return [.. cells.OrderBy(c => c.Cell.Y).ThenBy(c => c.Cell.X)];
    }
}
