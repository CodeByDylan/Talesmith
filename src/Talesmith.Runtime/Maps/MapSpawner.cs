using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Rendering;

namespace Talesmith.Runtime.Maps;

/// <summary>Turns a <see cref="TileMap"/> into entities: one for the map, whose tile layers are drawn by chunk, and one per map object.</summary>
/// <remarks>
/// Tile objects become sprites sorted by their bottom edge; polygons become <see cref="TriggerArea"/>s; every object keeps its data in a
/// <see cref="MapObjectComponent"/>.
/// </remarks>
public sealed class MapSpawner(TextureCache textures)
{
    /// <summary>Creates the map's entities and returns the map entity.</summary>
    /// <param name="origin">The world position of the center of cell (0, 0); object entities are placed relative to it.</param>
    /// <param name="renderLayer">The render layer of the map's first tile layer, so maps can be stacked above or below each other.</param>
    public Entity Spawn(World world, TileMap map, Vector2 origin = default, int renderLayer = RenderLayers.Terrain)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        var mapEntity = world.Create(new TileMapComponent(map) { RenderLayer = renderLayer }, new Transform(origin), new Name(map.Path));
        foreach (var layer in map.ObjectLayers)
        {
            foreach (var mapObject in layer.Objects)
                SpawnObject(world, map, origin, layer, mapObject, mapEntity);
        }

        return mapEntity;
    }

    /// <summary>Shows a map on an existing entity, replacing any map it showed, and spawns the map's objects as its children.</summary>
    /// <remarks>Objects follow the map entity through their <see cref="Parent"/> and <see cref="LocalTransform"/>.</remarks>
    public void Attach(World world, Entity mapEntity, TileMap map, int renderLayer = RenderLayers.Terrain)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(map);
        Detach(world, mapEntity);
        world.Set(mapEntity, new TileMapComponent(map) { RenderLayer = renderLayer });
        if (!world.Has<Transform>(mapEntity))
            world.Set(mapEntity, new Transform());
        var mapTransform = world.Get<Transform>(mapEntity);
        foreach (var layer in map.ObjectLayers)
        {
            foreach (var mapObject in layer.Objects)
            {
                var entity = SpawnObject(world, map, Vector2.Zero, layer, mapObject, mapEntity);
                var local = new LocalTransform(world.Get<Transform>(entity));
                world.Set(entity, new Parent(mapEntity));
                world.Set(entity, local);
                world.Set(entity, TransformHierarchy.Compose(mapTransform, local));
            }
        }
    }

    /// <summary>Stops showing a map on an entity and destroys the objects spawned for it.</summary>
    public static void Detach(World world, Entity mapEntity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!world.IsAlive(mapEntity) || !world.Remove<TileMapComponent>(mapEntity))
            return;
        var objects = new List<Entity>();
        foreach (var archetype in world.Query<MapObjectComponent>())
        {
            var components = archetype.GetSpan<MapObjectComponent>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i].Map == mapEntity)
                    objects.Add(archetype.Entities[i]);
            }
        }

        foreach (var entity in objects)
            world.Destroy(entity);
    }

    private Entity SpawnObject(World world, TileMap map, Vector2 origin, ObjectLayer layer, MapObject mapObject, Entity mapEntity)
    {
        var entity = world.Create(new Transform(origin + mapObject.Position), new Name(mapObject.Name), new MapObjectComponent(mapObject, layer, mapEntity));
        switch (mapObject.Shape)
        {
            case MapObjectShape.Tile when CreateSprite(map, layer, mapObject.Tile) is { } sprite:
                world.Set(entity, sprite);
                ref var transform = ref world.Get<Transform>(entity);
                transform.Rotation = MathHelper.ToRadians(mapObject.Tile.Rotation * map.RotationStepDegrees);
                break;
            case MapObjectShape.Polygon when mapObject.Polygon.Count >= 3:
                world.Set(entity, new TriggerArea(mapObject.Name, mapObject.Polygon, mapObject.Properties));
                break;
        }

        return entity;
    }

    private Sprite? CreateSprite(TileMap map, ObjectLayer layer, TileCell tile)
    {
        if (map.FindTileset(tile.TilesetId) is not { } tileset || !tileset.Contains(tile.TileId))
            return null;

        var opacity = (byte)Math.Clamp(layer.Opacity * 255, 0, 255);
        var size = new Vector2(tileset.TileWidth, tileset.TileHeight);
        if (tileset.Texture is { } image)
        {
            return new Sprite(textures.Get(image))
            {
                Source = tileset.SourceRect(tile.TileId),
                Size = size,
                Tint = Color.White.WithAlpha(opacity),
                FlipX = tile.FlipX,
                SortByY = true,
                Visible = layer.IsVisible
            };
        }

        var color = tileset.Find(tile.TileId)?.Color ?? TileGeometry.FallbackColor(tile.TileId);
        return new Sprite(textures.Get(CellMasks.For(map.Layout)))
        {
            Size = map.Layout.CellSize,
            Tint = color.WithAlpha((byte)(color.A * opacity / 255)),
            SortByY = true,
            Visible = layer.IsVisible
        };
    }
}
