using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.Physics.Tests;

public sealed class QueryTests
{
    [Theory]
    [InlineData(BroadphaseKind.DynamicTree)]
    [InlineData(BroadphaseKind.SpatialHash)]
    public void RayCastFindsTheClosestCollider(BroadphaseKind broadphase)
    {
        var test = new PhysicsTestWorld(s => s.Broadphase = broadphase);
        test.Static(new Vector2(300, 0), Collider2D.Box(new Vector2(20, 100)));
        var near = test.Static(new Vector2(200, 0), Collider2D.Circle(10));
        test.Static(new Vector2(400, 0), Collider2D.Capsule(new Vector2(20, 60)));
        test.Step();

        Assert.True(test.Physics.RayCast(Vector2.Zero, new Vector2(1, 0), 1000, out var hit));

        Assert.Equal(near, hit.Entity);
        Assert.Equal(190, hit.Distance, 2);
        Assert.Equal(new Vector2(190, 0), hit.Point);
        Assert.Equal(-1, hit.Normal.X, 3);
        Assert.Equal(0.19f, hit.Fraction, 3);
    }

    [Theory]
    [InlineData(BroadphaseKind.DynamicTree)]
    [InlineData(BroadphaseKind.SpatialHash)]
    public void RayCastAllReturnsEveryHitNearestFirst(BroadphaseKind broadphase)
    {
        var test = new PhysicsTestWorld(s => s.Broadphase = broadphase);
        var far = test.Static(new Vector2(400, 0), Collider2D.Capsule(new Vector2(20, 60)));
        var middle = test.Static(new Vector2(300, 0), Collider2D.Box(new Vector2(20, 100)));
        var near = test.Static(new Vector2(200, 0), Collider2D.Circle(10));
        test.Step();

        Span<RaycastHit> hits = stackalloc RaycastHit[8];
        var count = test.Physics.RayCastAll(Vector2.Zero, new Vector2(1, 0), 1000, hits);

        Assert.Equal(3, count);
        Assert.Equal([near, middle, far], hits[..count].ToArray().Select(h => h.Entity));
        Assert.Equal(390, hits[2].Distance, 2);
    }

    [Fact]
    public void RayCastAllKeepsTheNearestHitsWhenResultsFillUp()
    {
        var test = new PhysicsTestWorld();
        var entities = new List<Entity>();
        for (var i = 0; i < 10; i++)
            entities.Add(test.Static(new Vector2(100 + i * 50, 0), Collider2D.Circle(10)));
        test.Step();

        Span<RaycastHit> hits = stackalloc RaycastHit[3];
        var count = test.Physics.RayCastAll(Vector2.Zero, new Vector2(1, 0), 1000, hits);

        Assert.Equal(3, count);
        Assert.Equal(entities[..3], hits.ToArray().Select(h => h.Entity));
    }

    [Fact]
    public void RaysMissWhatIsOutOfReachOrOffTheLine()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(200, 50), Collider2D.Circle(10));
        test.Static(new Vector2(600, 0), Collider2D.Circle(10));
        test.Step();

        Assert.False(test.Physics.RayCast(Vector2.Zero, new Vector2(1, 0), 500, out _));
    }

    [Fact]
    public void RaysStartingInsideAColliderDoNotHitIt()
    {
        var test = new PhysicsTestWorld();
        test.Static(Vector2.Zero, Collider2D.Box(new Vector2(100, 100)));
        test.Step();

        Assert.False(test.Physics.RayCast(Vector2.Zero, new Vector2(1, 0), 500, out _));
    }

    [Fact]
    public void LayerMasksAndTriggersFilterQueries()
    {
        var test = new PhysicsTestWorld();
        var trigger = test.Static(new Vector2(100, 0), Collider2D.Circle(10) with { IsTrigger = true });
        var onLayerThree = test.Static(new Vector2(200, 0), Collider2D.Circle(10) with { Layer = 3 });
        var onLayerZero = test.Static(new Vector2(300, 0), Collider2D.Circle(10));
        test.Step();

        test.Physics.RayCast(Vector2.Zero, Vector2.UnitX, 1000, QueryFilter.Default, out var solid);
        test.Physics.RayCast(Vector2.Zero, Vector2.UnitX, 1000, QueryFilter.Default with { IncludeTriggers = true }, out var any);
        test.Physics.RayCast(Vector2.Zero, Vector2.UnitX, 1000, new QueryFilter(PhysicsLayers.Mask(0)), out var layerZero);
        test.Physics.RayCast(Vector2.Zero, Vector2.UnitX, 1000, QueryFilter.Default with { Ignore = onLayerThree }, out var ignoring);

        Assert.Equal(onLayerThree, solid.Entity);
        Assert.Equal(trigger, any.Entity);
        Assert.True(any.IsTrigger);
        Assert.Equal(onLayerZero, layerZero.Entity);
        Assert.Equal(onLayerZero, ignoring.Entity);
    }

    [Fact]
    public void RaysHitRotatedAndOffsetShapes()
    {
        var test = new PhysicsTestWorld();
        var box = test.Static(new Vector2(200, 0), Collider2D.Box(new Vector2(20, 20)) with { Offset = new Vector2(0, 50) }, MathF.PI / 2);
        test.Step();

        // Turned a quarter clockwise, the offset (0, 50) points to (-50, 0).
        Assert.True(test.Physics.RayCast(Vector2.Zero, Vector2.UnitX, 1000, out var hit));
        Assert.Equal(box, hit.Entity);
        Assert.Equal(140, hit.Distance, 2);
    }

    [Fact]
    public void OverlapPointFindsContainingColliders()
    {
        var test = new PhysicsTestWorld();
        var circle = test.Static(Vector2.Zero, Collider2D.Circle(20));
        var box = test.Static(new Vector2(10, 0), Collider2D.Box(new Vector2(20, 20)));
        test.Static(new Vector2(100, 0), Collider2D.Box(new Vector2(20, 20)));
        test.Step();

        Span<Entity> found = stackalloc Entity[8];
        var count = test.Physics.OverlapPoint(new Vector2(12, 0), found);

        Assert.Equal(2, count);
        Assert.Contains(circle, found[..count].ToArray());
        Assert.Contains(box, found[..count].ToArray());
    }

    [Fact]
    public void OverlapShapeFindsOverlappingButNotMerelyTouchingColliders()
    {
        var test = new PhysicsTestWorld();
        var overlapping = test.Static(new Vector2(25, 0), Collider2D.Box(new Vector2(20, 20)));
        test.Static(new Vector2(-30, 0), Collider2D.Box(new Vector2(20, 20)));
        test.Static(new Vector2(0, 80), Collider2D.Circle(10));
        test.Step();

        Span<Entity> found = stackalloc Entity[8];
        var count = test.Physics.OverlapBox(Vector2.Zero, new Vector2(40, 40), 0, QueryFilter.Default, found);

        Assert.Equal(overlapping, Assert.Single(found[..count].ToArray()));
    }

    [Fact]
    public void ShapeCastStopsAtTheFirstCollider()
    {
        var test = new PhysicsTestWorld();
        var wall = test.Static(new Vector2(200, 0), Collider2D.Box(new Vector2(20, 200)));
        test.Step();

        Assert.True(test.Physics.ShapeCast(QueryShape.Circle(10), Vector2.Zero, 0, Vector2.UnitX, 500, QueryFilter.Default, out var hit));

        Assert.Equal(wall, hit.Entity);
        Assert.InRange(hit.Distance, 179, 180);
        Assert.Equal(-1, hit.Normal.X, 3);
        Assert.Equal(190, hit.Point.X, 1);
    }

    [Fact]
    public void QueriesSeeBodiesMovedByTheSimulation()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.Dynamic(new Vector2(0, -200), Collider2D.Box(new Vector2(32, 32)));
        test.Step(60);

        Assert.True(test.Physics.RayCast(new Vector2(-100, -16), Vector2.UnitX, 200, out var hit));
        Assert.Equal(box, hit.Entity);
    }
}
