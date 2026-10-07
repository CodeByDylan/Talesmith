using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Rendering;

namespace Talesmith.Runtime.Tests;

public sealed class MapSpawnerTests : IDisposable
{
    private readonly TextureCache _textures = new(new NullRenderer(), new GameSettings());

    public void Dispose() => _textures.Dispose();

    [Fact]
    public void MapsOnDifferentGridsShareOneWorld()
    {
        var world = new World();
        var spawner = new MapSpawner(_textures);
        var hex = Map("hex.hexy", new HexLayout(pointyTop: true, 128, 148), new GridCoord(1, 1));
        var square = Map("square.hexy", new SquareLayout(64, 64), new GridCoord(2, 3));

        var hexEntity = spawner.Spawn(world, hex);
        var squareEntity = spawner.Spawn(world, square, new Vector2(1000, -500), RenderLayers.Terrain + 10);

        Assert.Equal(2, world.Query<TileMapComponent>().Count);
        Assert.Equal(RenderLayers.Terrain, world.Get<TileMapComponent>(hexEntity).RenderLayer);
        Assert.Equal(RenderLayers.Terrain + 10, world.Get<TileMapComponent>(squareEntity).RenderLayer);
        Assert.Equal(Vector2.Zero, world.Get<Transform>(hexEntity).Position);
        Assert.Equal(new Vector2(1000, -500), world.Get<Transform>(squareEntity).Position);
    }

    [Fact]
    public void ObjectsArePlacedRelativeToTheMapOrigin()
    {
        var world = new World();
        var map = Map("square.hexy", new SquareLayout(64, 64), new GridCoord(2, 3));

        var mapEntity = new MapSpawner(_textures).Spawn(world, map, new Vector2(1000, -500));

        var (transform, component) = Single(world);
        Assert.Equal(mapEntity, component.Map);
        Assert.Equal(new Vector2(128, 192), component.Object.Position);
        Assert.Equal(new Vector2(1128, -308), transform.Position);
    }

    [Fact]
    public void SquareMapsRotateTilesInQuarterTurns()
    {
        Assert.Equal(90, Map("square.hexy", new SquareLayout(64, 64), GridCoord.Zero).RotationStepDegrees);
        Assert.Equal(60, Map("hex.hexy", new HexLayout(pointyTop: false, 148, 128), GridCoord.Zero).RotationStepDegrees);
    }

    private static TileMap Map(string path, IGridLayout layout, GridCoord spawn)
    {
        var point = new MapObject(1, "Spawn", "spawn", MapObjectShape.Point, layout.CellToWorld(spawn), spawn, [], TileCell.Empty, PropertySet.Empty);
        var layers = new MapLayer[] { new TileLayer("Ground", 5), new ObjectLayer("Objects", [point]) };
        return new TileMap(path, layout, 5, [], layers, PropertySet.Empty);
    }

    private static (Transform Transform, MapObjectComponent Object) Single(World world)
    {
        foreach (var archetype in world.Query<MapObjectComponent, Transform>())
            return (archetype.GetSpan<Transform>()[0], archetype.GetSpan<MapObjectComponent>()[0]);
        throw new InvalidOperationException("No map object was spawned.");
    }
}
