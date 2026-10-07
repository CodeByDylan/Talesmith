using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;

namespace Talesmith.Physics.Tests;

/// <summary>An ECS world with a physics world and helpers to create bodies and step.</summary>
internal sealed class PhysicsTestWorld
{
    public const float Dt = 1f / 60;

    public PhysicsTestWorld(Action<PhysicsSettings>? configure = null)
    {
        var settings = new PhysicsSettings();
        configure?.Invoke(settings);
        Physics = new PhysicsWorld(World, settings, Events, new EngineProfilers(), NullLogger<PhysicsWorld>.Instance);
    }

    public World World { get; } = new();

    public EventBus Events { get; } = new();

    public PhysicsWorld Physics { get; }

    public Entity Static(Vector2 position, Collider2D collider, float rotation = 0) =>
        World.Create(new Transform(position, rotation), collider);

    /// <summary>A wide static box whose top edge is at <paramref name="top"/>.</summary>
    public Entity Ground(float top = 0, float width = 4000, float height = 100) =>
        Static(new Vector2(0, top + height / 2), Collider2D.Box(new Vector2(width, height)));

    public Entity Dynamic(Vector2 position, Collider2D collider, Rigidbody2D? body = null) =>
        World.Create(new Transform(position), collider, body ?? new Rigidbody2D());

    public void Step(int steps = 1)
    {
        for (var i = 0; i < steps; i++)
            Physics.Step(Dt);
    }

    public Vector2 Position(Entity entity) => World.Get<Transform>(entity).Position;

    public ref Rigidbody2D Body(Entity entity) => ref World.Get<Rigidbody2D>(entity);
}
