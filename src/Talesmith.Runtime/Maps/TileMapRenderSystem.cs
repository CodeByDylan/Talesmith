using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Runtime.Maps;

/// <summary>Draws the tile layers of every map entity, one cached mesh per visible chunk and texture.</summary>
/// <remarks>
/// Meshes are rebuilt when a chunk's cells, its layer's opacity, the map's tilesets or the map entity's position change; animated tiles
/// are drawn individually. Caches are kept per map entity, so one map asset can be shown several times.
/// </remarks>
[UpdateIn(SystemPhase.PreRender)]
[SystemOrder(-100)]
public sealed class TileMapRenderSystem(RenderContext render, EngineProfilers profilers) : ISystem, ISystemLifecycle
{
    private const int EvictAfterFrames = 600;

    private static readonly QueryDescription ActiveMaps = QueryDescription.With<TileMapComponent>().And<Transform>().Without<Inactive>();

    private readonly Dictionary<Entity, MapCache> _maps = new();
    private readonly List<Entity> _evictMaps = [];
    private readonly Dictionary<Texture, List<SpriteInstance>> _scratch = new();
    private readonly List<ChunkKey> _evict = [];
    private long _frame;

    public void OnStart(World world)
    {
    }

    public void OnStop(World world) => _maps.Clear();

    public void Update(in SystemContext context)
    {
        _frame++;
        var frame = render.Frame;
        var visibleChunks = 0;
        var meshesBuilt = 0;
        foreach (var archetype in context.World.Query(ActiveMaps))
        {
            var entities = archetype.Entities;
            var components = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < entities.Length; i++)
                DrawMap(entities[i], components[i], transforms[i].Position, frame, context.Time.TotalTime * 1000, ref visibleChunks, ref meshesBuilt);
        }

        if (_frame % 120 == 0)
            Evict();

        profilers.Game.Increment(RuntimeMarkers.VisibleChunks, visibleChunks);
        profilers.Game.Increment(RuntimeMarkers.ChunkMeshesBuilt, meshesBuilt);
    }

    private void DrawMap(Entity entity, TileMapComponent component, Vector2 origin, RenderFrame frame, double timeMilliseconds, ref int visibleChunks,
        ref int meshesBuilt)
    {
        var map = component.Map;
        if (!_maps.TryGetValue(entity, out var cache) || cache.Map != map)
            _maps[entity] = cache = new MapCache(map, render.Textures.Get(CellMasks.For(map.Layout)));
        if (cache.TilesetVersion != map.TilesetVersion)
            cache.Invalidate();
        cache.LastUsedFrame = _frame;

        var bounds = map.Layout.CoveringBounds(frame.VisibleBounds.Offset(-origin).Inflate(cache.Overhang));
        var (min, max) = ChunkCoord.Covering(bounds, map.ChunkShift);
        for (var layerIndex = 0; layerIndex < map.Layers.Count; layerIndex++)
        {
            if (map.Layers[layerIndex] is not TileLayer { IsVisible: true } layer || layer.Opacity <= 0)
                continue;

            var renderLayer = component.RenderLayer + layerIndex;
            for (var cy = min.Y; cy <= max.Y; cy++)
            {
                for (var cx = min.X; cx <= max.X; cx++)
                {
                    var coord = new ChunkCoord(cx, cy);
                    if (!layer.Chunks.TryGetValue(coord, out var chunk))
                        continue;

                    var key = new ChunkKey(layer, coord);
                    if (!cache.Chunks.TryGetValue(key, out var chunkCache))
                        cache.Chunks[key] = chunkCache = new ChunkCache();
                    if (chunkCache.Version != chunk.Version || chunkCache.Opacity != layer.Opacity || chunkCache.Origin != origin || !chunkCache.Built)
                    {
                        Build(map, cache, layer, chunk, chunkCache, origin);
                        meshesBuilt++;
                    }

                    chunkCache.LastUsedFrame = _frame;
                    visibleChunks++;
                    foreach (var (texture, mesh) in chunkCache.Meshes)
                        frame.DrawMesh(mesh, texture, null, renderLayer);
                    DrawAnimated(frame, chunkCache, renderLayer, layer.Opacity, timeMilliseconds);
                }
            }
        }
    }

    private void Build(TileMap map, MapCache cache, TileLayer layer, TileChunk chunk, ChunkCache chunkCache, Vector2 mapOrigin)
    {
        foreach (var list in _scratch.Values)
            list.Clear();
        chunkCache.Animated.Clear();

        var cells = chunk.EnsureDecoded();
        var origin = chunk.Coord.Origin(layer.ChunkShift);
        var size = chunk.Size;
        var tint = Color.White.WithAlpha((byte)Math.Clamp(layer.Opacity * 255, 0, 255));
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell.IsEmpty || map.FindTileset(cell.TilesetId) is not { } tileset || !tileset.Contains(cell.TileId))
                continue;

            var center = mapOrigin + map.Layout.CellToWorld(new GridCoord(origin.X + i % size, origin.Y + i / size));
            var info = tileset.Find(cell.TileId);
            if (info is { IsAnimated: true } && tileset.Texture is not null)
            {
                chunkCache.Animated.Add(new AnimatedTile(tileset, info, TileGeometry.CellTile(map, tileset, center, cell)));
                continue;
            }

            if (tileset.Texture is { } image)
            {
                Add(render.Textures.Get(image), new SpriteInstance(TileGeometry.CellTile(map, tileset, center, cell), tileset.SourceRect(cell.TileId), tint));
            }
            else
            {
                var color = info?.Color ?? TileGeometry.FallbackColor(cell.TileId);
                var source = new Rect2(0, 0, cache.Mask.Width, cache.Mask.Height);
                Add(cache.Mask, new SpriteInstance(TileGeometry.CellFill(map.Layout.CellSize, center), source, color.WithAlpha((byte)(color.A * tint.A / 255))));
            }
        }

        var meshIndex = 0;
        foreach (var (texture, instances) in _scratch)
        {
            if (instances.Count == 0)
                continue;
            if (meshIndex == chunkCache.Meshes.Count)
                chunkCache.Meshes.Add((texture, new SpriteMesh()));
            var mesh = chunkCache.Meshes[meshIndex].Mesh;
            mesh.Update(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(instances));
            chunkCache.Meshes[meshIndex++] = (texture, mesh);
        }

        chunkCache.Meshes.RemoveRange(meshIndex, chunkCache.Meshes.Count - meshIndex);
        chunkCache.Version = chunk.Version;
        chunkCache.Opacity = layer.Opacity;
        chunkCache.Origin = mapOrigin;
        chunkCache.Built = true;
    }

    private void Add(Texture texture, in SpriteInstance instance)
    {
        if (!_scratch.TryGetValue(texture, out var list))
            _scratch[texture] = list = [];
        list.Add(instance);
    }

    private void DrawAnimated(RenderFrame frame, ChunkCache chunkCache, int renderLayer, float opacity, double timeMilliseconds)
    {
        if (chunkCache.Animated.Count == 0)
            return;
        var tint = Color.White.WithAlpha((byte)Math.Clamp(opacity * 255, 0, 255));
        foreach (var tile in chunkCache.Animated)
        {
            var texture = render.Textures.Get(tile.Tileset.Texture!);
            var source = tile.Tileset.SourceRect(tile.Info.FrameAt(timeMilliseconds));
            frame.Draw(texture, null, new SpriteInstance(tile.Transform, source, tint), renderLayer);
        }
    }

    private void Evict()
    {
        _evictMaps.Clear();
        foreach (var (entity, cache) in _maps)
        {
            if (_frame - cache.LastUsedFrame > EvictAfterFrames)
                _evictMaps.Add(entity);
        }

        foreach (var entity in _evictMaps)
            _maps.Remove(entity);

        foreach (var cache in _maps.Values)
        {
            _evict.Clear();
            foreach (var (key, chunk) in cache.Chunks)
            {
                if (_frame - chunk.LastUsedFrame > EvictAfterFrames)
                    _evict.Add(key);
            }

            foreach (var key in _evict)
                cache.Chunks.Remove(key);
        }
    }

    private readonly record struct ChunkKey(TileLayer Layer, ChunkCoord Coord);

    private readonly record struct AnimatedTile(Tileset Tileset, TileInfo Info, Matrix3x2 Transform);

    private sealed class MapCache(TileMap map, Texture mask)
    {
        public TileMap Map { get; } = map;

        public Texture Mask { get; } = mask;

        public long LastUsedFrame { get; set; }

        public Dictionary<ChunkKey, ChunkCache> Chunks { get; } = new();

        /// <summary>How far tile artwork can reach beyond its cell, so chunks are not culled while their art is still on screen.</summary>
        public float Overhang { get; private set; } = MeasureOverhang(map);

        /// <summary>The <see cref="TileMap.TilesetVersion"/> the meshes were built for.</summary>
        public long TilesetVersion { get; private set; } = map.TilesetVersion;

        /// <summary>Drops every mesh after tilesets changed, since any tile may now look different.</summary>
        public void Invalidate()
        {
            Chunks.Clear();
            Overhang = MeasureOverhang(Map);
            TilesetVersion = Map.TilesetVersion;
        }

        private static float MeasureOverhang(TileMap map) => map.Tilesets.Count == 0 ? 0 : map.Tilesets.Max(t => Math.Max(t.TileWidth, t.TileHeight));
    }

    private sealed class ChunkCache
    {
        public List<(Texture Texture, SpriteMesh Mesh)> Meshes { get; } = [];

        public List<AnimatedTile> Animated { get; } = [];

        public int Version { get; set; } = -1;

        public float Opacity { get; set; } = -1;

        /// <summary>The map origin the meshes were built at.</summary>
        public Vector2 Origin { get; set; }

        public bool Built { get; set; }

        public long LastUsedFrame { get; set; }
    }
}
