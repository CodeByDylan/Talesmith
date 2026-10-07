using Talesmith.Ecs;
using Talesmith.Events;

namespace Talesmith.Core.Tests;

public sealed class WorldObserverTests
{
    private readonly record struct Position(float X, float Y);

    private readonly record struct Velocity(float X, float Y);

    [Fact]
    public void StructuralChangesAreReportedInOrder()
    {
        var log = new RecordingObserver();
        var world = new World { Observer = log };

        var entity = world.Create(new Position(1, 2));
        world.Set(entity, new Velocity(3, 4));
        world.Set(entity, new Velocity(5, 6));
        world.Remove<Position>(entity);
        world.Destroy(entity);

        Assert.Equal(
        [
            "created",
            "added Position",
            "added Velocity",
            "removed Position (still readable: True)",
            "destroyed (velocity 5)"
        ], log.Entries);
    }

    [Fact]
    public void CommandBufferChangesAreReported()
    {
        var log = new RecordingObserver();
        var world = new World { Observer = log };
        var commands = new CommandBuffer(world);

        var entity = commands.Create();
        commands.Set(entity, new Position(0, 0));
        commands.Playback();

        Assert.Equal(["created", "added Position"], log.Entries);
    }

    [Fact]
    public void TheEventPublisherOnlyPublishesWhatIsSubscribed()
    {
        var events = new EventBus();
        var world = new World { Observer = new WorldEventPublisher(events) };
        var created = new List<Entity>();
        var added = new List<Type>();
        using var createdSubscription = events.Subscribe((ref EntityCreated e) => created.Add(e.Entity));
        using var addedSubscription = events.Subscribe((ref ComponentAdded e) => added.Add(e.Type.Type));

        var entity = world.Create(new Position(1, 1), new Velocity(1, 1));
        world.Destroy(entity);

        Assert.Equal([entity], created);
        Assert.Equal([typeof(Position), typeof(Velocity)], added.Order(Comparer<Type>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name))));
        Assert.False(events.HasSubscribers<EntityDestroyed>());
    }

    [Fact]
    public void ComponentIdsListAnEntitysComponents()
    {
        var world = new World();

        var entity = world.Create(new Position(1, 1), new Velocity(1, 1));

        Assert.Equal([typeof(Position), typeof(Velocity)], world.GetComponentIds(entity).ToArray().Select(id => ComponentType.FromId(id).Type).OrderBy(t => t.Name));
    }

    private sealed class RecordingObserver : IWorldObserver
    {
        public List<string> Entries { get; } = [];

        public void OnEntityCreated(World world, Entity entity) => Entries.Add("created");

        public void OnEntityDestroyed(World world, Entity entity) =>
            Entries.Add($"destroyed (velocity {(world.TryGet<Velocity>(entity, out var velocity) ? velocity.X : -1)})");

        public void OnComponentAdded(World world, Entity entity, ComponentType type) => Entries.Add($"added {type.Type.Name}");

        public void OnComponentRemoved(World world, Entity entity, ComponentType type) =>
            Entries.Add($"removed {type.Type.Name} (still readable: {world.TryGet<Position>(entity, out _)})");
    }
}
