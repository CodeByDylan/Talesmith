using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets.Maps;
using Talesmith.Grids;
using Talesmith.Physics.Geometry;
using Talesmith.Physics.Tiles;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Tests;

public sealed class TileCollisionTests
{
    private const int Solid = 0;
    private const int Slope = 1;
    private const int Platform = 2;
    private const int Corner = 3;

    [Fact]
    public void AFloorOfSquareCellsBecomesOneRectangle()
    {
        var map = SquareMap(Row(0, 9, 3), Row(0, 9, 4));

        var shapes = Build(map, new ChunkCoord(0, 0));

        var shape = Assert.Single(shapes).Shape;
        Assert.Equal(new Aabb(new Vector2(-16, 80), new Vector2(304, 144)), shape.ComputeAabb());
        Assert.Equal(0, shape.InternalEdges);
    }

    [Fact]
    public void EdgesSharedWithSolidNeighborsAreInternal()
    {
        // An L: a floor and a column standing on its left end.
        var map = SquareMap(Row(0, 5, 5), [new(0, 3), new(0, 4)]);

        var shapes = Build(map, new ChunkCoord(0, 0));

        // Rows are merged first, so the column takes the floor's first cell and the floor continues to its right.
        Assert.Equal(2, shapes.Count);
        var column = shapes.Single(s => s.Shape.ComputeAabb().Min.X < 0).Shape;
        var floor = shapes.Single(s => s.Shape.ComputeAabb().Min.X > 0).Shape;
        Assert.True(floor.IsInternal(3), "The floor's left edge touches the column.");
        Assert.Equal(0b1000, floor.InternalEdges);
        Assert.Equal(0, column.InternalEdges);
    }

    [Fact]
    public void ShapesEnclosedOnEverySideAreLeftOut()
    {
        var cells = new List<GridCoord>();
        for (var y = -2; y < 34; y++)
            cells.AddRange(Row(-2, 33, y));
        var map = SquareMap([.. cells]);

        Assert.Empty(Build(map, new ChunkCoord(0, 0)));
    }

    [Fact]
    public void HexCellsBecomeHexagonsAndInteriorCellsAreLeftOut()
    {
        var map = HexMap(Disc(GridCoord.Zero, 2));

        var shapes = Build(map, new ChunkCoord(0, 0)).Concat(Build(map, new ChunkCoord(-1, 0))).Concat(Build(map, new ChunkCoord(0, -1)))
            .Concat(Build(map, new ChunkCoord(-1, -1))).ToList();

        Assert.Equal(12, shapes.Count);
        Assert.All(shapes, s => Assert.Equal(6, s.Shape.Count));
        Assert.All(shapes, s => Assert.NotEqual((1 << 6) - 1, s.Shape.InternalEdges));
        Assert.Contains(shapes, s => s.Shape.InternalEdges != 0);
    }

    [Fact]
    public void TileCollisionPolygonsReplaceTheFullCellAndFollowRotationAndFlip()
    {
        var map = SquareMap([]);
        var layer = (TileLayer)map.Layers[0];
        layer.SetCell(new GridCoord(1, 1), new TileCell(1, Slope));
        layer.SetCell(new GridCoord(3, 1), new TileCell(1, Slope, flipX: true));
        layer.SetCell(new GridCoord(5, 1), new TileCell(1, Slope, rotation: 1));

        var shapes = Build(map, new ChunkCoord(0, 0)).Select(s => s.Shape).OrderBy(s => s.ComputeAabb().Min.X).ToList();

        Assert.Equal(3, shapes.Count);
        Assert.All(shapes, s => Assert.Equal(3, s.Count));
        // The slope rises to the right: its highest point is the top right corner of the cell.
        Assert.Contains(new Vector2(48, 16), Points(shapes[0]));
        Assert.Contains(new Vector2(80, 16), Points(shapes[1]));
        // A quarter turn clockwise moves the top right corner to the bottom right.
        Assert.Contains(new Vector2(176, 48), Points(shapes[2]));
    }

    [Fact]
    public void ConcaveTileCollisionIsSplitIntoConvexPieces()
    {
        var map = SquareMap([]);
        ((TileLayer)map.Layers[0]).SetCell(new GridCoord(0, 0), new TileCell(1, Corner));

        var shapes = Build(map, new ChunkCoord(0, 0));

        Assert.InRange(shapes.Count, 2, 3);
        Assert.Equal(16 * 32 + 16 * 16, shapes.Sum(s => s.Shape.ComputeMass(1).Mass), 1);
    }

    [Fact]
    public void OneWayTilesBecomePlatformsMergedAlongRows()
    {
        var map = SquareMap([]);
        var layer = (TileLayer)map.Layers[0];
        foreach (var cell in Row(2, 6, 3))
            layer.SetCell(cell, new TileCell(1, Platform));

        var shape = Assert.Single(Build(map, new ChunkCoord(0, 0)));

        Assert.True(shape.OneWay);
        Assert.Equal(160, shape.Shape.ComputeAabb().Extents.X * 2, 2);
    }

    [Fact]
    public void BodiesLandOnSquareTileFloors()
    {
        var test = new PhysicsTestWorld();
        test.World.Create(new TileMapComponent(SquareMap(Row(-10, 10, 0))), new Transform(Vector2.Zero));
        var box = test.Dynamic(new Vector2(0, -100), Collider2D.Box(new Vector2(20, 20)));

        test.Step(90);

        Assert.InRange(test.Position(box).Y, -27, -25.5f);
    }

    [Fact]
    public void BodiesLandOnHexTileFloors()
    {
        var test = new PhysicsTestWorld();
        test.World.Create(new TileMapComponent(HexMap(Row(-5, 5, 0))), new Transform(new Vector2(500, 300)));
        // Wide enough to rest on the pointed tops of two neighboring cells.
        var plank = test.Dynamic(new Vector2(532, 0), Collider2D.Box(new Vector2(100, 20)));

        test.Step(120);

        Assert.InRange(test.Position(plank).Y, 300 - 37 - 11, 300 - 37 - 9);
        Assert.InRange(test.World.Get<Transform>(plank).Rotation, -0.01f, 0.01f);
    }

    [Fact]
    public void BodiesSlideAlongMergedFloorsWithoutCatching()
    {
        var test = new PhysicsTestWorld();
        var map = SquareMap(Row(-40, 40, 0));
        test.World.Create(new TileMapComponent(map), new Transform(Vector2.Zero));
        var box = test.Dynamic(new Vector2(-1000, -26), Collider2D.Box(new Vector2(20, 20)) with { Friction = 0 },
            new Rigidbody2D { Velocity = new Vector2(600, 0), FixedRotation = true });

        test.Step(120);

        Assert.InRange(test.Body(box).Velocity.X, 599, 601);
    }

    [Fact]
    public void ChangingCellsRebuildsTheirChunk()
    {
        var test = new PhysicsTestWorld();
        var map = SquareMap(Row(-10, 10, 0));
        test.World.Create(new TileMapComponent(map), new Transform(Vector2.Zero));
        var box = test.Dynamic(new Vector2(0, -26), Collider2D.Box(new Vector2(20, 20)));
        test.Step(30);
        Assert.InRange(test.Position(box).Y, -27, -25.5f);

        var layer = (TileLayer)map.Layers[0];
        foreach (var cell in Row(-2, 2, 0))
            layer.SetCell(cell, TileCell.Empty);
        test.Step(30);

        Assert.True(test.Position(box).Y > 0, "The box falls through the hole.");
    }

    [Fact]
    public void ChangingAnUnrelatedCellKeepsExistingContacts()
    {
        var test = new PhysicsTestWorld();
        var map = SquareMap(Row(-10, 10, 0), [new(10, -5)]);
        test.World.Create(new TileMapComponent(map), new Transform(Vector2.Zero));
        test.Dynamic(new Vector2(0, -26), Collider2D.Box(new Vector2(20, 20)), new Rigidbody2D { CanSleep = false });
        test.Step(30);
        var exits = 0;
        test.Events.Subscribe((ref CollisionExited _) => exits++);

        ((TileLayer)map.Layers[0]).SetCell(new GridCoord(10, -5), TileCell.Empty);
        test.Step(5);

        Assert.Equal(0, exits);
    }

    [Fact]
    public void ChunksAreBuiltOnlyNearBodies()
    {
        var test = new PhysicsTestWorld();
        var cells = new List<GridCoord>();
        for (var chunk = 0; chunk < 20; chunk++)
            cells.AddRange(Row(chunk * 32, chunk * 32 + 31, 0));
        test.World.Create(new TileMapComponent(SquareMap([.. cells])), new Transform(Vector2.Zero));
        test.Dynamic(new Vector2(500, -30), Collider2D.Box(new Vector2(20, 20)));

        test.Step(5);

        Assert.InRange(test.Physics.BodyCount, 2, 4);
    }

    [Fact]
    public void RaysBuildChunksAndReportTheCellTheyHit()
    {
        var test = new PhysicsTestWorld();
        var map = SquareMap(Row(-10, 10, 4));
        var mapEntity = test.World.Create(new TileMapComponent(map), new Transform(new Vector2(1000, 1000)));
        test.Step();

        Assert.True(test.Physics.RayCast(new Vector2(1000 + 3 * 32, 900), Vector2.UnitY, 500, out var hit));

        Assert.Equal(mapEntity, hit.Entity);
        Assert.Equal(1000 + 4 * 32 - 16, hit.Point.Y, 2);
        Assert.True(test.Physics.TryGetHitCell(hit, out var cell));
        Assert.Equal(new GridCoord(3, 4), cell);
    }

    [Fact]
    public void MovingTheMapMovesItsCollision()
    {
        var test = new PhysicsTestWorld();
        var mapEntity = test.World.Create(new TileMapComponent(SquareMap(Row(-10, 10, 0))), new Transform(Vector2.Zero));
        test.Step();

        test.World.Get<Transform>(mapEntity).Position = new Vector2(0, 200);
        test.Step();

        Assert.True(test.Physics.RayCast(new Vector2(0, 0), Vector2.UnitY, 500, out var hit));
        Assert.Equal(184, hit.Point.Y, 2);
    }

    [Fact]
    public void DisabledTileMapCollidersDoNotCollide()
    {
        var test = new PhysicsTestWorld();
        test.World.Create(new TileMapComponent(SquareMap(Row(-10, 10, 0))), new Transform(Vector2.Zero), new TileMapCollider2D { Enabled = false });
        var box = test.Dynamic(new Vector2(0, -100), Collider2D.Box(new Vector2(20, 20)));

        test.Step(60);

        Assert.True(test.Position(box).Y > 100);
    }

    [Fact]
    public void CharactersExactlyOnSquareTileFloorsWalkAndJump()
    {
        var test = new PhysicsTestWorld();
        test.World.Create(new TileMapComponent(SquareMap(Row(-10, 10, 0))), new Transform(Vector2.Zero));
        var hero = test.World.Create(new Transform(new Vector2(0, -36)), Collider2D.Box(new Vector2(20, 40)), new CharacterController2D());
        test.Physics.BeginStep();

        Assert.Equal(CharacterCollisions.Below, test.Physics.MoveCharacter(hero, new Vector2(5, 2)));
        Assert.InRange(test.Position(hero).X, 4.9f, 5.1f);
        test.Physics.MoveCharacter(hero, new Vector2(0, -10));
        Assert.InRange(test.Position(hero).Y, -47, -45);
    }

    [Fact]
    public void CharactersExactlyOnHexTileFloorsWalk()
    {
        var test = new PhysicsTestWorld();
        test.World.Create(new TileMapComponent(HexMap(Row(-5, 5, 0))), new Transform(new Vector2(500, 300)));
        var hero = test.World.Create(new Transform(new Vector2(500, 300 - 37 - 20)), Collider2D.Box(new Vector2(20, 40)), new CharacterController2D());
        test.Physics.BeginStep();

        Assert.Equal(CharacterCollisions.Below, test.Physics.MoveCharacter(hero, new Vector2(5, 2)));
        Assert.InRange(test.Position(hero).X, 504, 506);
    }

    private static List<TileShape> Build(TileMap map, ChunkCoord coord)
    {
        var shapes = new List<TileShape>();
        var layers = map.TileLayers.Where(l => l.Role == LayerRole.Collision).ToList();
        new TileCollisionBuilder(NullLogger.Instance).Build(map, layers, coord, shapes);
        return shapes;
    }

    private static Vector2[] Points(in Shape shape)
    {
        var points = new Vector2[shape.Count];
        for (var i = 0; i < shape.Count; i++)
            points[i] = new Vector2(MathF.Round(shape.Points[i].X, 3), MathF.Round(shape.Points[i].Y, 3));
        return points;
    }

    private static GridCoord[] Row(int fromX, int toX, int y) => Enumerable.Range(fromX, toX - fromX + 1).Select(x => new GridCoord(x, y)).ToArray();

    private static GridCoord[] Disc(GridCoord center, int radius)
    {
        var layout = new HexLayout(pointyTop: true, 64, 74);
        var cells = new List<GridCoord>();
        for (var q = -radius; q <= radius; q++)
        {
            for (var r = -radius; r <= radius; r++)
            {
                var cell = new GridCoord(center.X + q, center.Y + r);
                if (layout.Distance(cell, center) <= radius)
                    cells.Add(cell);
            }
        }

        return [.. cells];
    }

    private static TileMap SquareMap(params GridCoord[][] solids) => Map(new SquareLayout(32, 32), solids.SelectMany(s => s));

    private static TileMap HexMap(GridCoord[] solid) => Map(new HexLayout(pointyTop: true, 64, 74), solid);

    private static TileMap Map(IGridLayout layout, IEnumerable<GridCoord> solid)
    {
        var tiles = new Dictionary<int, TileInfo>
        {
            [Slope] = new(Slope, "slope", null, [], PropertySet.Empty) { Collision = [[new(-16, 16), new(16, 16), new(16, -16)]] },
            [Platform] = new(Platform, "plank", null, [], new PropertySet([new(TileCollisionBuilder.OneWayProperty, new PropertyValue(PropertyType.Bool, "true"))])),
            [Corner] = new(Corner, "corner", null, [], PropertySet.Empty)
            {
                Collision = [[new(-16, -16), new(0, -16), new(0, 0), new(16, 0), new(16, 16), new(-16, 16)]]
            }
        };
        var tileset = new Tileset(1, "tiles", 32, 32, null, 0, 0, 4, 4, tiles);
        var layer = new TileLayer("Collision", 5) { Role = LayerRole.Collision };
        foreach (var cell in solid)
            layer.SetCell(cell, new TileCell(1, Solid));
        return new TileMap("test", layout, 5, [tileset], [layer], PropertySet.Empty);
    }
}
