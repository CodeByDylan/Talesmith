using System.Text.Json;
using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Hexy;

/// <summary>Writes <c>map.json</c> exactly as Hexy's serializer does: the same fields in the same order, indented, nulls and empty lists left out.</summary>
/// <remarks>Fields Hexy does not know, read from the original file, follow the known fields of their object, as System.Text.Json writes extension data.</remarks>
internal static class HexyJsonWriter
{
    /// <param name="images">The <c>image</c> value of each tileset, by tileset id; tilesets without an entry are color tilesets.</param>
    public static void Write(Stream stream, HexySnapshot map, IReadOnlyDictionary<int, string> images)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = map.NewLine });
        json.WriteStartObject();
        json.WriteString("format", HexyMapReader.FormatId);
        json.WriteNumber("version", HexyMapReader.CurrentVersion);
        json.WriteString("orientation", map.Orientation);
        json.WriteNumber("hexWidth", map.HexWidth);
        json.WriteNumber("hexHeight", map.HexHeight);
        json.WriteNumber("chunkSize", map.ChunkSize);
        json.WriteNumber("nextObjectId", map.NextObjectId);
        if (map.BackgroundColor is not null)
            json.WriteString("backgroundColor", map.BackgroundColor);
        WriteProperties(json, map.Properties);

        json.WriteStartArray("tilesets");
        foreach (var tileset in map.Tilesets)
            WriteTileset(json, tileset, images.GetValueOrDefault(tileset.Id));
        json.WriteEndArray();

        if (map.Terrains.Length > 0)
        {
            json.WriteStartArray("terrains");
            foreach (var terrain in map.Terrains)
                WriteTerrain(json, terrain);
            json.WriteEndArray();
        }

        json.WriteStartArray("layers");
        foreach (var layer in map.Layers)
            WriteLayer(json, layer, map);
        json.WriteEndArray();

        WriteExtra(json, map.Extra);
        json.WriteEndObject();
    }

    private static void WriteTileset(Utf8JsonWriter json, TilesetSnapshot tileset, string? image)
    {
        json.WriteStartObject();
        json.WriteNumber("id", tileset.Id);
        json.WriteString("name", tileset.Name);
        if (image is not null)
            json.WriteString("image", image);
        if (tileset.ImageFile?.Source is { } source)
            json.WriteString("source", source);
        json.WriteNumber("tileWidth", tileset.TileWidth);
        json.WriteNumber("tileHeight", tileset.TileHeight);
        json.WriteNumber("margin", tileset.Margin);
        json.WriteNumber("spacing", tileset.Spacing);
        json.WriteNumber("columns", tileset.Columns);
        json.WriteNumber("tileCount", tileset.TileCount);
        if (tileset.Tiles.Length > 0)
        {
            json.WriteStartArray("tiles");
            foreach (var tile in tileset.Tiles)
                WriteTile(json, tile);
            json.WriteEndArray();
        }

        WriteExtra(json, tileset.Extra);
        json.WriteEndObject();
    }

    private static void WriteTile(Utf8JsonWriter json, TileInfo tile)
    {
        json.WriteStartObject();
        json.WriteNumber("id", tile.Id);
        if (tile.Name is not null)
            json.WriteString("name", tile.Name);
        if (tile.Color is { } color)
            json.WriteString("color", color.ToString());
        if (tile.Animation.Count > 0)
        {
            json.WriteStartArray("animation");
            foreach (var frame in tile.Animation)
            {
                json.WriteStartObject();
                json.WriteNumber("tile", frame.TileId);
                json.WriteNumber("duration", frame.DurationMilliseconds);
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        WriteProperties(json, TilesetSnapshot.PropertiesOf(tile));
        WriteExtra(json, (tile.FormatData as HexyTileData)?.Extra);
        json.WriteEndObject();
    }

    private static void WriteTerrain(Utf8JsonWriter json, Terrain terrain)
    {
        json.WriteStartObject();
        json.WriteNumber("id", terrain.Id);
        json.WriteString("name", terrain.Name);
        json.WriteNumber("tileset", terrain.TilesetId);
        json.WriteNumber("baseTile", terrain.BaseTileId);
        json.WriteStartArray("rules");
        foreach (var rule in terrain.Rules)
        {
            json.WriteStartObject();
            json.WriteString("pattern", rule.Pattern);
            json.WriteBoolean("rotations", rule.MatchRotations);
            json.WriteStartArray("tiles");
            foreach (var tile in rule.Tiles)
            {
                json.WriteStartObject();
                json.WriteNumber("tile", tile.TileId);
                json.WriteNumber("weight", tile.Weight);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteExtra(json, (terrain.FormatData as HexyTerrainData)?.Extra);
        json.WriteEndObject();
    }

    private static void WriteLayer(Utf8JsonWriter json, LayerSnapshot layer, HexySnapshot map)
    {
        json.WriteStartObject();
        switch (layer)
        {
            case TileLayerSnapshot tiles:
                json.WriteString("type", "tiles");
                json.WriteString("encoding", HexyMapReader.ChunkEncoding);
                json.WriteStartArray("chunks");
                var cellCount = map.ChunkSize * map.ChunkSize;
                foreach (var chunk in tiles.Chunks)
                {
                    if (chunk.Encode(cellCount) is not { } data)
                        continue;
                    json.WriteStartObject();
                    json.WriteNumber("q", chunk.Coord.X);
                    json.WriteNumber("r", chunk.Coord.Y);
                    json.WriteString("data", Convert.ToBase64String(data.Span));
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                break;
            case ObjectLayerSnapshot objects:
                json.WriteString("type", "objects");
                json.WriteString("color", objects.Color);
                json.WriteStartArray("objects");
                foreach (var mapObject in objects.Objects)
                    WriteObject(json, mapObject, map);
                json.WriteEndArray();
                break;
        }

        json.WriteString("id", layer.Id);
        json.WriteString("name", layer.Name);
        json.WriteBoolean("visible", layer.Visible);
        json.WriteBoolean("locked", layer.Locked);
        json.WriteNumber("opacity", layer.Opacity);
        WriteProperties(json, layer.Properties);
        WriteExtra(json, layer.Extra);
        json.WriteEndObject();
    }

    private static void WriteObject(Utf8JsonWriter json, MapObject mapObject, HexySnapshot map)
    {
        var (offset, vertices) = ObjectGeometry.Of(mapObject, map.Layout);
        json.WriteStartObject();
        json.WriteNumber("id", mapObject.Id);
        if (!string.IsNullOrEmpty(mapObject.Name))
            json.WriteString("name", mapObject.Name);
        if (!string.IsNullOrEmpty(mapObject.Type))
            json.WriteString("type", mapObject.Type);
        json.WriteString("shape", mapObject.Shape switch
        {
            MapObjectShape.Polygon => "polygon",
            MapObjectShape.Tile => "tile",
            _ => "point"
        });
        json.WriteNumber("q", mapObject.Cell.X);
        json.WriteNumber("r", mapObject.Cell.Y);
        if (offset is not null)
            WriteNumbers(json, "offset", offset);
        if (vertices is { Count: > 0 })
        {
            json.WriteStartArray("vertices");
            foreach (var vertex in vertices)
                WriteNumbers(json, null, vertex);
            json.WriteEndArray();
        }

        if (mapObject.Shape == MapObjectShape.Tile)
        {
            json.WriteStartObject("tile");
            json.WriteNumber("tileset", mapObject.Tile.TilesetId);
            json.WriteNumber("id", mapObject.Tile.TileId);
            json.WriteNumber("rotation", mapObject.Tile.Rotation);
            json.WriteBoolean("flipX", mapObject.Tile.FlipX);
            json.WriteEndObject();
        }

        WriteProperties(json, [.. mapObject.Properties.Values]);
        WriteExtra(json, (mapObject.FormatData as HexyObjectData)?.Extra);
        json.WriteEndObject();
    }

    private static void WriteNumbers(Utf8JsonWriter json, string? name, double[] values)
    {
        if (name is null)
            json.WriteStartArray();
        else
            json.WriteStartArray(name);
        foreach (var value in values)
            json.WriteNumberValue(value);
        json.WriteEndArray();
    }

    private static void WriteProperties(Utf8JsonWriter json, IReadOnlyList<KeyValuePair<string, PropertyValue>> properties)
    {
        if (properties.Count == 0)
            return;
        json.WriteStartArray("properties");
        foreach (var (name, value) in properties)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteString("type", value.Type.ToString().ToLowerInvariant());
            json.WriteString("value", value.Raw);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteExtra(Utf8JsonWriter json, Dictionary<string, JsonElement>? extra)
    {
        if (extra is null)
            return;
        foreach (var (name, value) in extra)
        {
            json.WritePropertyName(name);
            value.WriteTo(json);
        }
    }
}
