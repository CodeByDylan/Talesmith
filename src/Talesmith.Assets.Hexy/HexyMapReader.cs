using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Hexy;

/// <summary>Converts a parsed .hexy document into a <see cref="TileMap"/>, keeping what the model does not represent for the writer.</summary>
internal sealed class HexyMapReader(AssetImportContext context)
{
    public const int CurrentVersion = 2;

    public const string FormatId = "hexy";
    public const string ChunkEncoding = "base64-zlib-u32le";

    /// <exception cref="AssetException">The document is not a supported Hexy map.</exception>
    public static void Validate([NotNull] MapDocument? document)
    {
        if (document is null || !string.Equals(document.Format, FormatId, StringComparison.Ordinal))
            throw new AssetException("The file is not a Hexy map.");
        if (document.Version > CurrentVersion)
            throw new AssetException($"The map uses .hexy format version {document.Version}; versions up to {CurrentVersion} are supported.");
        if (document.ChunkSize is < 4 or > 256 || !BitOperations.IsPow2(document.ChunkSize))
            throw new AssetException($"Chunk size {document.ChunkSize} is not supported; it must be a power of two from 4 to 256.");
    }

    /// <param name="images">The image of each tileset, in document order; null for color tilesets.</param>
    /// <param name="newLine">The line ending of the document, so it is written back with the same one.</param>
    public TileMap Read(MapDocument document, IReadOnlyList<TilesetImage?> images, string? newLine)
    {
        var (layout, width, height) = CreateLayout(document);
        var chunkShift = BitOperations.Log2((uint)document.ChunkSize);
        var tilesets = ReadTilesets(document.Tilesets ?? [], images);
        var terrains = ReadTerrains(document.Terrains ?? [], tilesets, layout.Topology);
        var layers = new List<MapLayer>(document.Layers?.Count ?? 0);
        foreach (var layerDocument in document.Layers ?? [])
        {
            if (ReadLayer(layerDocument, layout, chunkShift, tilesets) is { } layer)
                layers.Add(layer);
        }

        var map = new TileMap(context.Path, layout, chunkShift, tilesets, layers, ReadProperties(document.Properties), terrains)
        {
            BackgroundColor = document.BackgroundColor is null ? null : ParseColor(document.BackgroundColor, "the map background")
        };
        map.NextObjectId = Math.Max(map.NextObjectId, document.NextObjectId);
        map.FormatData = new HexyMapData(document.Extra, document.Version, width, height, newLine);
        return map;
    }

    /// <remarks>Hexy writes rectangular maps with the orientation <c>"orthogonal"</c>; the <c>"square"</c> grid and orientation of earlier maps load the same way.</remarks>
    private static (IGridLayout Layout, double Width, double Height) CreateLayout(MapDocument document)
    {
        var grid = document.Grid ?? "hex";
        if (grid == "square" || document.Orientation is "orthogonal" or "square")
        {
            var width = document.HexWidth ?? document.TileWidth ?? 0;
            var height = document.HexHeight ?? document.TileHeight ?? 0;
            RequirePositive(width, height);
            return (new SquareLayout((float)width, (float)height), width, height);
        }

        if (grid != "hex")
            throw new AssetException($"Unknown grid '{grid}'. Expected 'hex' or 'square'.");

        var pointyTop = (document.Orientation ?? "pointy") switch
        {
            "pointy" => true,
            "flat" => false,
            _ => throw new AssetException($"Unknown orientation '{document.Orientation}'. Expected 'pointy', 'flat' or 'orthogonal'.")
        };
        var hexWidth = document.HexWidth ?? 0;
        var hexHeight = document.HexHeight ?? 0;
        RequirePositive(hexWidth, hexHeight);
        return (new HexLayout(pointyTop, (float)hexWidth, (float)hexHeight), hexWidth, hexHeight);
    }

    private static void RequirePositive(double width, double height)
    {
        if (width <= 0 || height <= 0)
            throw new AssetException("The map's cell size must be greater than zero.");
    }

    private List<Tileset> ReadTilesets(List<TilesetDocument> documents, IReadOnlyList<TilesetImage?> images)
    {
        var tilesets = new List<Tileset>(documents.Count);
        var ids = new HashSet<int>();
        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            if (document.TileWidth <= 0 || document.TileHeight <= 0)
                throw new AssetException($"Tileset \"{document.Name}\" has an invalid tile size.");
            if (document.Id is < 1 or > TileCell.MaxTilesetId)
                throw new AssetException($"Tileset \"{document.Name}\" has an invalid id {document.Id}.");
            if (!ids.Add(document.Id))
                throw new AssetException($"Tileset \"{document.Name}\" reuses id {document.Id}.");

            var tiles = new Dictionary<int, TileInfo>();
            foreach (var tile in document.Tiles ?? [])
                tiles[tile.Id] = ReadTile(tile, document.Name);

            var image = images[i];
            tilesets.Add(new Tileset(document.Id, document.Name, document.TileWidth, document.TileHeight, image?.Texture, document.Margin, document.Spacing,
                document.Columns, document.TileCount, tiles)
            {
                Placement = TilePlacement.BottomCenter,
                ImageFile = image?.File,
                FormatData = new HexyTilesetData(document.Extra)
            });
        }

        return tilesets;
    }

    private TileInfo ReadTile(TileDocument document, string tilesetName)
    {
        var color = document.Color is null ? (Color?)null : ParseColor(document.Color, $"tile {document.Id} of \"{tilesetName}\"");
        var animation = new TileFrame[document.Animation?.Count ?? 0];
        for (var i = 0; i < animation.Length; i++)
            animation[i] = new TileFrame(document.Animation![i].Tile, Math.Max(1, document.Animation[i].Duration));

        var properties = ReadProperties(document.Properties);
        IReadOnlyList<IReadOnlyList<Vector2>> collision = [];
        StoredProperty? stored = null;
        if (properties.TryGet(HexyConventions.CollisionProperty, out var value) && HexyConventions.TryParseCollision(value, out collision))
        {
            stored = new StoredProperty(IndexOf(properties, HexyConventions.CollisionProperty), HexyConventions.CollisionProperty, value);
            properties = properties.Without(HexyConventions.CollisionProperty);
        }

        return new TileInfo(document.Id, document.Name, color, animation, properties)
        {
            Collision = collision,
            FormatData = new HexyTileData(document.Extra, stored)
        };
    }

    /// <summary>Reads terrains like Hexy: terrains of missing tilesets and rules for another grid are skipped with a warning.</summary>
    private List<Terrain> ReadTerrains(List<TerrainDocument> documents, List<Tileset> tilesets, GridTopology topology)
    {
        var terrains = new List<Terrain>(documents.Count);
        foreach (var document in documents)
        {
            if (!tilesets.Exists(t => t.Id == document.Tileset))
            {
                Warn($"Terrain \"{document.Name}\" references missing tileset {document.Tileset} and was skipped.");
                continue;
            }

            var rules = new List<AutoTileRule>();
            foreach (var rule in document.Rules ?? [])
            {
                try
                {
                    if (rule.Pattern.Length != topology.NeighborCount)
                        throw new FormatException($"Pattern '{rule.Pattern}' needs {topology.NeighborCount} characters, one per neighbor.");
                    var tiles = (rule.Tiles ?? []).Select(t => new WeightedTile(t.Tile, t.Weight)).ToArray();
                    rules.Add(new AutoTileRule(rule.Pattern, tiles, rule.Rotations));
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    Warn($"Terrain \"{document.Name}\" has an invalid rule that was skipped: {ex.Message}");
                }
            }

            terrains.Add(new Terrain(document.Name, document.Tileset, document.BaseTile, rules, document.Id) { FormatData = new HexyTerrainData(document.Extra) });
        }

        return terrains;
    }

    private MapLayer? ReadLayer(LayerDocument document, IGridLayout layout, int chunkShift, List<Tileset> tilesets)
    {
        MapLayer layer;
        switch (document.Type)
        {
            case "tiles":
                layer = ReadTileLayer(document, chunkShift);
                break;
            case "objects":
                layer = ReadObjectLayer(document, layout, tilesets);
                break;
            default:
                Warn($"Layer \"{document.Name}\" has unsupported type '{document.Type}' and was skipped; it is not saved back.");
                return null;
        }

        var properties = ReadProperties(document.Properties);
        StoredProperty? stored = null;
        if (properties.TryGet(HexyConventions.RoleProperty, out var value) && value.Type == PropertyType.String && LayerRoles.TryParse(value.Raw, out var role))
        {
            stored = new StoredProperty(IndexOf(properties, HexyConventions.RoleProperty), HexyConventions.RoleProperty, value);
            properties = properties.Without(HexyConventions.RoleProperty);
        }
        else
        {
            role = LayerRoles.Infer(document.Name, layer is ObjectLayer);
        }

        layer.Role = role;
        layer.Properties = properties;
        layer.IsVisible = document.Visible;
        layer.IsLocked = document.Locked;
        layer.Opacity = (float)Math.Clamp(document.Opacity, 0, 1);
        layer.FormatData = new HexyLayerData(document.Extra, document.Opacity, stored);
        return layer;
    }

    private static TileLayer ReadTileLayer(LayerDocument document, int chunkShift)
    {
        if (!string.Equals(document.Encoding, ChunkEncoding, StringComparison.Ordinal))
            throw new AssetException($"Layer \"{document.Name}\" uses unsupported chunk encoding '{document.Encoding}'.");

        var layer = new TileLayer(document.Name, chunkShift) { Id = LayerId(document) };
        foreach (var chunk in document.Chunks ?? [])
        {
            var compressed = DecodeChunkData(chunk.Data)
                ?? throw new AssetException($"Layer \"{document.Name}\" has corrupt data in chunk ({chunk.Q}, {chunk.R}).");
            layer.AddCompressedChunk(new ChunkCoord(chunk.Q, chunk.R), compressed);
        }

        return layer;
    }

    private static Guid LayerId(LayerDocument document) => document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;

    /// <summary>Base64-decodes chunk data, returning null unless the result starts with a zlib header (RFC 1950).</summary>
    private static byte[]? DecodeChunkData(string data)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return null;
        }

        return bytes.Length >= 2 && (bytes[0] & 0x0F) == 8 && ((bytes[0] << 8) | bytes[1]) % 31 == 0 ? bytes : null;
    }

    private ObjectLayer ReadObjectLayer(LayerDocument document, IGridLayout layout, List<Tileset> tilesets)
    {
        var objects = new List<MapObject>(document.Objects?.Count ?? 0);
        foreach (var objectDocument in document.Objects ?? [])
            objects.Add(ReadObject(objectDocument, document.Name, layout, tilesets));

        return new ObjectLayer(document.Name, objects)
        {
            Id = LayerId(document),
            Color = document.Color is null ? null : ParseColor(document.Color, $"layer \"{document.Name}\"")
        };
    }

    private MapObject ReadObject(ObjectDocument document, string layerName, IGridLayout layout, List<Tileset> tilesets)
    {
        var shape = document.Shape?.ToLowerInvariant() switch
        {
            "polygon" => MapObjectShape.Polygon,
            "tile" when document.Tile is not null => MapObjectShape.Tile,
            _ => MapObjectShape.Point
        };

        var anchor = new GridCoord(document.Q, document.R);
        var offset = document.Offset is [var q, var r] ? new Vector2((float)q, (float)r) : Vector2.Zero;
        var position = layout.CellToWorld(new Vector2(anchor.X, anchor.Y) + offset);

        Vector2[] polygon = shape == MapObjectShape.Polygon && document.Vertices is { } vertices
            ? vertices.Where(v => v.Length == 2).Select(v => layout.CellToWorld(new Vector2((float)v[0], (float)v[1]))).ToArray()
            : [];
        var tile = shape == MapObjectShape.Tile ? ReadObjectTile(document.Tile!, layerName, layout.RotationSteps, tilesets) : TileCell.Empty;

        return new MapObject(document.Id, document.Name ?? string.Empty, document.Type ?? string.Empty, shape, position, anchor, polygon, tile,
            ReadProperties(document.Properties))
        {
            FormatData = new HexyObjectData(document.Extra, anchor, document.Offset, document.Vertices, position, polygon)
        };
    }

    private TileCell ReadObjectTile(ObjectTileDocument document, string layerName, int rotationSteps, List<Tileset> tilesets)
    {
        if (document.Tileset is < 1 or > TileCell.MaxTilesetId || document.Id is < 0 or > TileCell.MaxTileId)
            throw new AssetException($"Layer \"{layerName}\" has an image object with an invalid tile reference.");
        if (!tilesets.Exists(t => t.Id == document.Tileset))
            Warn($"Layer \"{layerName}\" has an image object from tileset {document.Tileset}, which is not part of the map.");
        return new TileCell(document.Tileset, document.Id, ((document.Rotation % rotationSteps) + rotationSteps) % rotationSteps, document.FlipX);
    }

    private PropertySet ReadProperties(List<PropertyDocument>? documents)
    {
        if (documents is not { Count: > 0 })
            return PropertySet.Empty;

        var values = new Dictionary<string, PropertyValue>(documents.Count, StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (!Enum.TryParse<PropertyType>(document.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            {
                Warn($"Property \"{document.Name}\" has unknown type '{document.Type}' and was read as text.");
                type = PropertyType.String;
            }

            values[document.Name] = new PropertyValue(type, document.Value ?? string.Empty);
        }

        return new PropertySet(values);
    }

    private static int IndexOf(PropertySet properties, string name)
    {
        var index = 0;
        foreach (var key in properties.Values.Keys)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
                return index;
            index++;
        }

        return -1;
    }

    private Color ParseColor(string text, string owner)
    {
        if (Color.TryParse(text, out var color))
            return color;
        Warn($"Invalid color '{text}' on {owner} was replaced with white.");
        return Color.White;
    }

    private void Warn(string message) => HexyLog.Warning(context.Logger, context.Path, message);
}
