namespace Talesmith.Ecs;

public sealed partial class World
{
    /// <summary>Creates an entity with one component, placing it directly in its final archetype.</summary>
    public Entity Create<T1>(in T1 c1)
    {
        var (entity, archetype, row) = CreateIn(ComponentTypeCache<T1>.Id);
        archetype.Storage<T1>().Items[row] = c1;
        if (_observer is not null)
            NotifyCreated(entity, archetype);
        return entity;
    }

    public Entity Create<T1, T2>(in T1 c1, in T2 c2)
    {
        var (entity, archetype, row) = CreateIn(ComponentTypeCache<T1>.Id, ComponentTypeCache<T2>.Id);
        archetype.Storage<T1>().Items[row] = c1;
        archetype.Storage<T2>().Items[row] = c2;
        if (_observer is not null)
            NotifyCreated(entity, archetype);
        return entity;
    }

    public Entity Create<T1, T2, T3>(in T1 c1, in T2 c2, in T3 c3)
    {
        var (entity, archetype, row) = CreateIn(ComponentTypeCache<T1>.Id, ComponentTypeCache<T2>.Id, ComponentTypeCache<T3>.Id);
        archetype.Storage<T1>().Items[row] = c1;
        archetype.Storage<T2>().Items[row] = c2;
        archetype.Storage<T3>().Items[row] = c3;
        if (_observer is not null)
            NotifyCreated(entity, archetype);
        return entity;
    }

    public Entity Create<T1, T2, T3, T4>(in T1 c1, in T2 c2, in T3 c3, in T4 c4)
    {
        var (entity, archetype, row) = CreateIn(ComponentTypeCache<T1>.Id, ComponentTypeCache<T2>.Id, ComponentTypeCache<T3>.Id, ComponentTypeCache<T4>.Id);
        archetype.Storage<T1>().Items[row] = c1;
        archetype.Storage<T2>().Items[row] = c2;
        archetype.Storage<T3>().Items[row] = c3;
        archetype.Storage<T4>().Items[row] = c4;
        if (_observer is not null)
            NotifyCreated(entity, archetype);
        return entity;
    }

    private void NotifyCreated(Entity entity, Archetype archetype)
    {
        _observer!.OnEntityCreated(this, entity);
        foreach (var id in archetype.ComponentIds)
            _observer.OnComponentAdded(this, entity, ComponentType.FromId(id));
    }

    private (Entity Entity, Archetype Archetype, int Row) CreateIn(params ReadOnlySpan<int> typeIds)
    {
        EnsureNotIterating();
        var archetype = EmptyArchetype;
        foreach (var typeId in typeIds)
            archetype = AddEdge(archetype, typeId);

        var entity = AllocateEntity();
        Place(entity, archetype);
        return (entity, archetype, _records[entity.Id].Row);
    }
}
