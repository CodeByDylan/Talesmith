using System.Numerics;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Scenes;

/// <summary>Where and how gameplay code spawns a prefab instance.</summary>
public sealed record PrefabSpawn
{
    /// <summary>The world to create the instance in; null uses the active scene's world.</summary>
    public World? World { get; init; }

    /// <summary>The root's position, relative to <see cref="Parent"/> when set; null keeps the prefab's.</summary>
    public Vector2? Position { get; init; }

    /// <summary>The root's rotation in radians, relative to <see cref="Parent"/> when set; null keeps the prefab's.</summary>
    public float? Rotation { get; init; }

    /// <summary>An entity to attach the instance to, or <see cref="Entity.Null"/>.</summary>
    public Entity Parent { get; init; }

    /// <summary>The root's name; null keeps the prefab's.</summary>
    public string? Name { get; init; }

    /// <summary>The root's id; null creates a new one.</summary>
    public Guid? InstanceId { get; init; }
}

/// <summary>Spawns prefabs from gameplay code, such as enemies or projectiles.</summary>
/// <remarks>Prefabs and their assets load once and stay cached, so later spawns of the same prefab complete without waiting.</remarks>
public interface IPrefabService
{
    /// <exception cref="AssetException">The prefab is not in the catalog or could not be loaded.</exception>
    Task<InstantiationResult> InstantiateAsync(AssetGuid prefab, PrefabSpawn? spawn = null, CancellationToken cancellationToken = default);

    /// <param name="path">The prefab's asset path, such as "prefabs/slime.tprefab".</param>
    /// <exception cref="AssetException">The prefab could not be loaded.</exception>
    Task<InstantiationResult> InstantiateAsync(string path, PrefabSpawn? spawn = null, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IPrefabService"/>, built on <see cref="SceneInstantiator"/>.</summary>
public sealed class PrefabService(SceneInstantiator instantiator, ISceneManager scenes, IAssetManager assets, IAssetCatalog catalog, AssetReferences references)
    : IPrefabService
{
    private readonly TransformHierarchy _hierarchy = new();

    public async Task<InstantiationResult> InstantiateAsync(AssetGuid prefab, PrefabSpawn? spawn = null, CancellationToken cancellationToken = default)
    {
        if (!catalog.TryGetPath(prefab, out var path))
            throw new AssetException($"The prefab {prefab} is not in the asset catalog.");
        return await InstantiateAsync(path, prefab, spawn, cancellationToken);
    }

    public Task<InstantiationResult> InstantiateAsync(string path, PrefabSpawn? spawn = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        catalog.TryGetGuid(path, out var guid);
        return InstantiateAsync(path, guid, spawn, cancellationToken);
    }

    private async Task<InstantiationResult> InstantiateAsync(string path, AssetGuid guid, PrefabSpawn? spawn, CancellationToken cancellationToken)
    {
        spawn ??= new PrefabSpawn();
        var world = spawn.World ?? scenes.Current?.World ?? throw new InvalidOperationException("No scene is active, so a prefab has no world to spawn in. Pass PrefabSpawn.World.");
        var prefab = await assets.LoadAsync<PrefabDocument>(path, cancellationToken);
        if (!guid.IsEmpty)
            references.Register(prefab, guid);

        var result = await instantiator.InstantiateAsync(world, prefab, new PrefabInstantiation
        {
            InstanceId = spawn.InstanceId ?? Guid.NewGuid(),
            Asset = guid,
            Name = spawn.Name,
            Parent = spawn.Parent
        }, cancellationToken);

        if (result.Root is { IsNull: false } root && (spawn.Position is not null || spawn.Rotation is not null))
        {
            Place(world, root, spawn);
            _hierarchy.Update(world);
        }

        return result;
    }

    private static void Place(World world, Entity root, PrefabSpawn spawn)
    {
        if (world.Has<Parent>(root))
        {
            ref var local = ref world.Get<LocalTransform>(root);
            local.Position = spawn.Position ?? local.Position;
            local.Rotation = spawn.Rotation ?? local.Rotation;
            return;
        }

        if (!world.Has<Transform>(root))
            world.Set(root, new Transform());
        ref var transform = ref world.Get<Transform>(root);
        transform.Position = spawn.Position ?? transform.Position;
        transform.Rotation = spawn.Rotation ?? transform.Rotation;
        ref var camera = ref world.TryGetRef<Camera>(root, out var hasCamera);
        if (hasCamera)
            camera.View.Position = transform.Position;
    }
}
