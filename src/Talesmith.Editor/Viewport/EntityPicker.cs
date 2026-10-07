using System.Numerics;
using System.Runtime.CompilerServices;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Plugins;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;

namespace Talesmith.Editor.Viewport;

/// <summary>What gives an entity its extent in the viewport.</summary>
public enum EntityVisualKind
{
    Sprite,
    TileMap,

    /// <summary>An icon of fixed screen size, for entities without sprites or tile maps.</summary>
    Icon
}

/// <summary>The world-space extent of a runtime entity, attributed to the document entity it belongs to.</summary>
/// <param name="Corners">The outline as four corners, clockwise from the top left before rotation.</param>
/// <param name="Bounds">The axis-aligned bounds of the corners.</param>
/// <param name="Layer">The render layer, which decides which entity is on top when picking.</param>
public sealed record EntityVisual(Guid DocumentId, Entity Entity, EntityVisualKind Kind, Vector2[] Corners, Rect2 Bounds, int Layer, EntityIcon? Icon)
{
    public Vector2 Position { get; init; }

    public bool Contains(Vector2 point)
    {
        if (!Bounds.Contains(point))
            return false;
        var sign = 0f;
        for (var i = 0; i < 4; i++)
        {
            var a = Corners[i];
            var b = Corners[(i + 1) % 4];
            var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (cross != 0)
            {
                if (sign != 0 && Math.Sign(cross) != Math.Sign(sign))
                    return false;
                sign = cross;
            }
        }

        return true;
    }
}

/// <summary>Finds entities in the viewport: their outlines, what is under the pointer and what a marquee covers.</summary>
public sealed class EntityPicker
{
    /// <summary>The size of entity icons in screen pixels.</summary>
    public const double IconSize = 26;

    private static readonly ConditionalWeakTable<TileMap, MapBounds> MapCache = new();
    private readonly IEditWorld _editWorld;
    private readonly ISceneDocumentService _documents;
    private readonly IEntityIconProvider[] _icons;
    private readonly EditorPluginGuard _plugins;
    private IReadOnlyList<EntityVisual> _visuals = [];
    private (World? World, SceneDocumentModel? Model, long Frame, int Changes, float Zoom) _visualsKey;
    private int _changes;

    public EntityPicker(IEditWorld editWorld, ISceneDocumentService documents, IEnumerable<IEntityIconProvider> iconProviders, EditorPluginGuard plugins)
    {
        _editWorld = editWorld;
        _documents = documents;
        _plugins = plugins;
        _icons = [.. iconProviders.OrderByDescending(p => plugins.Run(p, "show entity icons", () => p.Priority, 0))];
        editWorld.Changed += (_, _) => _changes++;
    }

    /// <summary>The visuals of every active entity that belongs to the open scene.</summary>
    /// <param name="zoom">The camera zoom, which sets the world size of icons.</param>
    /// <remarks>Collected again only after the edit game ticked, the edit world changed or the zoom changed.</remarks>
    public IReadOnlyList<EntityVisual> Collect(float zoom)
    {
        var key = (_editWorld.World, _documents.Active, _editWorld.Game?.FrameCount ?? 0, _changes, zoom);
        if (key != _visualsKey)
        {
            _visuals = CollectNow(zoom);
            _visualsKey = key;
        }

        return _visuals;
    }

    private List<EntityVisual> CollectNow(float zoom)
    {
        var result = new List<EntityVisual>();
        if (_editWorld.World is not { } world || _documents.Active is not { } model)
            return result;
        var iconWorld = (float)(IconSize / Math.Max(zoom, 1e-4f));
        foreach (var archetype in world.Query(QueryDescription.With<Transform>().Without<Inactive>()))
        {
            var transforms = archetype.GetSpan<Transform>();
            var hasSprite = archetype.Has<Sprite>();
            var hasMap = archetype.Has<TileMapComponent>();
            var hasId = archetype.Has<SceneEntityId>();
            var sprites = hasSprite ? archetype.GetSpan<Sprite>() : default;
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                ref readonly var transform = ref transforms[i];
                if (hasSprite && sprites[i].Visible && sprites[i].Size != Vector2.Zero)
                {
                    if (_editWorld.TryGetDocumentId(entity, out var id))
                        result.Add(SpriteVisual(id, entity, transform, sprites[i]));
                }
                else if (hasMap)
                {
                    if (_editWorld.TryGetDocumentId(entity, out var id) && MapVisual(id, entity, transform, world.Get<TileMapComponent>(entity)) is { } visual)
                        result.Add(visual);
                }
                else if (hasId && model.Contains(world.Get<SceneEntityId>(entity).Value) && IconFor(world, entity) is { } icon)
                {
                    var id = world.Get<SceneEntityId>(entity).Value;
                    var half = iconWorld / 2;
                    var rect = new Rect2(transform.Position.X - half, transform.Position.Y - half, iconWorld, iconWorld);
                    result.Add(new EntityVisual(id, entity, EntityVisualKind.Icon, Corners(rect), rect, int.MaxValue, icon) { Position = transform.Position });
                }
            }
        }

        return result;
    }

    /// <summary>The document entity under a world point: icons first, then sprites from the highest layer, then tile maps.</summary>
    public Guid? HitTest(Vector2 world, float zoom)
    {
        var model = _documents.Active;
        EntityVisual? best = null;
        foreach (var visual in Collect(zoom))
        {
            if (!visual.Contains(world) || model?.Find(visual.DocumentId) is not { Editor.Locked: false })
                continue;
            if (best is null || Rank(visual) > Rank(best) || Rank(visual) == Rank(best) && Area(visual) < Area(best))
                best = visual;
        }

        return best?.DocumentId;
    }

    /// <summary>The document entities whose visuals intersect a world rectangle, excluding locked ones and tile maps that are not inside it.</summary>
    public IReadOnlyList<Guid> Query(Rect2 area, float zoom)
    {
        var model = _documents.Active;
        var found = new List<Guid>();
        foreach (var visual in Collect(zoom))
        {
            if (model?.Find(visual.DocumentId) is not { Editor.Locked: false })
                continue;
            var hit = visual.Kind == EntityVisualKind.TileMap ? Contains(area, visual.Bounds) : area.Intersects(visual.Bounds);
            if (hit && !found.Contains(visual.DocumentId))
                found.Add(visual.DocumentId);
        }

        return found;
    }

    /// <summary>The combined bounds of document entities, including their prefab and map children, or null when none has a visual.</summary>
    public Rect2? GetBounds(IEnumerable<Guid> documentIds, float zoom)
    {
        var ids = documentIds.ToHashSet();
        Rect2? bounds = null;
        foreach (var visual in Collect(zoom))
        {
            if (ids.Contains(visual.DocumentId))
                bounds = bounds is { } b ? Union(b, visual.Bounds) : visual.Bounds;
        }

        return bounds;
    }

    /// <summary>The bounds of everything in the scene.</summary>
    public Rect2? GetSceneBounds(float zoom)
    {
        Rect2? bounds = null;
        foreach (var visual in Collect(zoom))
            bounds = bounds is { } b ? Union(b, visual.Bounds) : visual.Bounds;
        return bounds;
    }

    public static Rect2 Union(Rect2 a, Rect2 b)
    {
        var minX = Math.Min(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y);
        var maxX = Math.Max(a.X + a.Width, b.X + b.Width);
        var maxY = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool Contains(Rect2 outer, Rect2 inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static int Rank(EntityVisual visual) => visual.Kind switch
    {
        EntityVisualKind.Icon => 2,
        EntityVisualKind.Sprite => 1,
        _ => 0
    } * 1_000_000 + Math.Clamp(visual.Layer, -999_999, 999_999);

    private static float Area(EntityVisual visual) => visual.Bounds.Width * visual.Bounds.Height;

    private EntityIcon? IconFor(World world, Entity entity)
    {
        foreach (var provider in _icons)
        {
            if (_plugins.IsFaulted(provider))
                continue;
            try
            {
                if (provider.GetIcon(world, entity) is { } icon)
                    return icon;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && _plugins.Isolate(provider, "show entity icons", ex))
            {
            }
        }

        return null;
    }

    private static EntityVisual SpriteVisual(Guid id, Entity entity, in Transform transform, in Sprite sprite)
    {
        var size = sprite.Size * transform.Scale;
        var min = -sprite.Origin * size;
        var local = new[] { min, min + new Vector2(size.X, 0), min + size, min + new Vector2(0, size.Y) };
        var rotation = Matrix3x2.CreateRotation(transform.Rotation);
        var corners = new Vector2[4];
        for (var i = 0; i < 4; i++)
            corners[i] = Vector2.Transform(local[i], rotation) + transform.Position;
        return new EntityVisual(id, entity, EntityVisualKind.Sprite, corners, BoundsOf(corners), sprite.Layer, null) { Position = transform.Position };
    }

    private static EntityVisual? MapVisual(Guid id, Entity entity, in Transform transform, TileMapComponent component)
    {
        var map = component.Map;
        var cached = MapCache.GetValue(map, static _ => new MapBounds());
        if (cached.Version != map.Version || cached.Layers != map.TileLayers.Count)
        {
            cached.Bounds = ComputeBounds(map, cached.Chunks);
            cached.Version = map.Version;
            cached.Layers = map.TileLayers.Count;
        }

        if (cached.Bounds is not { } bounds)
            return null;
        var rect = new Rect2(bounds.X + transform.Position.X, bounds.Y + transform.Position.Y, bounds.Width, bounds.Height);
        return new EntityVisual(id, entity, EntityVisualKind.TileMap, Corners(rect), rect, component.RenderLayer, null) { Position = transform.Position };
    }

    /// <summary>The bounds of the map's cells, recomputing only chunks that changed; chunks never decoded count whole.</summary>
    private static Rect2? ComputeBounds(TileMap map, Dictionary<TileChunk, ChunkBounds> chunks)
    {
        Rect2? bounds = null;
        var seen = new HashSet<TileChunk>(ReferenceEqualityComparer.Instance);
        foreach (var layer in map.TileLayers)
        {
            foreach (var chunk in layer.ChunkValues)
            {
                seen.Add(chunk);
                var decoded = chunk.IsDecoded;
                if (!chunks.TryGetValue(chunk, out var entry) || entry.Version != chunk.Version || entry.Decoded != decoded)
                    chunks[chunk] = entry = new ChunkBounds(chunk.Version, decoded, BoundsOf(map, layer, chunk, decoded));
                if (entry.Bounds is { } chunkBounds)
                    bounds = bounds is { } b ? Union(b, chunkBounds) : chunkBounds;
            }
        }

        foreach (var stale in chunks.Keys.Where(c => !seen.Contains(c)).ToList())
            chunks.Remove(stale);
        return bounds;
    }

    private static Rect2? BoundsOf(TileMap map, TileLayer layer, TileChunk chunk, bool decoded)
    {
        var size = chunk.Size;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        if (decoded && chunk.Cells is { } cells)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    if (cells[y * size + x].IsEmpty)
                        continue;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (minX == int.MaxValue)
                return null;
        }
        else
        {
            (minX, minY, maxX, maxY) = (0, 0, size - 1, size - 1);
        }

        var origin = chunk.Coord.Origin(layer.ChunkShift);
        Rect2? bounds = null;
        foreach (var (x, y) in new[] { (minX, minY), (maxX, minY), (minX, maxY), (maxX, maxY) })
        {
            var cell = map.Layout.CellBounds(new GridCoord(origin.X + x, origin.Y + y));
            bounds = bounds is { } b ? Union(b, cell) : cell;
        }

        return bounds;
    }

    private readonly record struct ChunkBounds(int Version, bool Decoded, Rect2? Bounds);

    private static Vector2[] Corners(Rect2 rect) =>
        [new(rect.X, rect.Y), new(rect.X + rect.Width, rect.Y), new(rect.X + rect.Width, rect.Y + rect.Height), new(rect.X, rect.Y + rect.Height)];

    private static Rect2 BoundsOf(Vector2[] corners)
    {
        var min = Vector2.Min(Vector2.Min(corners[0], corners[1]), Vector2.Min(corners[2], corners[3]));
        var max = Vector2.Max(Vector2.Max(corners[0], corners[1]), Vector2.Max(corners[2], corners[3]));
        return new Rect2(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    }

    private sealed class MapBounds
    {
        public long Version { get; set; } = -1;

        public int Layers { get; set; }

        public Rect2? Bounds { get; set; }

        public Dictionary<TileChunk, ChunkBounds> Chunks { get; } = new(ReferenceEqualityComparer.Instance);
    }
}
