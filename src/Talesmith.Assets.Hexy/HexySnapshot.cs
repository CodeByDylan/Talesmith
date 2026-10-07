using System.Globalization;
using System.Text.Json;
using Talesmith.Assets.Maps;
using Talesmith.Grids;

namespace Talesmith.Assets.Hexy;

/// <summary>Everything the writer needs from a map, captured on the caller's thread so the map can change while the file is written.</summary>
/// <remarks>
/// Model data that is already immutable (objects, tile data, terrains, property sets) is shared; layer and tileset settings and
/// chunk cells are copied. Values the model stores with less precision than the file, such as cell sizes and opacities, are written
/// as they were read unless they changed.
/// </remarks>
internal sealed class HexySnapshot
{
    private HexySnapshot()
    {
    }

    public required IGridLayout Layout { get; init; }

    public required string Orientation { get; init; }

    public required double HexWidth { get; init; }

    public required double HexHeight { get; init; }

    public required int ChunkSize { get; init; }

    public required int NextObjectId { get; init; }

    public required string? BackgroundColor { get; init; }

    public required IReadOnlyList<KeyValuePair<string, PropertyValue>> Properties { get; init; }

    public required TilesetSnapshot[] Tilesets { get; init; }

    public required Terrain[] Terrains { get; init; }

    public required LayerSnapshot[] Layers { get; init; }

    public required Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The line ending the map was read with, so an unchanged map is written back byte for byte; new maps use the system's, as Hexy does.</summary>
    public required string NewLine { get; init; }

    public static HexySnapshot Capture(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var data = map.FormatData as HexyMapData;
        var size = map.Layout.CellSize;
        return new HexySnapshot
        {
            Layout = map.Layout,
            Orientation = map.Layout.Kind switch
            {
                GridKind.HexPointyTop => "pointy",
                GridKind.HexFlatTop => "flat",
                _ => "orthogonal"
            },
            HexWidth = data is not null && (float)data.HexWidth == size.X ? data.HexWidth : ToDouble(size.X),
            HexHeight = data is not null && (float)data.HexHeight == size.Y ? data.HexHeight : ToDouble(size.Y),
            ChunkSize = 1 << map.ChunkShift,
            NextObjectId = map.NextObjectId,
            BackgroundColor = map.BackgroundColor?.ToString(),
            Properties = [.. map.Properties.Values],
            Tilesets = map.Tilesets.Select(TilesetSnapshot.Capture).ToArray(),
            Terrains = [.. map.Terrains],
            Layers = map.Layers.Select(LayerSnapshot.Capture).ToArray(),
            Extra = data?.Extra,
            NewLine = data?.NewLine ?? Environment.NewLine
        };
    }

    /// <summary>The shortest decimal that reads back as the same float, so 0.7f is written as 0.7.</summary>
    public static double ToDouble(float value) => double.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Puts a property the model keeps as data back among the properties: where it was when unchanged, otherwise last.</summary>
    public static IReadOnlyList<KeyValuePair<string, PropertyValue>> WithStored(PropertySet properties, string name, PropertyValue? current, StoredProperty? stored)
    {
        var list = new List<KeyValuePair<string, PropertyValue>>(properties.Values);
        if (current is not { } value || properties.Contains(name))
            return list;
        if (stored is not null && stored.Value == value)
            list.Insert(Math.Clamp(stored.Index, 0, list.Count), new KeyValuePair<string, PropertyValue>(name, value));
        else
            list.Add(new KeyValuePair<string, PropertyValue>(name, value));
        return list;
    }
}

internal sealed record TilesetSnapshot(
    int Id,
    string Name,
    TilesetImageFile? ImageFile,
    bool HasTexture,
    int TileWidth,
    int TileHeight,
    int Margin,
    int Spacing,
    int Columns,
    int TileCount,
    TileInfo[] Tiles,
    Dictionary<string, JsonElement>? Extra)
{
    public static TilesetSnapshot Capture(Tileset tileset)
    {
        var tiles = tileset.Tiles.Values.Where(t => t.HasData).OrderBy(t => t.Id).ToArray();
        return new TilesetSnapshot(tileset.Id, tileset.Name, tileset.ImageFile, tileset.Texture is not null, tileset.TileWidth, tileset.TileHeight, tileset.Margin, tileset.Spacing,
            tileset.Columns, tileset.TileCount, tiles, (tileset.FormatData as HexyTilesetData)?.Extra);
    }

    /// <summary>The tile's properties with its collision shapes stored as Talesmith stores them in Hexy maps.</summary>
    public static IReadOnlyList<KeyValuePair<string, PropertyValue>> PropertiesOf(TileInfo tile)
    {
        var stored = (tile.FormatData as HexyTileData)?.Collision;
        PropertyValue? current = null;
        if (tile.Collision.Count > 0)
        {
            current = stored is not null && HexyConventions.TryParseCollision(stored.Value, out var read) && HexyConventions.SameCollision(read, tile.Collision)
                ? stored.Value
                : HexyConventions.FormatCollision(tile.Collision);
        }

        return HexySnapshot.WithStored(tile.Properties, HexyConventions.CollisionProperty, current, stored);
    }
}

internal abstract record LayerSnapshot(
    Guid Id,
    string Name,
    bool Visible,
    bool Locked,
    double Opacity,
    IReadOnlyList<KeyValuePair<string, PropertyValue>> Properties,
    Dictionary<string, JsonElement>? Extra)
{
    public static LayerSnapshot Capture(MapLayer layer)
    {
        var data = layer.FormatData as HexyLayerData;
        var opacity = data is not null && (float)Math.Clamp(data.Opacity, 0, 1) == layer.Opacity ? Math.Clamp(data.Opacity, 0, 1) : HexySnapshot.ToDouble(layer.Opacity);
        var properties = HexySnapshot.WithStored(layer.Properties, HexyConventions.RoleProperty, RoleValue(layer, data?.Role), data?.Role);
        return layer switch
        {
            TileLayer tiles => new TileLayerSnapshot(layer.Id, layer.Name, layer.IsVisible, layer.IsLocked, opacity, properties, data?.Extra, CaptureChunks(tiles)),
            ObjectLayer objects => new ObjectLayerSnapshot(layer.Id, layer.Name, layer.IsVisible, layer.IsLocked, opacity, properties, data?.Extra,
                (objects.Color ?? ObjectLayer.DefaultColor).ToString(), [.. objects.Objects]),
            _ => throw new InvalidOperationException($"Layers of type {layer.GetType().Name} cannot be saved as .hexy.")
        };
    }

    /// <summary>The role property to store: as read when the role is unchanged, otherwise only when the name does not already imply the role.</summary>
    private static PropertyValue? RoleValue(MapLayer layer, StoredProperty? stored)
    {
        if (stored is not null && LayerRoles.TryParse(stored.Value.Raw, out var storedRole) && storedRole == layer.Role)
            return stored.Value;
        return layer.Role == LayerRoles.Infer(layer.Name, layer is ObjectLayer) ? null : PropertyValue.FromString(LayerRoles.Format(layer.Role));
    }

    private static ChunkSnapshot[] CaptureChunks(TileLayer layer)
    {
        var chunks = new List<ChunkSnapshot>(layer.Chunks.Count);
        foreach (var chunk in layer.ChunkValues)
        {
            chunks.Add(chunk.TryGetCompressed(out var compressed)
                ? new ChunkSnapshot(chunk.Coord, compressed, null)
                : new ChunkSnapshot(chunk.Coord, null, (TileCell[])chunk.EnsureDecoded().Clone()));
        }

        chunks.Sort((a, b) => a.Coord.Y != b.Coord.Y ? a.Coord.Y.CompareTo(b.Coord.Y) : a.Coord.X.CompareTo(b.Coord.X));
        return [.. chunks];
    }
}

internal sealed record TileLayerSnapshot(
    Guid Id,
    string Name,
    bool Visible,
    bool Locked,
    double Opacity,
    IReadOnlyList<KeyValuePair<string, PropertyValue>> Properties,
    Dictionary<string, JsonElement>? Extra,
    ChunkSnapshot[] Chunks) : LayerSnapshot(Id, Name, Visible, Locked, Opacity, Properties, Extra);

internal sealed record ObjectLayerSnapshot(
    Guid Id,
    string Name,
    bool Visible,
    bool Locked,
    double Opacity,
    IReadOnlyList<KeyValuePair<string, PropertyValue>> Properties,
    Dictionary<string, JsonElement>? Extra,
    string Color,
    MapObject[] Objects) : LayerSnapshot(Id, Name, Visible, Locked, Opacity, Properties, Extra);

/// <summary>A chunk as loaded, or a copy of its cells once edited.</summary>
internal sealed record ChunkSnapshot(ChunkCoord Coord, ReadOnlyMemory<byte>? Compressed, TileCell[]? Cells)
{
    /// <summary>Gets the chunk's compressed cells, or null when every cell is empty, since Hexy keeps no empty chunks.</summary>
    public ReadOnlyMemory<byte>? Encode(int cellCount)
    {
        var cells = Cells ?? ChunkCodec.Decode(Compressed!.Value.Span, cellCount);
        foreach (var cell in cells)
        {
            if (!cell.IsEmpty)
                return Compressed is { } compressed ? compressed : ChunkCodec.Encode(cells);
        }

        return null;
    }
}

/// <summary>Converts object geometry back to the cell units .hexy stores, keeping the stored numbers of objects that did not move.</summary>
internal static class ObjectGeometry
{
    private const int Decimals = 5;

    public static (double[]? Offset, List<double[]>? Vertices) Of(MapObject mapObject, IGridLayout layout)
    {
        if (mapObject.FormatData is HexyObjectData data && data.Anchor == mapObject.Cell && data.Position == mapObject.Position
            && mapObject.Polygon.SequenceEqual(data.Polygon))
            return (data.Offset, data.Vertices);

        var continuous = layout.WorldToContinuous(mapObject.Position);
        var x = Round(continuous.X - mapObject.Cell.X);
        var y = Round(continuous.Y - mapObject.Cell.Y);
        double[]? offset = x == 0 && y == 0 ? null : [x, y];

        List<double[]>? vertices = null;
        if (mapObject.Polygon.Count > 0)
        {
            vertices = new List<double[]>(mapObject.Polygon.Count);
            foreach (var vertex in mapObject.Polygon)
            {
                var cell = layout.WorldToContinuous(vertex);
                vertices.Add([Round(cell.X), Round(cell.Y)]);
            }
        }

        return (offset, vertices);
    }

    private static double Round(float value)
    {
        var rounded = Math.Round((double)value, Decimals);
        return rounded == 0 ? 0 : rounded;
    }
}
