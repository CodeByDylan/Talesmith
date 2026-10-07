using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Authoring;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Physics.Systems;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Physics.Tests;

public sealed class IntegrationTests
{
    [Fact]
    public void InterpolatedBodiesAreDrawnBetweenTheirLastTwoSteps()
    {
        var test = new PhysicsTestWorld(s => s.Gravity = Vector2.Zero);
        var box = test.Dynamic(Vector2.Zero, Collider2D.Box(new Vector2(10, 10)),
            new Rigidbody2D { Velocity = new Vector2(60, 0), Interpolation = RigidbodyInterpolation.Interpolate });
        test.Step(2);
        Assert.Equal(2, test.Position(box).X, 3);

        test.Physics.Interpolate(0.5f);
        Assert.Equal(1.5f, test.Position(box).X, 3);

        test.Step();
        Assert.Equal(3, test.Position(box).X, 3);
    }

    [Fact]
    public void MovingAnInterpolatedBodyTeleportsIt()
    {
        var test = new PhysicsTestWorld(s => s.Gravity = Vector2.Zero);
        var box = test.Dynamic(Vector2.Zero, Collider2D.Box(new Vector2(10, 10)), new Rigidbody2D { Interpolation = RigidbodyInterpolation.Interpolate });
        test.Step();
        test.Physics.Interpolate(0.5f);

        test.World.Get<Transform>(box).Position = new Vector2(500, 0);
        test.Step();

        Assert.Equal(500, test.Position(box).X, 3);
    }

    [Fact]
    public void KinematicMovePositionReachesTheTargetInOneStepAndPushes()
    {
        var test = new PhysicsTestWorld(s => s.Gravity = Vector2.Zero);
        var paddle = test.World.Create(new Transform(Vector2.Zero), Collider2D.Box(new Vector2(20, 100)), new Rigidbody2D(BodyType.Kinematic));
        var ball = test.Dynamic(new Vector2(30, 0), Collider2D.Circle(10));
        test.Step();

        test.Physics.MovePosition(paddle, new Vector2(15, 0));
        test.Step();
        Assert.Equal(15, test.Position(paddle).X, 3);
        Assert.Equal(Vector2.Zero, test.Body(paddle).Velocity);

        test.Step(10);
        Assert.Equal(15, test.Position(paddle).X, 3);
        Assert.True(test.Position(ball).X > 34, "The ball is pushed out of the paddle.");
    }

    [Fact]
    public void OneWayCollidersCatchFallingBodiesAndLetRisingOnesThrough()
    {
        var test = new PhysicsTestWorld();
        test.Static(new Vector2(0, 0), Collider2D.Box(new Vector2(200, 10)) with { OneWay = true });
        var falling = test.Dynamic(new Vector2(-50, -100), Collider2D.Box(new Vector2(20, 20)));
        var rising = test.Dynamic(new Vector2(50, 100), Collider2D.Box(new Vector2(20, 20)), new Rigidbody2D { Velocity = new Vector2(0, -700) });

        test.Step(90);

        Assert.InRange(test.Position(falling).Y, -16, -14.5f);
        Assert.InRange(test.Position(rising).Y, -16, -14.5f);
    }

    [Fact]
    public void ScaledTransformsScaleTheirColliders()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        var box = test.World.Create(new Transform(new Vector2(0, -100)) { Scale = new Vector2(2, 2) }, Collider2D.Box(new Vector2(20, 20)), new Rigidbody2D());

        test.Step(90);

        Assert.InRange(test.Position(box).Y, -20.5f, -19.5f);
    }

    [Fact]
    public void ConcavePolygonCollidersCollideWithEveryPiece()
    {
        var test = new PhysicsTestWorld();
        test.Ground();
        // A U shape whose arms rest on the ground.
        var cup = test.Dynamic(new Vector2(0, -100), Collider2D.Polygon(
            new Vector2(-30, -20), new Vector2(-20, -20), new Vector2(-20, 10), new Vector2(20, 10),
            new Vector2(20, -20), new Vector2(30, -20), new Vector2(30, 20), new Vector2(-30, 20)));

        test.Step(120);

        Assert.InRange(test.Position(cup).Y, -21, -19.5f);
        Assert.InRange(test.World.Get<Transform>(cup).Rotation, -0.01f, 0.01f);
    }

    [Fact]
    public void RegistrationAddsComponentsSystemsAndAWorldPerScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton(new Runtime.Diagnostics.DebugOptions());
        services.AddSingleton<Runtime.Diagnostics.EngineProfilers>();
        services.AddScoped<World>();
        services.AddTalesmithPhysics(s => s.Gravity = new Vector2(0, 500));
        services.AddTalesmithPhysics(s => s.SetLayerCollision(1, 2, false));
        using var provider = services.BuildServiceProvider();

        var components = provider.GetServices<ComponentRegistration>().Select(c => c.Type).ToList();
        Assert.Equal([typeof(Collider2D), typeof(Rigidbody2D), typeof(CharacterController2D), typeof(TileMapCollider2D)], components);
        var systems = provider.GetServices<SystemDescriptor>().Select(d => d.Type).ToList();
        Assert.Equal([typeof(PhysicsPrepareSystem), typeof(PhysicsStepSystem), typeof(PhysicsInterpolationSystem), typeof(PhysicsDebugDrawSystem)], systems);
        Assert.All(provider.GetServices<SystemDescriptor>().Where(d => d.Type != typeof(PhysicsDebugDrawSystem)), d => Assert.Equal(ExecutionModes.Play, d.Modes));

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var physics = first.ServiceProvider.GetRequiredService<IPhysicsWorld>();
        Assert.Same(physics, first.ServiceProvider.GetRequiredService<PhysicsWorld>());
        Assert.NotSame(physics, second.ServiceProvider.GetRequiredService<IPhysicsWorld>());
        Assert.Equal(new Vector2(0, 500), physics.Settings.Gravity);
        Assert.False(physics.Settings.ShouldLayersCollide(1, 2));

        physics.Settings.Gravity = Vector2.Zero;
        Assert.Equal(new Vector2(0, 500), second.ServiceProvider.GetRequiredService<IPhysicsWorld>().Settings.Gravity);
    }
}
