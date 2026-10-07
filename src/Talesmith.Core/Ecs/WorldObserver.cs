using Talesmith.Events;

namespace Talesmith.Ecs;

/// <summary>Is told about structural changes of a <see cref="World"/>, set through <see cref="World.Observer"/>.</summary>
/// <remarks>
/// Calls happen synchronously inside the change. Destruction and removal are reported before they happen, so the entity's components
/// can still be read; creation and addition after. Components an entity is created with are reported after its creation. Observers
/// must not change the world's structure.
/// </remarks>
public interface IWorldObserver
{
    void OnEntityCreated(World world, Entity entity);

    void OnEntityDestroyed(World world, Entity entity);

    void OnComponentAdded(World world, Entity entity, ComponentType type);

    void OnComponentRemoved(World world, Entity entity, ComponentType type);
}

/// <summary>Raised after an entity was created, with the components it was created with.</summary>
public readonly record struct EntityCreated(World World, Entity Entity);

/// <summary>Raised before an entity is destroyed, while its components can still be read.</summary>
public readonly record struct EntityDestroyed(World World, Entity Entity);

/// <summary>Raised after a component was added to an entity; replacing a component's value raises nothing.</summary>
public readonly record struct ComponentAdded(World World, Entity Entity, ComponentType Type);

/// <summary>Raised before a component is removed from a living entity, while it can still be read.</summary>
public readonly record struct ComponentRemoved(World World, Entity Entity, ComponentType Type);

/// <summary>Publishes a world's structural changes as <see cref="EntityCreated"/>, <see cref="EntityDestroyed"/>, <see cref="ComponentAdded"/> and <see cref="ComponentRemoved"/>.</summary>
/// <remarks>Events without subscribers are skipped, so an unobserved world pays one subscriber check per structural change.</remarks>
public sealed class WorldEventPublisher(IEventBus events) : IWorldObserver
{
    public void OnEntityCreated(World world, Entity entity)
    {
        if (events.HasSubscribers<EntityCreated>())
            events.Publish(new EntityCreated(world, entity));
    }

    public void OnEntityDestroyed(World world, Entity entity)
    {
        if (events.HasSubscribers<EntityDestroyed>())
            events.Publish(new EntityDestroyed(world, entity));
    }

    public void OnComponentAdded(World world, Entity entity, ComponentType type)
    {
        if (events.HasSubscribers<ComponentAdded>())
            events.Publish(new ComponentAdded(world, entity, type));
    }

    public void OnComponentRemoved(World world, Entity entity, ComponentType type)
    {
        if (events.HasSubscribers<ComponentRemoved>())
            events.Publish(new ComponentRemoved(world, entity, type));
    }
}
