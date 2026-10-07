using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Scenes;

/// <summary>How to create an instance of a prefab.</summary>
public sealed record PrefabInstantiation
{
    /// <summary>The id of the instance's root entity; the ids of the other entities derive from it.</summary>
    public Guid InstanceId { get; init; } = Guid.NewGuid();

    /// <summary>The prefab's asset guid, recorded in the root's <see cref="PrefabInstance"/> component.</summary>
    public AssetGuid Asset { get; init; }

    /// <summary>The root entity's name; null keeps the prefab's.</summary>
    public string? Name { get; init; }

    /// <summary>An existing entity to make the instance's root a child of, or <see cref="Entity.Null"/>.</summary>
    public Entity Parent { get; init; }

    /// <summary>Property values that differ from the prefab.</summary>
    public IReadOnlyList<PrefabOverride> Overrides { get; init; } = [];
}

/// <summary>The entities created from a document.</summary>
public sealed class InstantiationResult(IReadOnlyDictionary<Guid, Entity> entities, IReadOnlyList<Entity> roots)
{
    /// <summary>The created entity of every document entity id, including the expanded entities of prefab instances.</summary>
    public IReadOnlyDictionary<Guid, Entity> Entities { get; } = entities;

    /// <summary>The created entities without a parent in the document, in document order.</summary>
    public IReadOnlyList<Entity> Roots { get; } = roots;

    /// <summary>The first root, which for a prefab instance is the instance's root entity.</summary>
    public Entity Root => Roots.Count > 0 ? Roots[0] : Entity.Null;
}

/// <summary>Creates the entities of scenes and prefabs: expands prefab instances, loads every asset they use, then creates and fills the entities.</summary>
/// <remarks>
/// Prefabs and assets load in parallel on the thread pool before the world is touched, so a world never holds a half-loaded scene;
/// entities are created where the call resumes, which in a game is the game thread. Prefab instances are expanded recursively: each
/// prefab entity gets an id derived from the instance id (see <see cref="PrefabIds"/>), then the instance's overrides are applied.
/// Entity references inside prefabs resolve within their instance first. Missing prefabs, assets and component types are logged and
/// skipped. Every created entity gets a <see cref="SceneEntityId"/> and, when named, a <see cref="Name"/>; children get a
/// <see cref="Parent"/> and <see cref="LocalTransform"/>, and world transforms are computed before returning.
/// </remarks>
public sealed class SceneInstantiator(ComponentRegistry components, IAssetCatalog catalog, IAssetManager assets, AssetReferences references,
    IServiceProvider services, ILogger<SceneInstantiator> logger)
{
    private static readonly ConcurrentDictionary<Type, Func<IAssetManager, string, CancellationToken, Task<object>>> Loaders = new();

    public ComponentRegistry Components { get; } = components;

    private ILogger Logger => logger;

    public Task<InstantiationResult> InstantiateAsync(World world, SceneDocument scene, CancellationToken cancellationToken = default) =>
        InstantiateAsync(world, scene, progress: null, cancellationToken);

    /// <summary>Creates a scene's entities, adding the assets they use to <paramref name="progress"/> and advancing it as each one loads.</summary>
    public Task<InstantiationResult> InstantiateAsync(World world, SceneDocument scene, SceneLoadProgress? progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return InstantiateDocumentsAsync(world, scene.Entities, progress, cancellationToken);
    }

    /// <summary>Creates entities from documents in hierarchy order, such as a scene's entities or pasted ones.</summary>
    public Task<InstantiationResult> InstantiateAsync(World world, IReadOnlyList<EntityDocument> entities, CancellationToken cancellationToken = default) =>
        InstantiateDocumentsAsync(world, entities, progress: null, cancellationToken);

    /// <summary>Creates an instance of a prefab document.</summary>
    public async Task<InstantiationResult> InstantiateAsync(World world, PrefabDocument prefab, PrefabInstantiation? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(prefab);
        options ??= new PrefabInstantiation();
        var instance = new EntityDocument
        {
            Id = options.InstanceId,
            Name = options.Name ?? "",
            Prefab = new PrefabLink { Asset = options.Asset, Overrides = [.. options.Overrides] }
        };
        var expansion = new Expansion(this, cancellationToken);
        var expanded = new List<ExpandedEntity>();
        await expansion.ExpandInstanceAsync(instance, prefab, options.Asset.IsEmpty ? [] : [options.Asset], expanded);
        return await CreateAsync(world, expanded, options.Parent, progress: null, cancellationToken);
    }

    /// <summary>Loads a prefab asset by guid and creates an instance of it.</summary>
    /// <exception cref="AssetException">The prefab is not in the catalog or could not be loaded.</exception>
    public async Task<InstantiationResult> InstantiateAsync(World world, AssetGuid prefab, PrefabInstantiation? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!catalog.TryGetPath(prefab, out var path))
            throw new AssetException($"The prefab {prefab} is not in the asset catalog.");
        var document = await assets.LoadAsync<PrefabDocument>(path, cancellationToken);
        references.Register(document, prefab);
        return await InstantiateAsync(world, document, (options ?? new PrefabInstantiation()) with { Asset = prefab }, cancellationToken);
    }

    private async Task<InstantiationResult> InstantiateDocumentsAsync(World world, IReadOnlyList<EntityDocument> entities, SceneLoadProgress? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(entities);
        var expansion = new Expansion(this, cancellationToken);
        var expanded = await expansion.ExpandAsync(entities, []);
        return await CreateAsync(world, expanded, Entity.Null, progress, cancellationToken);
    }

    private async Task<InstantiationResult> CreateAsync(World world, List<ExpandedEntity> expanded, Entity parent, SceneLoadProgress? progress,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadDependenciesAsync(CollectDependencies(expanded), progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Create(world, expanded, loaded, parent);
    }

    private HashSet<AssetDependency> CollectDependencies(List<ExpandedEntity> expanded)
    {
        var dependencies = new HashSet<AssetDependency>();
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in expanded)
        {
            foreach (var component in entity.Components)
            {
                if (Components.TryGet(component.Type, out var definition))
                {
                    foreach (var dependency in definition.GetDependencies(component.Data))
                        dependencies.Add(dependency);
                }
                else if (unknown.Add(component.Type))
                {
                    logger.UnknownComponent(component.Type, entity.Describe());
                }
            }
        }

        return dependencies;
    }

    private async Task<Dictionary<AssetDependency, object>> LoadDependenciesAsync(HashSet<AssetDependency> dependencies, SceneLoadProgress? progress,
        CancellationToken cancellationToken)
    {
        progress?.Expect(dependencies.Count);
        var loads = dependencies.Select(async d =>
        {
            var asset = await LoadAsync(d, cancellationToken).ConfigureAwait(false);
            progress?.Advance();
            return (Dependency: d, Asset: asset);
        }).ToArray();
        var results = await Task.WhenAll(loads);
        var loaded = new Dictionary<AssetDependency, object>(results.Length);
        foreach (var (dependency, asset) in results)
        {
            if (asset is not null)
                loaded[dependency] = asset;
        }

        return loaded;
    }

    private async Task<object?> LoadAsync(AssetDependency dependency, CancellationToken cancellationToken)
    {
        if (!catalog.TryGetPath(dependency.Guid, out var path))
        {
            logger.AssetNotInCatalog(dependency.Guid.ToString());
            return null;
        }

        try
        {
            var load = dependency.AssetType.IsCollectible ? CreateLoader(dependency.AssetType) : Loaders.GetOrAdd(dependency.AssetType, CreateLoader);
            var asset = await load(assets, path, cancellationToken).ConfigureAwait(false);
            references.Register(asset, dependency.Guid);
            return asset;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.AssetLoadFailed(ex, path, dependency.Guid.ToString(), dependency.AssetType.Name);
            return null;
        }
    }

    internal async Task<PrefabDocument?> LoadPrefabAsync(AssetGuid guid, CancellationToken cancellationToken)
    {
        if (!catalog.TryGetPath(guid, out var path))
            return null;
        try
        {
            var prefab = await assets.LoadAsync<PrefabDocument>(path, cancellationToken).ConfigureAwait(false);
            references.Register(prefab, guid);
            return prefab;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.AssetLoadFailed(ex, path, guid.ToString(), nameof(PrefabDocument));
            return null;
        }
    }

    private InstantiationResult Create(World world, List<ExpandedEntity> expanded, Dictionary<AssetDependency, object> loaded, Entity externalParent)
    {
        var ids = new Dictionary<Guid, Entity>(expanded.Count);
        var created = new List<(ExpandedEntity Source, Entity Entity)>(expanded.Count);
        var roots = new List<Entity>();
        foreach (var source in expanded)
        {
            if (ids.ContainsKey(source.Id))
                continue;
            var entity = world.Create();
            ids[source.Id] = entity;
            created.Add((source, entity));
        }

        foreach (var (source, entity) in created)
        {
            world.Set(entity, new SceneEntityId(source.Id));
            if (!string.IsNullOrEmpty(source.Name))
                world.Set(entity, new Name(source.Name));
            if (!source.Active)
                world.Set(entity, new Inactive());
            if (!source.Prefab.IsEmpty)
                world.Set(entity, new PrefabInstance(source.Prefab));

            var parent = Entity.Null;
            if (source.Parent is { } parentId)
            {
                if (!ids.TryGetValue(parentId, out parent))
                {
                    logger.ParentMissing(source.Describe(), parentId.ToString("N"));
                    roots.Add(entity);
                }
            }
            else
            {
                roots.Add(entity);
                if (!externalParent.IsNull && world.IsAlive(externalParent))
                    parent = externalParent;
            }

            if (!parent.IsNull)
            {
                world.Set(entity, new Parent(parent));
                world.Set(entity, new LocalTransform());
            }
        }

        var context = new InstantiationContext(loaded, ids, services);
        foreach (var (source, entity) in created)
        {
            foreach (var component in source.Components)
            {
                if (!Components.TryGet(component.Type, out var definition))
                    continue;
                context.Scopes = component.Scopes;
                try
                {
                    definition.Apply(world, entity, component.Data, context);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.ComponentApplyFailed(ex, component.Type, source.Describe());
                }
            }
        }

        new TransformHierarchy().Update(world);
        return new InstantiationResult(ids, roots);
    }

    private static Func<IAssetManager, string, CancellationToken, Task<object>> CreateLoader(Type type) =>
        typeof(SceneInstantiator).GetMethod(nameof(LoadTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(type).CreateDelegate<Func<IAssetManager, string, CancellationToken, Task<object>>>();

    private static async Task<object> LoadTyped<T>(IAssetManager assets, string path, CancellationToken cancellationToken) where T : class =>
        await assets.LoadAsync<T>(path, cancellationToken).ConfigureAwait(false);

    /// <summary>Expands prefab instances into plain entities, loading each prefab once.</summary>
    private sealed class Expansion(SceneInstantiator owner, CancellationToken cancellationToken)
    {
        private readonly Dictionary<AssetGuid, Task<PrefabDocument?>> _prefabs = new();

        public async Task<List<ExpandedEntity>> ExpandAsync(IReadOnlyList<EntityDocument> documents, ImmutableHashSet<AssetGuid> enclosing)
        {
            foreach (var document in documents)
            {
                if (document.Prefab is { } link)
                    _ = Load(link.Asset);
            }

            var result = new List<ExpandedEntity>(documents.Count);
            foreach (var document in documents)
            {
                if (document.Prefab is not { } link)
                {
                    result.Add(ExpandedEntity.From(document));
                    continue;
                }

                if (enclosing.Contains(link.Asset))
                {
                    owner.Logger.PrefabCycle(link.Asset.ToString(), Describe(document));
                    result.Add(ExpandedEntity.From(document));
                    continue;
                }

                if (await Load(link.Asset) is not { } prefab)
                {
                    owner.Logger.PrefabMissing(link.Asset.ToString(), Describe(document));
                    result.Add(ExpandedEntity.From(document));
                    continue;
                }

                await ExpandInstanceAsync(document, prefab, enclosing.Add(link.Asset), result);
            }

            return result;
        }

        public async Task ExpandInstanceAsync(EntityDocument instance, PrefabDocument prefab, ImmutableHashSet<AssetGuid> enclosing, List<ExpandedEntity> result)
        {
            var inner = await ExpandAsync(prefab.Entities, enclosing);
            var link = instance.Prefab!;
            var rootId = prefab.Root?.Id;
            var byId = new Dictionary<Guid, ExpandedEntity>(inner.Count);
            foreach (var entity in inner)
                byId.TryAdd(entity.Id, entity);

            foreach (var removed in link.RemovedComponents)
            {
                if (byId.TryGetValue(removed.Entity, out var entity))
                    entity.Components.RemoveAll(c => c.Type == removed.Component);
            }

            foreach (var change in link.Overrides)
            {
                if (byId.TryGetValue(change.Entity, out var entity))
                    ApplyOverride(entity, change);
                else
                    owner.Logger.OverrideTargetMissing(link.Asset.ToString(), change.Entity.ToString("N"));
            }

            if (rootId is not { } root)
            {
                result.Add(ExpandedEntity.From(instance));
                return;
            }

            var scope = new PrefabScope(instance.Id, root);
            foreach (var entity in inner)
            {
                var isRoot = entity.Id == root;
                entity.Id = scope.Map(entity.Id);
                entity.Parent = isRoot ? instance.Parent : entity.Parent is { } parent ? scope.Map(parent) : instance.Id;
                foreach (var component in entity.Components)
                    component.Scopes = [.. component.Scopes, scope];

                if (isRoot)
                {
                    if (!string.IsNullOrEmpty(instance.Name))
                        entity.Name = instance.Name;
                    entity.Active = instance.Active;
                    entity.Prefab = link.Asset;
                    foreach (var own in instance.Components)
                    {
                        entity.Components.RemoveAll(c => c.Type == own.Type);
                        entity.Components.Add(new ExpandedComponent(own.Type, own.Data));
                    }
                }

                result.Add(entity);
            }
        }

        private void ApplyOverride(ExpandedEntity entity, PrefabOverride change)
        {
            var component = entity.Components.Find(c => c.Type == change.Component);
            if (component is null)
            {
                var data = owner.Components.TryGet(change.Component, out var definition) ? definition.CreateDefault() : [];
                component = new ExpandedComponent(change.Component, data) { Owned = true };
                entity.Components.Add(component);
            }
            else if (!component.Owned)
            {
                component.Data = (JsonObject)component.Data.DeepClone();
                component.Owned = true;
            }

            try
            {
                JsonPaths.Set(component.Data, change.Path, change.Value?.DeepClone());
            }
            catch (FormatException ex)
            {
                owner.Logger.OverridePathInvalid(change.Path, change.Component, ex.Message);
            }
        }

        private Task<PrefabDocument?> Load(AssetGuid guid)
        {
            if (!_prefabs.TryGetValue(guid, out var task))
                _prefabs[guid] = task = owner.LoadPrefabAsync(guid, cancellationToken);
            return task;
        }

        private static string Describe(EntityDocument document) => string.IsNullOrEmpty(document.Name) ? document.Id.ToString("N") : document.Name;
    }

    /// <summary>Maps the entity ids of one prefab instance from the prefab's ids to the enclosing document's.</summary>
    private sealed record PrefabScope(Guid InstanceId, Guid RootId)
    {
        public Guid Map(Guid id) => PrefabIds.Map(InstanceId, RootId, id);
    }

    private sealed class ExpandedEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = "";

        public Guid? Parent { get; set; }

        public bool Active { get; set; } = true;

        public AssetGuid Prefab { get; set; }

        public List<ExpandedComponent> Components { get; } = [];

        public static ExpandedEntity From(EntityDocument document)
        {
            var entity = new ExpandedEntity { Id = document.Id, Name = document.Name, Parent = document.Parent, Active = document.Active, Prefab = document.Prefab?.Asset ?? default };
            foreach (var component in document.Components)
                entity.Components.Add(new ExpandedComponent(component.Type, component.Data));
            return entity;
        }

        public string Describe() => string.IsNullOrEmpty(Name) ? Id.ToString("N") : Name;
    }

    /// <summary>Component data in a prefab instance; shared with the prefab document until an override changes it.</summary>
    private sealed class ExpandedComponent(string type, JsonObject data)
    {
        public string Type { get; } = type;

        public JsonObject Data { get; set; } = data;

        /// <summary>Whether <see cref="Data"/> is a copy this instantiation may change.</summary>
        public bool Owned { get; set; }

        /// <summary>The prefab instances the data was written in, innermost first, for resolving entity references.</summary>
        public PrefabScope[] Scopes { get; set; } = [];
    }

    private sealed class InstantiationContext(Dictionary<AssetDependency, object> assets, Dictionary<Guid, Entity> entities, IServiceProvider services)
        : IInstantiationContext
    {
        public PrefabScope[] Scopes { get; set; } = [];

        public IServiceProvider Services { get; } = services;

        public T? GetAsset<T>(AssetGuid guid) where T : class
        {
            if (assets.TryGetValue(new AssetDependency(guid, typeof(T)), out var asset) && asset is T typed)
                return typed;
            foreach (var (dependency, loaded) in assets)
            {
                if (dependency.Guid == guid && loaded is T match)
                    return match;
            }

            return null;
        }

        /// <summary>Resolves an id written inside the innermost prefab first, then in each enclosing document, ending with the outermost.</summary>
        public Entity GetEntity(Guid id)
        {
            var scopes = Scopes;
            for (var start = 0; start <= scopes.Length; start++)
            {
                var candidate = id;
                for (var i = start; i < scopes.Length; i++)
                    candidate = scopes[i].Map(candidate);
                if (entities.TryGetValue(candidate, out var entity))
                    return entity;
            }

            return Entity.Null;
        }
    }
}
