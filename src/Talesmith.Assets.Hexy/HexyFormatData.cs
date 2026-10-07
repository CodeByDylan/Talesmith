using System.Numerics;
using System.Text.Json;
using Talesmith.Assets.Maps;
using Talesmith.Grids;

namespace Talesmith.Assets.Hexy;

/// <summary>A property the reader turned into model data, remembered as stored so an unchanged value is written back identically.</summary>
internal sealed record StoredProperty(int Index, string Name, PropertyValue Value);

/// <summary>What a .hexy map held that the model does not represent exactly, kept in <see cref="TileMap"/> format data.</summary>
/// <param name="NewLine">The line ending of the stored <c>map.json</c>, which depends on the system Hexy saved it on; null when it had no line breaks.</param>
internal sealed record HexyMapData(Dictionary<string, JsonElement>? Extra, int Version, double HexWidth, double HexHeight, string? NewLine);

internal sealed record HexyTilesetData(Dictionary<string, JsonElement>? Extra);

internal sealed record HexyTileData(Dictionary<string, JsonElement>? Extra, StoredProperty? Collision);

internal sealed record HexyTerrainData(Dictionary<string, JsonElement>? Extra);

internal sealed record HexyLayerData(Dictionary<string, JsonElement>? Extra, double Opacity, StoredProperty? Role);

/// <summary>The cell-unit geometry an object was stored with, and the world geometry it became, to detect whether it moved.</summary>
internal sealed record HexyObjectData(
    Dictionary<string, JsonElement>? Extra,
    GridCoord Anchor,
    double[]? Offset,
    List<double[]>? Vertices,
    Vector2 Position,
    Vector2[] Polygon);
