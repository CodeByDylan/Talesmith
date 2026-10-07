using System.Runtime.CompilerServices;

namespace Talesmith.Ecs;

/// <summary>Describes a component type registered with the entity component system.</summary>
public sealed class ComponentType
{
    private static readonly Lock RegistryLock = new();
    private static ComponentType?[] _byId = new ComponentType?[64];
    private static WeakReference<ComponentType>?[] _collectibleById = new WeakReference<ComponentType>?[64];
    private static int _count;

    private readonly Func<int, ComponentStorage> _createStorage;

    private ComponentType(int id, Type type, Func<int, ComponentStorage> createStorage)
    {
        Id = id;
        Type = type;
        _createStorage = createStorage;
    }

    /// <summary>A dense, process-wide id used to index bit sets and storage.</summary>
    public int Id { get; }

    public Type Type { get; }

    /// <summary>The number of component types registered so far.</summary>
    public static int Count => Volatile.Read(ref _count);

    public static ComponentType Of<T>() => ComponentTypeCache<T>.Type;

    /// <exception cref="InvalidOperationException">The type belonged to an assembly that was unloaded.</exception>
    public static ComponentType FromId(int id) => Volatile.Read(ref _byId)[id] ?? FromCollectibleId(id);

    /// <remarks>Types from collectible assemblies, such as plugins and scripts, are held weakly so their assemblies can unload.</remarks>
    internal static ComponentType Register(Type type, Func<int, ComponentStorage> createStorage)
    {
        lock (RegistryLock)
        {
            var id = _count;
            if (id == _byId.Length)
            {
                var byId = _byId;
                Array.Resize(ref byId, id * 2);
                Array.Resize(ref _collectibleById, id * 2);
                Volatile.Write(ref _byId, byId);
            }

            var componentType = new ComponentType(id, type, createStorage);
            if (type.IsCollectible)
                _collectibleById[id] = new WeakReference<ComponentType>(componentType);
            else
                _byId[id] = componentType;
            Volatile.Write(ref _count, id + 1);
            return componentType;
        }
    }

    private static ComponentType FromCollectibleId(int id)
    {
        lock (RegistryLock)
        {
            return _collectibleById[id] is { } reference && reference.TryGetTarget(out var componentType)
                ? componentType
                : throw new InvalidOperationException($"Component type {id} belonged to an assembly that was unloaded.");
        }
    }

    internal ComponentStorage CreateStorage(int capacity) => _createStorage(capacity);

    public override string ToString() => Type.Name;
}

internal static class ComponentTypeCache<T>
{
    public static readonly ComponentType Type = ComponentType.Register(typeof(T), capacity => new ComponentStorage<T>(capacity));

    public static int Id
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Type.Id;
    }
}
