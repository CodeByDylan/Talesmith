using System.Numerics;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Grids;
using Talesmith.Physics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;

namespace Talesmith.Benchmarks;

/// <summary>One 60 Hz fixed step of a physics world with every body awake.</summary>
/// <remarks>
/// Pile: bodies resting on each other in a box, the solver's worst case. Swarm: bodies flying around a large box without gravity, the
/// broadphase's worst case. Tiles: bodies resting on the collision layer of a square tile map with uneven terrain.
/// </remarks>
[MemoryDiagnoser]
public class PhysicsStepBenchmarks : IDisposable
{
    private const float Step = 1f / 60;

    private PhysicsWorld _physics = null!;

    [Params(1000, 4000)]
    public int Bodies { get; set; }

    [Params("Pile", "Swarm", "Tiles")]
    public string Scenario { get; set; } = "Pile";

    [Params(BroadphaseKind.DynamicTree, BroadphaseKind.SpatialHash)]
    public BroadphaseKind Broadphase { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var world = new World();
        var settings = new PhysicsSettings { Broadphase = Broadphase, AllowSleeping = false };
        if (Scenario == "Swarm")
            settings.Gravity = Vector2.Zero;
        _physics = new PhysicsWorld(world, settings, new EventBus(), new EngineProfilers(), NullLogger<PhysicsWorld>.Instance);
        var random = new Random(42);
        var columns = (int)MathF.Sqrt(Bodies) * 2;
        var width = columns * 24f;

        if (Scenario == "Tiles")
            world.Create(new TileMapComponent(Terrain(columns, random)), new Transform(new Vector2(-width / 2, 0)));
        else
            Box(world, Scenario == "Swarm" ? width * 2 : width);

        for (var i = 0; i < Bodies; i++)
        {
            var position = new Vector2(-width / 2 + 16 + i % columns * 24, -40 - i / columns * 24);
            var collider = i % 2 == 0 ? Collider2D.Box(new Vector2(16, 16)) : Collider2D.Circle(8);
            var body = new Rigidbody2D();
            if (Scenario == "Swarm")
            {
                position = new Vector2(random.Next((int)-width + 50, (int)width - 50), -random.Next(50, (int)(width * 2) - 50));
                body.Velocity = new Vector2(random.Next(-200, 200), random.Next(-200, 200));
                collider = collider with { Restitution = 1, Friction = 0 };
            }

            world.Create(new Transform(position), collider, body);
        }

        for (var i = 0; i < 240; i++)
            _physics.Step(Step);
    }

    [Benchmark]
    public void FixedStep() => _physics.Step(Step);

    [GlobalCleanup]
    public void Dispose()
    {
        _physics.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void Box(World world, float width)
    {
        var height = width * 2;
        world.Create(new Transform(new Vector2(0, 50)), Collider2D.Box(new Vector2(width + 200, 100)));
        world.Create(new Transform(new Vector2(0, -height - 50)), Collider2D.Box(new Vector2(width + 200, 100)));
        world.Create(new Transform(new Vector2(-width / 2 - 50, -height / 2)), Collider2D.Box(new Vector2(100, height + 200)));
        world.Create(new Transform(new Vector2(width / 2 + 50, -height / 2)), Collider2D.Box(new Vector2(100, height + 200)));
    }

    /// <summary>A square map, 32 units per cell, with a bumpy floor and walls on both sides.</summary>
    private static TileMap Terrain(int columns, Random random)
    {
        var layer = new TileLayer("Collision", 5) { Role = LayerRole.Collision };
        var cells = columns * 24 / 32 + 2;
        for (var x = -1; x <= cells; x++)
        {
            var top = x < 0 || x == cells ? -200 : random.Next(0, 2);
            for (var y = top; y < 6; y++)
                layer.SetCell(new GridCoord(x, y), new TileCell(1, 0));
        }

        return new TileMap("terrain", new SquareLayout(32, 32), 5, [], [layer], PropertySet.Empty);
    }
}

/// <summary>Queries against a world of 4000 bodies: 1000 rays, or 1000 circle overlaps, per operation.</summary>
[MemoryDiagnoser]
public class PhysicsQueryBenchmarks : IDisposable
{
    private const int Queries = 1000;

    private readonly Entity[] _overlaps = new Entity[64];
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private PhysicsWorld _physics = null!;
    private Vector2[] _origins = [];

    [Params(BroadphaseKind.DynamicTree, BroadphaseKind.SpatialHash)]
    public BroadphaseKind Broadphase { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var world = new World();
        _physics = new PhysicsWorld(world, new PhysicsSettings { Broadphase = Broadphase, Gravity = Vector2.Zero }, new EventBus(), new EngineProfilers(),
            NullLogger<PhysicsWorld>.Instance);
        var random = new Random(3);
        for (var i = 0; i < 4000; i++)
        {
            var position = new Vector2(random.Next(-3000, 3000), random.Next(-3000, 3000));
            world.Create(new Transform(position), i % 2 == 0 ? Collider2D.Box(new Vector2(20, 20)) : Collider2D.Circle(10));
        }

        _physics.Step(1f / 60);
        _origins = Enumerable.Range(0, Queries).Select(_ => new Vector2(random.Next(-3000, 3000), random.Next(-3000, 3000))).ToArray();
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _physics.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public int ClosestRays()
    {
        var hits = 0;
        for (var i = 0; i < Queries; i++)
        {
            var direction = new Vector2(MathF.Cos(i), MathF.Sin(i));
            if (_physics.RayCast(_origins[i], direction, 500, out _))
                hits++;
        }

        return hits;
    }

    [Benchmark]
    public int AllRays()
    {
        var hits = 0;
        for (var i = 0; i < Queries; i++)
            hits += _physics.RayCastAll(_origins[i], new Vector2(MathF.Cos(i), MathF.Sin(i)), 500, _hits);
        return hits;
    }

    [Benchmark]
    public int CircleOverlaps()
    {
        var found = 0;
        for (var i = 0; i < Queries; i++)
            found += _physics.OverlapCircle(_origins[i], 60, QueryFilter.Default, _overlaps);
        return found;
    }
}
