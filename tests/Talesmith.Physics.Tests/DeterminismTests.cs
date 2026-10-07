using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Tests;

public sealed class DeterminismTests
{
    [Theory]
    [InlineData(BroadphaseKind.DynamicTree)]
    [InlineData(BroadphaseKind.SpatialHash)]
    public void IdenticalRunsProduceIdenticalResults(BroadphaseKind broadphase)
    {
        var first = Simulate(broadphase);
        var second = Simulate(broadphase);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BothBroadphasesProduceTheSameSimulation()
    {
        Assert.Equal(Simulate(BroadphaseKind.DynamicTree), Simulate(BroadphaseKind.SpatialHash));
    }

    /// <summary>Drops a pile of mixed shapes into a box with a ramp and returns every final pose, bit for bit.</summary>
    private static (Vector2 Position, float Rotation)[] Simulate(BroadphaseKind broadphase)
    {
        var test = new PhysicsTestWorld(s => s.Broadphase = broadphase);
        test.Ground();
        test.Static(new Vector2(-400, -300), Collider2D.Box(new Vector2(40, 600)));
        test.Static(new Vector2(400, -300), Collider2D.Box(new Vector2(40, 600)));
        test.Static(new Vector2(-100, -150), Collider2D.Box(new Vector2(300, 20)), 0.3f);
        var random = new Random(1234);
        var bodies = new List<Entity>();
        for (var i = 0; i < 150; i++)
        {
            var position = new Vector2(random.Next(-350, 350), -300 - random.Next(0, 800));
            var collider = (i % 4) switch
            {
                0 => Collider2D.Box(new Vector2(random.Next(10, 40), random.Next(10, 40))),
                1 => Collider2D.Circle(random.Next(5, 20)),
                2 => Collider2D.Capsule(new Vector2(random.Next(10, 20), random.Next(25, 50))),
                _ => Collider2D.Polygon(new Vector2(-15, 10), new Vector2(15, 10), new Vector2(0, -15))
            };
            bodies.Add(test.Dynamic(position, collider with { Restitution = i % 3 == 0 ? 0.3f : 0 }));
        }

        test.Step(300);
        return bodies.Select(b => (test.Position(b), test.World.Get<Transform>(b).Rotation)).ToArray();
    }
}
