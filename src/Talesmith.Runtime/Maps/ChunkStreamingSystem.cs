using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Runtime.Maps;

/// <summary>Keeps decoded tile chunks near the camera and compressed ones elsewhere, so huge maps use little memory.</summary>
/// <remarks>
/// Chunks within <see cref="PrefetchChunks"/> of the visible area are decoded on background threads before they scroll into view;
/// decoded chunks farther than <see cref="EvictChunks"/> are returned to their compressed form. Visible chunks that are not decoded yet
/// are decoded on demand by <see cref="TileMapRenderSystem"/>, so nothing ever pops in. Distances are measured from where each map entity
/// places its map, and a map shown by several entities keeps every chunk that any of them needs.
/// </remarks>
[UpdateIn(SystemPhase.LateUpdate)]
[ExecuteIn(ExecutionModes.All)]
[UpdateAfter(typeof(Systems.CameraSystem))]
public sealed class ChunkStreamingSystem(RenderContext render, EngineProfilers profilers, ILogger<ChunkStreamingSystem> logger) : ISystem
{
    public const int PrefetchChunks = 1;
    public const int EvictChunks = 3;
    private const int EvictIntervalFrames = 30;

    private static readonly QueryDescription ActiveMaps = QueryDescription.With<TileMapComponent>().And<Transform>().Without<Inactive>();

    private readonly ConcurrentDictionary<TileChunk, byte> _decoding = new(ReferenceEqualityComparer.Instance);
    private readonly int _maxConcurrentDecodes = Math.Max(1, Environment.ProcessorCount / 2);
    private readonly Dictionary<TileMap, List<(ChunkCoord Min, ChunkCoord Max)>> _visible = new(ReferenceEqualityComparer.Instance);
    private readonly List<TileMap> _unused = [];
    private long _frame;

    public void Update(in SystemContext context)
    {
        _frame++;
        var visible = render.VisibleBounds;
        foreach (var ranges in _visible.Values)
            ranges.Clear();
        foreach (var archetype in context.World.Query(ActiveMaps))
        {
            var components = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < components.Length; i++)
            {
                var map = components[i].Map;
                if (!_visible.TryGetValue(map, out var ranges))
                    _visible[map] = ranges = [];
                ranges.Add(ChunkCoord.Covering(map.Layout.CoveringBounds(visible.Offset(-transforms[i].Position)), map.ChunkShift));
            }
        }

        var decoded = 0;
        _unused.Clear();
        foreach (var (map, ranges) in _visible)
        {
            if (ranges.Count == 0)
                _unused.Add(map);
            else
                decoded += Stream(map, ranges);
        }

        foreach (var map in _unused)
            _visible.Remove(map);
        profilers.Game.Set(RuntimeMarkers.DecodedChunks, decoded);
    }

    /// <param name="ranges">The visible chunks of each entity showing the map, which may show the same map at several places.</param>
    private int Stream(TileMap map, List<(ChunkCoord Min, ChunkCoord Max)> ranges)
    {
        var evict = _frame % EvictIntervalFrames == 0;
        var decoded = 0;
        var layers = map.TileLayers;
        for (var l = 0; l < layers.Count; l++)
        {
            foreach (var chunk in layers[l].ChunkValues)
            {
                var coord = chunk.Coord;
                var distance = int.MaxValue;
                foreach (var (min, max) in ranges)
                    distance = Math.Min(distance, Math.Max(Outside(coord.X, min.X, max.X), Outside(coord.Y, min.Y, max.Y)));
                if (chunk.IsDecoded)
                {
                    if (evict && distance > EvictChunks && !_decoding.ContainsKey(chunk) && chunk.Evict())
                        continue;
                    decoded++;
                }
                else if (distance > 0 && distance <= PrefetchChunks && _decoding.Count < _maxConcurrentDecodes && _decoding.TryAdd(chunk, 0))
                {
                    StartDecode(chunk);
                }
            }
        }

        return decoded;
    }

    private void StartDecode(TileChunk chunk) => _ = Task.Run(() => Decode(chunk));

    private void Decode(TileChunk chunk)
    {
        try
        {
            chunk.EnsureDecoded();
        }
        catch (Assets.AssetException ex)
        {
            logger.ChunkDecodeFailed(ex, chunk.Coord.ToString());
        }
        finally
        {
            _decoding.TryRemove(chunk, out _);
        }
    }

    private static int Outside(int value, int min, int max) => value < min ? min - value : value > max ? value - max : 0;
}
