using System.Numerics;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Physics.Geometry;
using Talesmith.Physics.Simulation;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Tiles;

/// <summary>Keeps one static body per tile map chunk near moving bodies and queries, rebuilt only when the chunk's cells change.</summary>
/// <remarks>
/// Chunks are built on demand, the first time something comes near them, so huge maps cost only what is used. Every step the cached
/// chunks compare their <see cref="TileChunk.Version"/>s; a changed chunk is rebuilt, keeping the shapes that did not change so contacts
/// on them persist, and its neighbors refresh the edges they share with it.
/// </remarks>
internal sealed class TileColliderCache(PhysicsState state, ILogger logger)
{
    private readonly List<MapEntry> _maps = [];
    private readonly List<TileShape> _shapes = [];
    private readonly Dictionary<ulong, int> _shapeIndex = new();
    private bool[] _matched = [];
    private int _stamp;

    public int MapCount => _maps.Count;

    /// <summary>Follows the scene's tile map entities: adds new maps, moves moved ones and removes those that are gone.</summary>
    public void Sync(World world)
    {
        _stamp++;
        foreach (var archetype in world.Query<TileMapComponent, Transform>())
        {
            var entities = archetype.Entities;
            var maps = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            var hasSettings = archetype.Has<TileMapCollider2D>();
            var settings = hasSettings ? archetype.GetSpan<TileMapCollider2D>() : default;
            for (var i = 0; i < entities.Length; i++)
            {
                var options = hasSettings ? settings[i] : new TileMapCollider2D();
                if (options.Enabled)
                    SyncMap(entities[i], maps[i].Map, transforms[i].Position, options);
            }
        }

        for (var i = _maps.Count - 1; i >= 0; i--)
        {
            if (_maps[i].Stamp == _stamp)
                continue;
            ClearChunks(_maps[i]);
            _maps.RemoveAt(i);
        }
    }

    /// <summary>Rebuilds cached chunks whose cells changed since they were built.</summary>
    public void Validate()
    {
        foreach (var map in _maps)
        {
            var chunks = map.ChunkList;
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var changed = HasChanged(map, chunk);
                if (!changed && !chunk.Dirty)
                    continue;
                Rebuild(map, chunk);
                if (changed)
                    MarkNeighborsDirty(map, chunk.Coord);
            }
        }
    }

    /// <summary>Builds the chunks of every map that a world-space box touches.</summary>
    public void EnsureRegion(in Aabb bounds)
    {
        foreach (var map in _maps)
        {
            if (map.Layers.Length == 0)
                continue;
            var chunkBounds = map.ChunkBounds(out var hasChunks);
            if (!hasChunks)
                continue;
            var local = bounds.Offset(-map.Origin).ToRect();
            var (min, max) = ChunkCoord.Covering(map.Map.Layout.CoveringBounds(local), map.Map.ChunkShift);
            var minX = Math.Max(min.X, chunkBounds.MinX);
            var minY = Math.Max(min.Y, chunkBounds.MinY);
            var maxX = Math.Min(max.X, chunkBounds.MaxX);
            var maxY = Math.Min(max.Y, chunkBounds.MaxY);
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var coord = new ChunkCoord(x, y);
                    if (map.Chunks.ContainsKey(coord))
                        continue;
                    var chunk = new ChunkEntry(coord, map.Layers.Length);
                    map.Chunks[coord] = chunk;
                    map.ChunkList.Add(chunk);
                    Rebuild(map, chunk);
                }
            }
        }
    }

    /// <summary>Finds the cell of a tile map that a world point just inside a surface lies in.</summary>
    public bool TryGetCell(Entity mapEntity, Vector2 point, out GridCoord cell)
    {
        foreach (var map in _maps)
        {
            if (map.Entity != mapEntity)
                continue;
            cell = map.Map.Layout.WorldToCell(point - map.Origin);
            return true;
        }

        cell = default;
        return false;
    }

    public void Clear()
    {
        foreach (var map in _maps)
            ClearChunks(map);
        _maps.Clear();
    }

    private void SyncMap(Entity entity, TileMap tileMap, Vector2 origin, in TileMapCollider2D options)
    {
        MapEntry? map = null;
        foreach (var candidate in _maps)
        {
            if (candidate.Entity == entity)
            {
                map = candidate;
                break;
            }
        }

        var fixtureOptions = new FixtureOptions(options.Friction, options.Restitution, 1, options.Layer, options.CollisionMask, false, false, new Vector2(0, -1));
        if (map is not null && (!ReferenceEquals(map.Map, tileMap) || map.Options != fixtureOptions || !map.HasSameLayers()))
        {
            ClearChunks(map);
            _maps.Remove(map);
            map = null;
        }

        if (map is null)
        {
            map = new MapEntry(entity, tileMap, origin, fixtureOptions, new TileCollisionBuilder(logger));
            _maps.Add(map);
        }
        else if (map.Origin != origin)
        {
            var displacement = origin - map.Origin;
            map.Origin = origin;
            foreach (var chunk in map.ChunkList)
            {
                if (chunk.Body == PhysicsState.Null)
                    continue;
                ref var body = ref state.Bodies[chunk.Body];
                body.SetPose(origin, 0);
                body.Flags |= BodyFlags.Moved;
                state.SynchronizeFixtures(chunk.Body, displacement);
            }
        }

        map.Stamp = _stamp;
    }

    private static bool HasChanged(MapEntry map, ChunkEntry chunk)
    {
        for (var l = 0; l < map.Layers.Length; l++)
        {
            map.Layers[l].Chunks.TryGetValue(chunk.Coord, out var source);
            if (!ReferenceEquals(source, chunk.Sources[l]) || (source is not null && source.Version != chunk.Versions[l]))
                return true;
        }

        return false;
    }

    private static void MarkNeighborsDirty(MapEntry map, ChunkCoord coord)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if ((dx != 0 || dy != 0) && map.Chunks.TryGetValue(new ChunkCoord(coord.X + dx, coord.Y + dy), out var neighbor))
                    neighbor.Dirty = true;
            }
        }
    }

    private void Rebuild(MapEntry map, ChunkEntry chunk)
    {
        chunk.Dirty = false;
        for (var l = 0; l < map.Layers.Length; l++)
        {
            map.Layers[l].Chunks.TryGetValue(chunk.Coord, out var source);
            chunk.Sources[l] = source;
            chunk.Versions[l] = source?.Version ?? 0;
        }

        map.Builder.Build(map.Map, map.Layers, chunk.Coord, _shapes);
        if (_shapes.Count == 0)
        {
            if (chunk.Body != PhysicsState.Null)
            {
                state.DestroyBody(chunk.Body);
                chunk.Body = PhysicsState.Null;
            }

            return;
        }

        if (chunk.Body == PhysicsState.Null)
            chunk.Body = state.CreateBody(map.Entity, BodyKind.Static, BodyFlags.TileChunk, map.Origin, 0);

        _shapeIndex.Clear();
        if (_matched.Length < _shapes.Count)
            _matched = new bool[Math.Max(_shapes.Count, _matched.Length * 2)];
        Array.Clear(_matched, 0, _shapes.Count);
        for (var i = 0; i < _shapes.Count; i++)
            _shapeIndex.TryAdd(Hash(_shapes[i].Shape, _shapes[i].OneWay), i);

        var fixture = state.Bodies[chunk.Body].FirstFixture;
        while (fixture != PhysicsState.Null)
        {
            var next = state.Fixtures[fixture].Next;
            ref var existing = ref state.Fixtures[fixture];
            if (_shapeIndex.TryGetValue(Hash(existing.Local, existing.OneWay), out var index) && !_matched[index]
                && SameShape(existing.Local, _shapes[index].Shape))
                _matched[index] = true;
            else
                state.DestroyFixture(fixture);
            fixture = next;
        }

        for (var i = 0; i < _shapes.Count; i++)
        {
            if (!_matched[i])
                state.CreateFixture(chunk.Body, _shapes[i].Shape, map.Options with { OneWay = _shapes[i].OneWay });
        }
    }

    private void ClearChunks(MapEntry map)
    {
        foreach (var chunk in map.ChunkList)
        {
            if (chunk.Body != PhysicsState.Null)
                state.DestroyBody(chunk.Body);
        }

        map.ChunkList.Clear();
        map.Chunks.Clear();
    }

    private static ulong Hash(in Shape shape, bool oneWay)
    {
        var hash = new HashCode();
        hash.Add(shape.Type);
        hash.Add(shape.Count);
        hash.Add(shape.InternalEdges);
        hash.Add(oneWay);
        for (var i = 0; i < shape.Count; i++)
            hash.Add(shape.Points[i]);
        return (ulong)(uint)hash.ToHashCode();
    }

    private static bool SameShape(in Shape a, in Shape b)
    {
        if (a.Type != b.Type || a.Count != b.Count || a.InternalEdges != b.InternalEdges || a.Radius != b.Radius)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a.Points[i] != b.Points[i])
                return false;
        }

        return true;
    }

    private sealed class MapEntry(Entity entity, TileMap map, Vector2 origin, FixtureOptions options, TileCollisionBuilder builder)
    {
        private GridBounds _chunkBounds;
        private int _chunkCount = -1;

        public Entity Entity { get; } = entity;

        public TileMap Map { get; } = map;

        public Vector2 Origin { get; set; } = origin;

        public FixtureOptions Options { get; } = options;

        /// <summary>Builds this map's chunks and caches the shapes of its tiles.</summary>
        public TileCollisionBuilder Builder { get; } = builder;

        public TileLayer[] Layers { get; } = CollisionLayers(map);

        public Dictionary<ChunkCoord, ChunkEntry> Chunks { get; } = new();

        /// <summary>The cached chunks in the order they were built, so rebuilding is deterministic.</summary>
        public List<ChunkEntry> ChunkList { get; } = [];

        public int Stamp { get; set; }

        /// <summary>The chunk coordinates spanned by the collision layers, refreshed when chunks are added.</summary>
        public GridBounds ChunkBounds(out bool hasChunks)
        {
            var count = 0;
            foreach (var layer in Layers)
                count += layer.Chunks.Count;
            hasChunks = count > 0;
            if (count == _chunkCount)
                return _chunkBounds;

            _chunkCount = count;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            foreach (var layer in Layers)
            {
                foreach (var coord in layer.Chunks.Keys)
                {
                    minX = Math.Min(minX, coord.X);
                    minY = Math.Min(minY, coord.Y);
                    maxX = Math.Max(maxX, coord.X);
                    maxY = Math.Max(maxY, coord.Y);
                }
            }

            _chunkBounds = new GridBounds(minX, minY, maxX, maxY);
            return _chunkBounds;
        }

        public bool HasSameLayers()
        {
            var index = 0;
            var tileLayers = Map.TileLayers;
            for (var i = 0; i < tileLayers.Count; i++)
            {
                var layer = tileLayers[i];
                if (layer.Role != LayerRole.Collision)
                    continue;
                if (index >= Layers.Length || !ReferenceEquals(Layers[index], layer))
                    return false;
                index++;
            }

            return index == Layers.Length;
        }

        private static TileLayer[] CollisionLayers(TileMap map)
        {
            var layers = new List<TileLayer>();
            foreach (var layer in map.TileLayers)
            {
                if (layer.Role == LayerRole.Collision)
                    layers.Add(layer);
            }

            return [.. layers];
        }
    }

    private sealed class ChunkEntry(ChunkCoord coord, int layerCount)
    {
        public ChunkCoord Coord { get; } = coord;

        public TileChunk?[] Sources { get; } = new TileChunk?[layerCount];

        public int[] Versions { get; } = new int[layerCount];

        public int Body { get; set; } = PhysicsState.Null;

        /// <summary>A neighbor changed, so edges shared with it must be recomputed.</summary>
        public bool Dirty { get; set; }
    }
}
