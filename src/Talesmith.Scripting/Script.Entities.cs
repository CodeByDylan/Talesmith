using System.Numerics;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Scripting;

public abstract partial class Script
{
    /// <summary>Gets a reference to a component of the script's entity that can be read and changed in place.</summary>
    /// <exception cref="InvalidOperationException">The entity has no such component.</exception>
    public ref T GetComponent<T>() => ref GetComponent<T>(Entity);

    /// <summary>Gets a reference to a component of another entity that can be read and changed in place.</summary>
    /// <exception cref="InvalidOperationException">The entity is not alive or has no such component.</exception>
    public ref T GetComponent<T>(Entity entity)
    {
        ref T component = ref World.TryGetRef<T>(entity, out var exists)!;
        if (!exists)
            throw new InvalidOperationException($"{ScriptRuntime.Describe(World, entity)} has no {typeof(T).Name} component.");
        return ref component;
    }

    /// <summary>Copies a component of the script's entity, if it has one.</summary>
    public bool TryGetComponent<T>(out T component) => TryGetComponent(Entity, out component);

    /// <summary>Copies a component of another entity, if it is alive and has one.</summary>
    public bool TryGetComponent<T>(Entity entity, out T component) => World.TryGet(entity, out component);

    public bool HasComponent<T>() => HasComponent<T>(Entity);

    /// <summary>Whether another entity is alive and has a component.</summary>
    public bool HasComponent<T>(Entity entity) => World.IsAlive(entity) && World.Has<T>(entity);

    /// <summary>Adds a component to the script's entity, or replaces it, and returns a reference to the stored component.</summary>
    public ref T AddComponent<T>(in T component) => ref AddComponent(Entity, component);

    /// <summary>Adds a component to another entity, or replaces it, and returns a reference to the stored component.</summary>
    public ref T AddComponent<T>(Entity entity, in T component)
    {
        World.Set(entity, component);
        return ref World.Get<T>(entity);
    }

    /// <summary>Removes a component from the script's entity; returns false when it had none.</summary>
    public bool RemoveComponent<T>() => RemoveComponent<T>(Entity);

    /// <summary>Removes a component from another entity; returns false when it is not alive or had none.</summary>
    public bool RemoveComponent<T>(Entity entity) => World.IsAlive(entity) && World.Remove<T>(entity);

    /// <summary>Gets the first script of the entity that is, derives from or implements <typeparamref name="T"/>, or null.</summary>
    public T? GetScript<T>() where T : class => Owner?.Get<T>();

    /// <summary>Gets the first script of another entity that is, derives from or implements <typeparamref name="T"/>, or null.</summary>
    public T? GetScript<T>(Entity entity) where T : class =>
        World.IsAlive(entity) && World.TryGet<ScriptComponent>(entity, out var scripts) ? scripts.Get<T>() : null;

    public bool TryGetScript<T>(out T script) where T : class => (script = GetScript<T>()!) is not null;

    public bool TryGetScript<T>(Entity entity, out T script) where T : class => (script = GetScript<T>(entity)!) is not null;

    /// <summary>Adds a new script to the script's entity; it is created at the start of the next update phase.</summary>
    public T AddScript<T>() where T : Script, new() => AddScript<T>(Entity);

    /// <summary>Adds a new script to another entity, giving it a <see cref="ScriptComponent"/> when it has none.</summary>
    public T AddScript<T>(Entity entity) where T : Script, new() => (T)AddScript(entity, new T());

    /// <summary>Adds a script instance to an entity, giving it a <see cref="ScriptComponent"/> when it has none.</summary>
    public Script AddScript(Entity entity, Script script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!World.TryGet<ScriptComponent>(entity, out var scripts))
        {
            World.Set(entity, new ScriptComponent(script));
            return script;
        }

        return scripts.Add(script);
    }

    /// <summary>Removes a script from its entity, calling <see cref="OnDisable"/> and <see cref="OnDestroy"/>; returns false when it is not attached.</summary>
    /// <exception cref="InvalidOperationException">The script runs in another scene.</exception>
    public bool RemoveScript(Script script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Runtime is not null && !ReferenceEquals(script.Runtime, Runtime))
            throw new InvalidOperationException($"{script} runs in another scene.");
        return script.Owner?.Remove(script) ?? false;
    }

    /// <summary>Removes the first script of the entity that is, derives from or implements <typeparamref name="T"/>; returns false when it has none.</summary>
    public bool RemoveScript<T>() where T : class => GetScript<T>() is Script script && RemoveScript(script);

    /// <summary>Creates an empty entity with a <see cref="Runtime.Components.Transform"/> at <paramref name="position"/>.</summary>
    public Entity CreateEntity(string? name = null, Vector2 position = default)
    {
        var entity = World.Create(new Transform(position));
        if (!string.IsNullOrEmpty(name))
            World.Set(entity, new Name(name));
        return entity;
    }

    /// <summary>Spawns an instance of a prefab and returns its root entity once its assets are loaded.</summary>
    /// <param name="prefabPath">The prefab's asset path, such as "prefabs/coin.tprefab".</param>
    /// <param name="position">The root's position, relative to <paramref name="parent"/> when one is given; null keeps the prefab's.</param>
    /// <remarks>Prefabs load once, so later spawns of the same prefab finish at the start of the next frame.</remarks>
    public ScriptTask<Entity> Spawn(string prefabPath, Vector2? position = null, Entity parent = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefabPath);
        return new ScriptTask<Entity>(this, RootOf(Running.Prefabs.InstantiateAsync(prefabPath, Spawning(position, parent), DestroyCancellationToken)));
    }

    /// <summary>Spawns an instance of a prefab by its asset guid; see <see cref="Spawn(string, Vector2?, Entity)"/>.</summary>
    public ScriptTask<Entity> Spawn(AssetGuid prefab, Vector2? position = null, Entity parent = default) =>
        new(this, RootOf(Running.Prefabs.InstantiateAsync(prefab, Spawning(position, parent), DestroyCancellationToken)));

    /// <summary>Copies an entity with all its saved components and scripts, which start like newly spawned ones.</summary>
    /// <param name="position">The copy's position; null keeps the original's.</param>
    /// <remarks>Children are not copied; spawn a prefab to create a whole hierarchy.</remarks>
    public Entity Instantiate(Entity original, Vector2? position = null)
    {
        var copy = Running.Clone(original);
        if (position is { } at && World.Has<Transform>(copy))
            World.Get<Transform>(copy).Position = at;
        return copy;
    }

    /// <summary>Destroys the script's entity, its children and their scripts.</summary>
    public void Destroy() => Destroy(Entity);

    /// <summary>Destroys an entity, its children and their scripts; destroying an entity that is not alive does nothing.</summary>
    public void Destroy(Entity entity) => Running.Destroy(entity);

    /// <summary>Finds the first entity with a <see cref="Runtime.Components.Name"/>, or <see cref="Entity.Null"/>.</summary>
    /// <remarks>Looks through every named entity; call it once, such as in <see cref="OnStart"/>, and keep the result.</remarks>
    public Entity Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var archetype in World.Query<Name>())
        {
            var names = archetype.GetSpan<Name>();
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i].Value, name, StringComparison.Ordinal))
                    return archetype.Entities[i];
            }
        }

        return Entity.Null;
    }

    /// <summary>Finds the first entity whose <see cref="Tags"/> contain <paramref name="tag"/>, or <see cref="Entity.Null"/>.</summary>
    public Entity FindWithTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        foreach (var archetype in World.Query<Tags>())
        {
            var tags = archetype.GetSpan<Tags>();
            for (var i = 0; i < tags.Length; i++)
            {
                if (tags[i].Values?.Contains(tag) == true)
                    return archetype.Entities[i];
            }
        }

        return Entity.Null;
    }

    /// <summary>Adds every entity whose <see cref="Tags"/> contain <paramref name="tag"/> to <paramref name="results"/>; returns how many were added.</summary>
    public int FindAllWithTag(string tag, List<Entity> results)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(results);
        var count = 0;
        foreach (var archetype in World.Query<Tags>())
        {
            var tags = archetype.GetSpan<Tags>();
            for (var i = 0; i < tags.Length; i++)
            {
                if (tags[i].Values?.Contains(tag) == true)
                {
                    results.Add(archetype.Entities[i]);
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Gets the tile map shown by the script's entity, through its <see cref="TileMapComponent"/>.</summary>
    /// <exception cref="InvalidOperationException">The entity shows no tile map.</exception>
    public ScriptTileMap GetTileMap() => GetTileMap(Entity);

    /// <summary>Gets the tile map shown by an entity, through its <see cref="TileMapComponent"/>.</summary>
    /// <exception cref="InvalidOperationException">The entity shows no tile map.</exception>
    public ScriptTileMap GetTileMap(Entity mapEntity) =>
        TryGetTileMap(mapEntity, out var map) ? map : throw new InvalidOperationException($"{ScriptRuntime.Describe(World, mapEntity)} shows no tile map.");

    public bool TryGetTileMap(Entity mapEntity, out ScriptTileMap map)
    {
        if (World.IsAlive(mapEntity) && World.TryGet<TileMapComponent>(mapEntity, out var component) && component is not null)
        {
            map = new ScriptTileMap(World, mapEntity, component.Map);
            return true;
        }

        map = default;
        return false;
    }

    /// <summary>Finds the first entity showing a tile map, such as the level, and returns its map.</summary>
    public bool TryFindTileMap(out ScriptTileMap map)
    {
        foreach (var archetype in World.Query<TileMapComponent>())
        {
            if (archetype.Entities.Length > 0)
                return TryGetTileMap(archetype.Entities[0], out map);
        }

        map = default;
        return false;
    }

    private PrefabSpawn Spawning(Vector2? position, Entity parent) => new() { World = World, Position = position, Parent = parent };

    private static async Task<Entity> RootOf(Task<InstantiationResult> spawning) => (await spawning.ConfigureAwait(true)).Root;
}
