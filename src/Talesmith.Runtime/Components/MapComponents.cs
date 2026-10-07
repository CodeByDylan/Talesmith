using Talesmith.Assets.Maps;
using Talesmith.Rendering;

namespace Talesmith.Runtime.Components;

/// <summary>An entity that shows a tile map; its tile layers are drawn and streamed by the map systems.</summary>
/// <remarks>
/// The entity's <see cref="Transform"/> position is where the map's cell (0, 0) is centered, so several maps, on any mix of grids, can
/// be placed and layered in one world. Rotation and scale do not apply to maps.
/// </remarks>
public sealed record TileMapComponent(TileMap Map)
{
    /// <summary>The render layer of the map's first tile layer; tile layer <c>i</c> is drawn on <see cref="RenderLayer"/> + <c>i</c>.</summary>
    public int RenderLayer { get; init; } = RenderLayers.Terrain;
}

/// <summary>Links an entity to the map object it was spawned from.</summary>
/// <param name="Map">The map entity that spawned this object.</param>
public sealed record MapObjectComponent(MapObject Object, ObjectLayer Layer, Ecs.Entity Map);
