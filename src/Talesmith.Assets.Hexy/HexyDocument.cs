using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Assets.Hexy;

internal sealed class MapDocument
{
    public string? Format { get; set; }
    public int Version { get; set; }
    public string? Grid { get; set; }
    public string? Orientation { get; set; }
    public double? HexWidth { get; set; }
    public double? HexHeight { get; set; }
    public double? TileWidth { get; set; }
    public double? TileHeight { get; set; }
    public int ChunkSize { get; set; }
    public int NextObjectId { get; set; }
    public string? BackgroundColor { get; set; }
    public List<PropertyDocument>? Properties { get; set; }
    public List<TilesetDocument>? Tilesets { get; set; }
    public List<TerrainDocument>? Terrains { get; set; }
    public List<LayerDocument>? Layers { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class PropertyDocument
{
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Value { get; set; }
}

internal sealed class TilesetDocument
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? Source { get; set; }
    public int TileWidth { get; set; }
    public int TileHeight { get; set; }
    public int Margin { get; set; }
    public int Spacing { get; set; }
    public int Columns { get; set; }
    public int TileCount { get; set; }
    public List<TileDocument>? Tiles { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class TileDocument
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Color { get; set; }
    public List<FrameDocument>? Animation { get; set; }
    public List<PropertyDocument>? Properties { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class FrameDocument
{
    public int Tile { get; set; }
    public int Duration { get; set; }
}

internal sealed class TerrainDocument
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Tileset { get; set; }
    public int BaseTile { get; set; }
    public List<RuleDocument>? Rules { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class RuleDocument
{
    public string Pattern { get; set; } = "******";
    public bool Rotations { get; set; }
    public List<WeightedTileDocument>? Tiles { get; set; }
}

internal sealed class WeightedTileDocument
{
    public int Tile { get; set; }
    public double Weight { get; set; } = 1.0;
}

internal sealed class LayerDocument
{
    public string? Type { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public double Opacity { get; set; } = 1;
    public List<PropertyDocument>? Properties { get; set; }
    public string? Encoding { get; set; }
    public List<ChunkDocument>? Chunks { get; set; }
    public string? Color { get; set; }
    public List<ObjectDocument>? Objects { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class ChunkDocument
{
    public int Q { get; set; }
    public int R { get; set; }
    public string Data { get; set; } = string.Empty;
}

internal sealed class ObjectDocument
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Shape { get; set; }
    public int Q { get; set; }
    public int R { get; set; }
    public double[]? Offset { get; set; }
    public List<double[]>? Vertices { get; set; }
    public ObjectTileDocument? Tile { get; set; }
    public List<PropertyDocument>? Properties { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class ObjectTileDocument
{
    public int Tileset { get; set; }
    public int Id { get; set; }
    public int Rotation { get; set; }
    public bool FlipX { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(MapDocument))]
internal sealed partial class HexyJsonContext : JsonSerializerContext;
