using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;
using Talesmith.Runtime.Components;

namespace Talesmith.Lighting.Systems;

/// <summary>Keeps the shadow outline of every collision-layer chunk near shadowed lights, rebuilding a chunk only when it or the map's
/// tilesets change.</summary>
internal sealed class TileOccluderCache
{
    private const int EvictAfterFrames = 600;
    private const int EvictionInterval = 120;

    private readonly TileOccluderBuilder _builder = new();
    private readonly Dictionary<ChunkKey, Entry> _entries = [];
    private readonly List<ChunkKey> _evict = [];
    private long _frame;

    /// <summary>Adds the outlines of collision chunks overlapping <paramref name="region"/> to the frame.</summary>
    /// <returns>The chunks added and the chunks whose outline was rebuilt.</returns>
    public (int Chunks, int Built) Collect(World world, in Rect2 region, uint layers, LightingFrame lighting)
    {
        _frame++;
        int chunks = 0, built = 0;
        foreach (var archetype in world.Query<TileMapComponent, Transform>())
        {
            var entities = archetype.Entities;
            var maps = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < maps.Length; i++)
                CollectMap(entities[i], maps[i].Map, transforms[i].Position, region, layers, lighting, ref chunks, ref built);
        }

        if (_frame % EvictionInterval == 0)
            Evict();
        return (chunks, built);
    }

    public void Clear() => _entries.Clear();

    private void CollectMap(Entity entity, TileMap map, Vector2 origin, in Rect2 region, uint layers, LightingFrame lighting, ref int chunks, ref int built)
    {
        var (min, max) = ChunkCoord.Covering(map.Layout.CoveringBounds(region.Offset(-origin)), map.ChunkShift);
        for (var layerIndex = 0; layerIndex < map.Layers.Count; layerIndex++)
        {
            if (map.Layers[layerIndex] is not TileLayer { Role: LayerRole.Collision } layer)
                continue;
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var x = min.X; x <= max.X; x++)
                {
                    var coord = new ChunkCoord(x, y);
                    if (!layer.Chunks.TryGetValue(coord, out var chunk))
                        continue;

                    var key = new ChunkKey(entity, layerIndex, coord);
                    if (!_entries.TryGetValue(key, out var entry) || entry.Version != chunk.Version || entry.Origin != origin || !ReferenceEquals(entry.Map, map)
                        || entry.TilesetVersion != map.TilesetVersion)
                    {
                        var (edges, bounds) = _builder.Build(map, layer, chunk, origin);
                        _entries[key] = entry = new Entry(map, origin, chunk.Version, map.TilesetVersion, edges, bounds);
                        built++;
                    }

                    entry.LastUsedFrame = _frame;
                    if (entry.Edges.Length == 0)
                        continue;
                    lighting.AddOccluder(entry.Edges, entry.Bounds, layers, selfShadows: false);
                    chunks++;
                }
            }
        }
    }

    private void Evict()
    {
        _evict.Clear();
        foreach (var (key, entry) in _entries)
        {
            if (_frame - entry.LastUsedFrame > EvictAfterFrames)
                _evict.Add(key);
        }

        foreach (var key in _evict)
            _entries.Remove(key);
    }

    private readonly record struct ChunkKey(Entity Map, int Layer, ChunkCoord Coord);

    private sealed class Entry(TileMap map, Vector2 origin, int version, long tilesetVersion, ShadowEdge[] edges, Rect2 bounds)
    {
        public TileMap Map { get; } = map;

        public Vector2 Origin { get; } = origin;

        public int Version { get; } = version;

        /// <summary>The map's <see cref="TileMap.TilesetVersion"/> when built: tile shapes follow their artwork.</summary>
        public long TilesetVersion { get; } = tilesetVersion;

        public ShadowEdge[] Edges { get; } = edges;

        public Rect2 Bounds { get; } = bounds;

        public long LastUsedFrame { get; set; }
    }
}
