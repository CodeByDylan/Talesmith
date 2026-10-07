using System.Runtime.CompilerServices;

namespace Talesmith.Ecs;

/// <summary>Owns entities and their components and answers queries over them.</summary>
/// <remarks>
/// Entities with the same component types share an <see cref="Archetype"/>. Adding or removing a component moves the entity to
/// another archetype, so structural changes are not allowed while a query is iterating; record them in a
/// <see cref="CommandBuffer"/> and play it back afterwards. Reading and writing existing components is always allowed.
/// A world is not thread-safe.
/// </remarks>
public sealed partial class World
{
    private readonly Dictionary<ComponentSet, Archetype> _archetypesBySignature = new();
    private readonly List<Archetype> _archetypes = [];
    private readonly List<Query> _queries = [];
    private readonly Dictionary<QueryDescription, Query> _queriesByDescription = new();
    private readonly Stack<int> _freeIds = new();
    private EntityRecord[] _records = new EntityRecord[256];
    private int _nextId = 1;
    private int _iterationDepth;
    private IWorldObserver? _observer;

    public World()
    {
        EmptyArchetype = GetOrCreateArchetype(ComponentSet.Empty);
    }

    /// <summary>The number of live entities.</summary>
    public int EntityCount { get; private set; }

    public IReadOnlyList<Archetype> Archetypes => _archetypes;

    /// <summary>Whether a query is currently iterating, which forbids structural changes.</summary>
    public bool IsIterating => _iterationDepth > 0;

    internal Archetype EmptyArchetype { get; }

    /// <summary>Receives entity and component lifecycle notifications; null, the default, costs nothing.</summary>
    public IWorldObserver? Observer
    {
        get => _observer;
        set => _observer = value;
    }

    /// <summary>Creates an entity without components.</summary>
    public Entity Create()
    {
        EnsureNotIterating();
        var entity = AllocateEntity();
        Place(entity, EmptyArchetype);
        _observer?.OnEntityCreated(this, entity);
        return entity;
    }

    /// <summary>Destroys an entity and its components. Destroying a dead entity does nothing.</summary>
    public bool Destroy(Entity entity)
    {
        EnsureNotIterating();
        if (!IsAlive(entity))
            return false;

        _observer?.OnEntityDestroyed(this, entity);
        ref var record = ref _records[entity.Id];
        if (record.Archetype is { } archetype)
            RemoveFromArchetype(archetype, record.Row);

        record.Archetype = null;
        record.Row = -1;
        record.Version++;
        if (record.Version == 0)
            record.Version = 1;
        record.Alive = false;
        _freeIds.Push(entity.Id);
        EntityCount--;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsAlive(Entity entity) =>
        (uint)entity.Id < (uint)_records.Length && _records[entity.Id].Alive && _records[entity.Id].Version == entity.Version;

    /// <summary>Adds a component, or replaces it when the entity already has one.</summary>
    public void Set<T>(Entity entity, in T component)
    {
        ref var record = ref RecordOf(entity);
        var typeId = ComponentTypeCache<T>.Id;
        var archetype = record.Archetype!;
        var index = archetype.StorageIndexOf(typeId);
        if (index >= 0)
        {
            Unsafe.As<ComponentStorage<T>>(archetype.StorageAt(index)).Items[record.Row] = component;
            return;
        }

        EnsureNotIterating();
        var target = AddEdge(archetype, typeId);
        var row = Move(entity, ref record, target);
        target.Storage<T>().Items[row] = component;
        _observer?.OnComponentAdded(this, entity, ComponentTypeCache<T>.Type);
    }

    /// <summary>Adds a default component when the entity does not have one yet.</summary>
    public void Add<T>(Entity entity) where T : new()
    {
        if (!Has<T>(entity))
            Set(entity, new T());
    }

    /// <summary>Removes a component; returns false when the entity did not have it.</summary>
    public bool Remove<T>(Entity entity) => Remove(entity, ComponentTypeCache<T>.Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>(Entity entity) => RecordOf(entity).Archetype!.StorageIndexOf(ComponentTypeCache<T>.Id) >= 0;

    /// <summary>Gets a reference to a component that can be read and written in place.</summary>
    /// <exception cref="InvalidOperationException">The entity has no such component.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Get<T>(Entity entity)
    {
        ref var record = ref RecordOf(entity);
        return ref record.Archetype!.Storage<T>().Items[record.Row];
    }

    /// <summary>Gets a reference to a component, or a null reference when the entity has none.</summary>
    public ref T TryGetRef<T>(Entity entity, out bool exists)
    {
        ref var record = ref RecordOf(entity);
        var archetype = record.Archetype!;
        var index = archetype.StorageIndexOf(ComponentTypeCache<T>.Id);
        exists = index >= 0;
        if (!exists)
            return ref Unsafe.NullRef<T>();
        return ref Unsafe.As<ComponentStorage<T>>(archetype.StorageAt(index)).Items[record.Row];
    }

    public bool TryGet<T>(Entity entity, out T component)
    {
        if (IsAlive(entity))
        {
            ref var value = ref TryGetRef<T>(entity, out var exists);
            if (exists)
            {
                component = value;
                return true;
            }
        }

        component = default!;
        return false;
    }

    /// <summary>Lists the component types and boxed values of an entity, for inspection tools.</summary>
    public IEnumerable<(ComponentType Type, object? Value)> Inspect(Entity entity)
    {
        var record = RecordOf(entity);
        var archetype = record.Archetype!;
        for (var i = 0; i < archetype.ComponentIds.Length; i++)
            yield return (ComponentType.FromId(archetype.ComponentIds[i]), archetype.StorageAt(i).GetBoxed(record.Row));
    }

    /// <summary>The ids of the component types an entity has, in ascending order; see <see cref="ComponentType.FromId"/>.</summary>
    public ReadOnlySpan<int> GetComponentIds(Entity entity) => RecordOf(entity).Archetype!.ComponentIds;

    /// <summary>Gets the cached query for a description; queries track new archetypes automatically.</summary>
    public Query Query(QueryDescription description)
    {
        if (_queriesByDescription.TryGetValue(description, out var query))
            return query;

        query = new Query(this, description);
        foreach (var archetype in _archetypes)
            query.TryAdd(archetype);
        _queries.Add(query);
        _queriesByDescription[description] = query;
        return query;
    }

    public Query Query<T1>() => Query(CachedDescription<T1>.Value);

    public Query Query<T1, T2>() => Query(CachedDescription<T1, T2>.Value);

    public Query Query<T1, T2, T3>() => Query(CachedDescription<T1, T2, T3>.Value);

    public Query Query<T1, T2, T3, T4>() => Query(CachedDescription<T1, T2, T3, T4>.Value);

    /// <summary>Destroys every entity while keeping archetypes and queries for reuse.</summary>
    public void Clear()
    {
        EnsureNotIterating();
        for (var id = 1; id < _nextId; id++)
        {
            ref var record = ref _records[id];
            if (record.Alive)
                Destroy(new Entity(id, record.Version));
        }
    }

    internal void BeginIteration() => _iterationDepth++;

    internal void EndIteration() => _iterationDepth--;

    /// <summary>Reserves an entity that has no archetype yet; used by <see cref="CommandBuffer"/>.</summary>
    internal Entity Reserve()
    {
        var entity = AllocateEntity();
        _observer?.OnEntityCreated(this, entity);
        return entity;
    }

    internal bool Remove(Entity entity, int typeId)
    {
        ref var record = ref RecordOf(entity);
        var archetype = record.Archetype!;
        if (archetype.StorageIndexOf(typeId) < 0)
            return false;

        EnsureNotIterating();
        _observer?.OnComponentRemoved(this, entity, ComponentType.FromId(typeId));
        Move(entity, ref record, RemoveEdge(archetype, typeId));
        return true;
    }

    /// <summary>Places a reserved entity in the empty archetype so it can receive components.</summary>
    private void Materialize(Entity entity)
    {
        ref var record = ref _records[entity.Id];
        record.Archetype = EmptyArchetype;
        record.Row = EmptyArchetype.Add(entity);
    }

    private Entity AllocateEntity()
    {
        var id = _freeIds.Count > 0 ? _freeIds.Pop() : _nextId++;
        if (id >= _records.Length)
            Array.Resize(ref _records, _records.Length * 2);

        ref var record = ref _records[id];
        if (record.Version == 0)
            record.Version = 1;
        record.Alive = true;
        record.Archetype = null;
        record.Row = -1;
        EntityCount++;
        return new Entity(id, record.Version);
    }

    private void Place(Entity entity, Archetype archetype)
    {
        ref var record = ref _records[entity.Id];
        record.Archetype = archetype;
        record.Row = archetype.Add(entity);
    }

    private int Move(Entity entity, ref EntityRecord record, Archetype target)
    {
        var source = record.Archetype!;
        var oldRow = record.Row;
        var newRow = target.Add(entity);
        source.CopySharedComponents(oldRow, target, newRow);
        RemoveFromArchetype(source, oldRow);
        record.Archetype = target;
        record.Row = newRow;
        return newRow;
    }

    private void RemoveFromArchetype(Archetype archetype, int row)
    {
        var moved = archetype.RemoveSwapBack(row);
        if (!moved.IsNull)
            _records[moved.Id].Row = row;
    }

    private Archetype AddEdge(Archetype archetype, int typeId)
    {
        if (!archetype.TryGetAddEdge(typeId, out var target))
        {
            target = GetOrCreateArchetype(archetype.Signature.With(typeId));
            archetype.SetAddEdge(typeId, target);
            target.SetRemoveEdge(typeId, archetype);
        }

        return target;
    }

    private Archetype RemoveEdge(Archetype archetype, int typeId)
    {
        if (!archetype.TryGetRemoveEdge(typeId, out var target))
        {
            target = GetOrCreateArchetype(archetype.Signature.Without(typeId));
            archetype.SetRemoveEdge(typeId, target);
            target.SetAddEdge(typeId, archetype);
        }

        return target;
    }

    private Archetype GetOrCreateArchetype(ComponentSet signature)
    {
        if (_archetypesBySignature.TryGetValue(signature, out var archetype))
            return archetype;

        archetype = new Archetype(_archetypes.Count, signature);
        _archetypes.Add(archetype);
        _archetypesBySignature[signature] = archetype;
        foreach (var query in _queries)
            query.TryAdd(archetype);
        return archetype;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref EntityRecord RecordOf(Entity entity)
    {
        if (!IsAlive(entity))
            ThrowDead(entity);
        ref var record = ref _records[entity.Id];
        if (record.Archetype is null)
            Materialize(entity);
        return ref record;
    }

    private void EnsureNotIterating()
    {
        if (_iterationDepth > 0)
            throw new InvalidOperationException("Entities cannot be created, destroyed or change components while a query is iterating. Record the change in a CommandBuffer and play it back afterwards.");
    }

    private static void ThrowDead(Entity entity) =>
        throw new InvalidOperationException($"{entity} is not alive.");

    private static class CachedDescription<T1>
    {
        public static readonly QueryDescription Value = QueryDescription.With<T1>();
    }

    private static class CachedDescription<T1, T2>
    {
        public static readonly QueryDescription Value = QueryDescription.With<T1>().And<T2>();
    }

    private static class CachedDescription<T1, T2, T3>
    {
        public static readonly QueryDescription Value = QueryDescription.With<T1>().And<T2>().And<T3>();
    }

    private static class CachedDescription<T1, T2, T3, T4>
    {
        public static readonly QueryDescription Value = QueryDescription.With<T1>().And<T2>().And<T3>().And<T4>();
    }

    private struct EntityRecord
    {
        public Archetype? Archetype;
        public int Row;
        public int Version;
        public bool Alive;
    }
}
