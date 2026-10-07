using System.Runtime.CompilerServices;

namespace Talesmith.Ecs;

/// <summary>All entities that have exactly the same set of component types, stored as one array per component type.</summary>
public sealed class Archetype
{
    private const int InitialCapacity = 16;

    private readonly ComponentStorage[] _storages;
    private readonly int[] _storageIndex;
    private readonly Dictionary<int, Archetype> _addEdges = new();
    private readonly Dictionary<int, Archetype> _removeEdges = new();
    private Entity[] _entities = new Entity[InitialCapacity];

    internal Archetype(int index, ComponentSet signature)
    {
        Index = index;
        Signature = signature;
        ComponentIds = signature.Ids.ToArray();
        _storages = new ComponentStorage[ComponentIds.Length];
        _storageIndex = new int[ComponentIds.Length == 0 ? 0 : ComponentIds[^1] + 1];
        Array.Fill(_storageIndex, -1);
        for (var i = 0; i < ComponentIds.Length; i++)
        {
            _storages[i] = ComponentType.FromId(ComponentIds[i]).CreateStorage(InitialCapacity);
            _storageIndex[ComponentIds[i]] = i;
        }
    }

    /// <summary>The position of this archetype in <see cref="World.Archetypes"/>.</summary>
    public int Index { get; }

    public ComponentSet Signature { get; }

    /// <summary>The component type ids of this archetype in ascending order.</summary>
    public int[] ComponentIds { get; }

    public int Count { get; private set; }

    public ReadOnlySpan<Entity> Entities => _entities.AsSpan(0, Count);

    /// <summary>Gets the components of type <typeparamref name="T"/> of every entity in this archetype.</summary>
    /// <exception cref="InvalidOperationException">The archetype has no component of type <typeparamref name="T"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetSpan<T>() => Storage<T>().Items.AsSpan(0, Count);

    public bool Has<T>() => StorageIndexOf(ComponentTypeCache<T>.Id) >= 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ComponentStorage<T> Storage<T>()
    {
        var index = StorageIndexOf(ComponentTypeCache<T>.Id);
        if (index < 0)
            ThrowMissing(typeof(T));
        return Unsafe.As<ComponentStorage<T>>(_storages[index]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int StorageIndexOf(int typeId) => (uint)typeId < (uint)_storageIndex.Length ? _storageIndex[typeId] : -1;

    internal ComponentStorage StorageAt(int index) => _storages[index];

    internal bool TryGetAddEdge(int typeId, out Archetype archetype) => _addEdges.TryGetValue(typeId, out archetype!);

    internal void SetAddEdge(int typeId, Archetype archetype) => _addEdges[typeId] = archetype;

    internal bool TryGetRemoveEdge(int typeId, out Archetype archetype) => _removeEdges.TryGetValue(typeId, out archetype!);

    internal void SetRemoveEdge(int typeId, Archetype archetype) => _removeEdges[typeId] = archetype;

    /// <summary>Appends an entity with default components and returns its row.</summary>
    internal int Add(Entity entity)
    {
        if (Count == _entities.Length)
        {
            var capacity = _entities.Length * 2;
            Array.Resize(ref _entities, capacity);
            foreach (var storage in _storages)
                storage.Resize(capacity);
        }

        _entities[Count] = entity;
        return Count++;
    }

    /// <summary>Removes the entity at <paramref name="row"/> by moving the last entity into its place.</summary>
    /// <returns>The entity that now occupies <paramref name="row"/>, or <see cref="Entity.Null"/> when the removed entity was last.</returns>
    internal Entity RemoveSwapBack(int row)
    {
        var last = Count - 1;
        foreach (var storage in _storages)
            storage.RemoveSwapBack(row, last);

        var moved = Entity.Null;
        if (row != last)
        {
            moved = _entities[last];
            _entities[row] = moved;
        }

        _entities[last] = default;
        Count--;
        return moved;
    }

    /// <summary>Copies every component this archetype shares with <paramref name="destination"/>.</summary>
    internal void CopySharedComponents(int row, Archetype destination, int destinationRow)
    {
        for (var i = 0; i < _storages.Length; i++)
        {
            var destinationIndex = destination.StorageIndexOf(ComponentIds[i]);
            if (destinationIndex >= 0)
                _storages[i].CopyTo(row, destination._storages[destinationIndex], destinationRow);
        }
    }

    public override string ToString() => $"Archetype {Signature} ({Count} entities)";

    private static void ThrowMissing(Type type) =>
        throw new InvalidOperationException($"The archetype has no {type.Name} component.");
}
