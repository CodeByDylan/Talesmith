using Talesmith.Assets.Maps;
using Talesmith.Ecs;

namespace Talesmith.Runtime.Maps;

/// <summary>Raised after a map and its objects were spawned into a world, so gameplay can place players, enemies and triggers.</summary>
public readonly record struct MapLoaded(Entity MapEntity, TileMap Map, World World);
