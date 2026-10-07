using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Physics.Tests;

public sealed class SerializationTests : IDisposable
{
    private readonly ServiceProvider _services;

    public SerializationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IRenderer, NullRenderer>();
        services.AddSingleton(new GameSettings());
        services.AddSingleton<TextureCache>();
        services.AddTalesmithScenes();
        services.AddTalesmithPhysics();
        _services = services.BuildServiceProvider();
    }

    private ComponentRegistry Registry => new(_services.GetServices<Authoring.ComponentRegistration>(), [], _services.GetRequiredService<ValueConverterRegistry>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<ComponentRegistry>.Instance);

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData("Collider2D")]
    [InlineData("Rigidbody2D")]
    [InlineData("CharacterController2D")]
    [InlineData("TileMapCollider2D")]
    public void PhysicsComponentsAreAuthorableUnderTheirShortNames(string name)
    {
        Assert.True(Registry.TryGet(name, out var definition));
        Assert.Equal("Physics", definition.Info.Category);
        Assert.Empty(((dynamic)definition).UnsupportedMembers);
    }

    [Fact]
    public void RuntimeStateOfCharactersIsNotSaved()
    {
        Assert.True(Registry.TryGet("CharacterController2D", out var definition));

        var names = definition.Properties.Select(p => p.Name).ToList();

        Assert.Contains("slopeLimit", names);
        Assert.DoesNotContain("collisions", names);
        Assert.DoesNotContain("isGrounded", names);
        Assert.DoesNotContain("ground", names);
    }

    [Fact]
    public void CollidersRoundTripThroughSavedData()
    {
        Assert.True(Registry.TryGet("Collider2D", out var definition));
        var world = new World();
        var source = world.Create(Collider2D.Polygon(new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 10)) with
        {
            IsTrigger = true,
            Layer = 5,
            CollisionMask = PhysicsLayers.Mask(1, 2),
            Rotation = 0.5f
        });

        var data = definition.Capture(world, source, NullCapture.Instance)!;
        var target = world.Create();
        definition.Apply(world, target, data, null!);

        var copy = world.Get<Collider2D>(target);
        Assert.Equal(ColliderShape.Polygon, copy.Shape);
        Assert.Equal([new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 10)], copy.Points!);
        Assert.True(copy.IsTrigger);
        Assert.Equal(5, copy.Layer);
        Assert.Equal(PhysicsLayers.Mask(1, 2), copy.CollisionMask);
        Assert.Equal(0.5f, copy.Rotation);
    }

    [Fact]
    public void NewCollidersStartWithUsefulDefaults()
    {
        Assert.True(Registry.TryGet("Collider2D", out var definition));
        var world = new World();
        var entity = world.Create();

        definition.Apply(world, entity, definition.CreateDefault(), null!);

        var collider = world.Get<Collider2D>(entity);
        Assert.Equal(new Vector2(32, 32), collider.Size);
        Assert.Equal(PhysicsLayers.All, collider.CollisionMask);
        Assert.Equal(0.4f, collider.Friction);
    }

    private sealed class NullCapture : ICaptureContext
    {
        public static NullCapture Instance { get; } = new();

        public Assets.AssetGuid GetGuid(object asset) => default;

        public Guid GetEntityId(Entity entity) => Guid.Empty;
    }
}
