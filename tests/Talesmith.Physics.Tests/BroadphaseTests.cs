using System.Numerics;
using Talesmith.Physics.Broadphase;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Tests;

public sealed class BroadphaseTests
{
    public static TheoryData<string> Kinds => ["tree", "hash"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void QueriesFindExactlyTheOverlappingProxies(string kind)
    {
        var broadphase = Create(kind);
        var random = new Random(7);
        var boxes = new List<Aabb>();
        var proxies = new List<int>();
        for (var i = 0; i < 500; i++)
        {
            var min = new Vector2(random.Next(-2000, 2000), random.Next(-2000, 2000));
            var box = new Aabb(min, min + new Vector2(random.Next(1, i % 50 == 0 ? 3000 : 60), random.Next(1, 60)));
            boxes.Add(box);
            proxies.Add(broadphase.CreateProxy(box, i));
        }

        for (var i = 0; i < 100; i += 2)
        {
            broadphase.DestroyProxy(proxies[i]);
            proxies[i] = -1;
        }

        for (var i = 1; i < 200; i += 2)
        {
            var moved = boxes[i].Offset(new Vector2(random.Next(-300, 300), random.Next(-300, 300)));
            broadphase.MoveProxy(proxies[i], moved, moved.Center - boxes[i].Center);
            boxes[i] = moved;
        }

        for (var q = 0; q < 50; q++)
        {
            var min = new Vector2(random.Next(-2000, 2000), random.Next(-2000, 2000));
            var area = new Aabb(min, min + new Vector2(random.Next(1, 400), random.Next(1, 400)));
            var collector = new Collector();
            broadphase.Query(area, ref collector);

            for (var i = 0; i < boxes.Count; i++)
            {
                if (proxies[i] < 0)
                    continue;
                if (boxes[i].Overlaps(area))
                    Assert.Contains(i, collector.Found);
            }

            Assert.All(collector.Found, i => Assert.True(broadphase.GetFatAabb(proxies[i]).Overlaps(area)));
            Assert.Equal(collector.Found.Count, collector.Found.Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RayCastsVisitEveryProxyAlongTheRay(string kind)
    {
        var broadphase = Create(kind);
        for (var i = 0; i < 20; i++)
            broadphase.CreateProxy(new Aabb(new Vector2(i * 100, -10), new Vector2(i * 100 + 20, 10)), i);
        broadphase.CreateProxy(new Aabb(new Vector2(0, 100), new Vector2(50, 150)), 99);
        broadphase.CreateProxy(new Aabb(new Vector2(-5000, -5000), new Vector2(5000, -4000)), 98);

        var collector = new RayCollector();
        broadphase.RayCast(new Vector2(-50, 0), new Vector2(2100, 0), 1, ref collector);

        Assert.Equal(Enumerable.Range(0, 20), collector.Found.Order());
    }

    [Fact]
    public void TheTreeStaysBalanced()
    {
        var tree = new DynamicTree(1);
        for (var i = 0; i < 4096; i++)
            tree.CreateProxy(new Aabb(new Vector2(i * 10, 0), new Vector2(i * 10 + 5, 5)), i);

        Assert.InRange(tree.Height, 12, 30);
    }

    [Fact]
    public void SmallMovesInsideTheFatBoundsDoNotRestructure()
    {
        var tree = new DynamicTree(10);
        var box = new Aabb(Vector2.Zero, new Vector2(10, 10));
        var proxy = tree.CreateProxy(box, 0);

        Assert.False(tree.MoveProxy(proxy, box.Offset(new Vector2(2, 0)), new Vector2(2, 0)));
        Assert.True(tree.MoveProxy(proxy, box.Offset(new Vector2(50, 0)), new Vector2(50, 0)));
    }

    private static IBroadphase Create(string kind) => kind == "tree" ? new DynamicTree(5) : new SpatialHash(5, 64);

    private struct Collector() : IProxyQuery
    {
        public List<int> Found { get; } = [];

        public readonly bool Report(int proxy, int userData)
        {
            Found.Add(userData);
            return true;
        }
    }

    private struct RayCollector() : IProxyRayCast
    {
        public List<int> Found { get; } = [];

        public readonly float Report(int proxy, int userData, float maxFraction)
        {
            Found.Add(userData);
            return -1;
        }
    }
}
