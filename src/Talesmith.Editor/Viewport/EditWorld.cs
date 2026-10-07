using System.Collections.Concurrent;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Viewport;

/// <summary>The default <see cref="IEditWorld"/>: loads the open scene into the edit game, then applies each document change to the affected
/// entities only.</summary>
/// <remarks>
/// Changes are applied in order. A change whose assets are loaded already is applied immediately, within the edit that made it; otherwise it
/// waits for its assets, and the changes after it wait too. Applying reads the current document, so applying a change twice is harmless.
/// </remarks>
public sealed partial class EditWorld : IEditWorld, IDisposable
{
    private const string DocumentKey = "editor.scene";

    private static readonly TimeSpan StartFrameTime = TimeSpan.FromSeconds(1.0 / 60);

    private static readonly ConcurrentDictionary<Type, Func<IAssetManager, string, CancellationToken, Task<object>>> Loaders = new();

    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly ILogger<EditWorld> _logger;
    private readonly Dictionary<Guid, Entity> _entities = [];
    private Dictionary<AssetDependency, object?> _assets = [];
    private SceneDocumentModel? _model;
    private SceneDocumentModel? _loaded;
    private Task _tail = Task.CompletedTask;
    private int _queued;
    private bool _reloadQueued;
    private bool _awaitingStartScene;
    private IDisposable? _sceneLoaded;
    private Game? _worldGame;
    private DispatcherTimer? _starter;

    /// <summary>Cancelled when the edit game is replaced, so the queue stops waiting for work on that game: it never runs another frame.</summary>
    private CancellationTokenSource _replaced = new();

    public EditWorld(IProjectService project, ISceneDocumentService documents, ILogger<EditWorld> logger)
    {
        _project = project;
        _documents = documents;
        _logger = logger;
        _documents.ActiveChanged += (_, _) => Track();
        _project.StatusChanged += (_, _) => Track();
        _project.EditSessionChanged += OnEditSessionChanged;
        Track();
    }

    /// <remarks>While a new edit game loads the scene, this stays the game whose world is shown.</remarks>
    public Game? Game => World is not null ? _worldGame : _project.EditSession?.Game;

    public World? World { get; private set; }

    public bool IsBusy => _queued > 0 || _awaitingStartScene;

    public int EntityCount => World?.EntityCount ?? 0;

    public Task WhenIdle => _tail;

    public event EventHandler? Changed;

    public bool TryGetEntity(Guid documentId, out Entity entity)
    {
        if (World is { } world && _entities.TryGetValue(documentId, out entity) && world.IsAlive(entity))
            return true;
        entity = Entity.Null;
        return false;
    }

    public bool TryGetDocumentId(Entity entity, out Guid documentId)
    {
        documentId = Guid.Empty;
        if (World is not { } world || _model is not { } model)
            return false;
        for (var current = entity; !current.IsNull && world.IsAlive(current);)
        {
            if (world.TryGet<SceneEntityId>(current, out var id) && model.Contains(id.Value))
            {
                documentId = id.Value;
                return true;
            }

            current = world.TryGet<Parent>(current, out var parent) ? parent.Value : Entity.Null;
        }

        return false;
    }

    public void Dispose()
    {
        _starter?.Stop();
        _project.EditSessionChanged -= OnEditSessionChanged;
        _sceneLoaded?.Dispose();
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = null;
        _replaced.Dispose();
    }

    private void Track()
    {
        var active = _documents.Active;
        if (_project.EditSession?.Game is not { } game || ReferenceEquals(active, _model))
            return;
        _sceneLoaded ??= game.Services.GetRequiredService<IEventBus>().Subscribe<SceneLoaded>(OnSceneLoaded);
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = active;
        if (active is null)
            return;
        active.Changed += OnSceneChanged;
        if (game.Scenes.Current is null)
        {
            _awaitingStartScene = true;
            game.Start();
            RunUntilStarted(game);
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Enqueue(() => ReloadAsync(active));
    }

    /// <summary>Loads the open scene once the game's own start scene is in, since that start scene replaces whatever loaded before it: into a new
    /// edit game, or into the first one when the scene opened before the game started.</summary>
    /// <remarks>
    /// This runs inside the game's frame, so the reload starts after the frame: started here, it would resume only in the game's next frame,
    /// which never comes when another edit game replaces this one first, and every later change would wait behind it.
    /// </remarks>
    private void OnSceneLoaded(ref SceneLoaded e)
    {
        if (e.Scene is not EmptyScene)
            return;
        _awaitingStartScene = false;
        if (_model is not { } model)
            return;
        if (ReferenceEquals(_worldGame, _project.EditSession?.Game))
        {
            World = null;
            _entities.Clear();
        }

        _loaded = null;
        _queued++;
        Dispatcher.UIThread.Post(() =>
        {
            _queued--;
            Enqueue(() => ReloadAsync(model));
        });
    }

    /// <summary>Loads the open scene into the new edit game once its start scene loaded, and shows the old world until then.</summary>
    private void OnEditSessionChanged(object? sender, EditSessionChangedEventArgs e)
    {
        _sceneLoaded?.Dispose();
        _sceneLoaded = e.Current.Game.Services.GetRequiredService<IEventBus>().Subscribe<SceneLoaded>(OnSceneLoaded);
        _assets = [];
        _loaded = null;
        e.Current.Game.Start();
        RunUntilStarted(e.Current.Game);
        var replaced = _replaced;
        _replaced = new CancellationTokenSource();
        replaced.Cancel();
        replaced.Dispose();
    }

    /// <summary>Runs frames of an edit game until its start scene is in, whenever nothing else runs them, as when the Scene panel is not built
    /// because another panel fills the window; the open scene waits for that start scene.</summary>
    private void RunUntilStarted(Game game)
    {
        _starter?.Stop();
        var frame = game.FrameCount;
        var starter = new DispatcherTimer(DispatcherPriority.Background) { Interval = StartFrameTime };
        starter.Tick += (_, _) =>
        {
            if (game.WhenStarted.IsCompleted || !ReferenceEquals(game, _project.EditSession?.Game))
            {
                starter.Stop();
                return;
            }

            if (game.FrameCount == frame)
                game.Tick(StartFrameTime.TotalSeconds);
            frame = game.FrameCount;
        };
        _starter = starter;
        starter.Start();
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        var model = (SceneDocumentModel)sender!;
        var change = e.Change;
        if (change.Kind is SceneChangeKind.Reloaded or SceneChangeKind.EnvironmentChanged)
        {
            if (_reloadQueued)
                return;
            _reloadQueued = true;
            Enqueue(() =>
            {
                _reloadQueued = false;
                return ReloadAsync(model);
            });
        }
        else
            Enqueue(() => ApplyAsync(model, change));
    }

    private void Enqueue(Func<Task> operation)
    {
        if (_tail.IsCompleted)
        {
            Task task;
            try
            {
                task = operation();
            }
            catch (Exception ex)
            {
                LogApplyFailed(_logger, ex);
                return;
            }

            if (task.IsCompleted)
            {
                Observe(task);
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            _queued++;
            _tail = Finish(task);
            return;
        }

        _queued++;
        _tail = Chain(_tail, operation);
    }

    private async Task Chain(Task previous, Func<Task> operation)
    {
        await previous;
        await Finish(operation());
    }

    private async Task Finish(Task task)
    {
        var replaced = _replaced.Token;
        try
        {
            await task.WaitAsync(replaced);
        }
        catch (OperationCanceledException) when (replaced.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogApplyFailed(_logger, ex);
        }
        finally
        {
            _queued--;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Observe(Task task)
    {
        if (task.Exception is { } error)
            LogApplyFailed(_logger, error.GetBaseException());
    }

    private async Task ReloadAsync(SceneDocumentModel model)
    {
        if (!ReferenceEquals(model, _model) || _project.EditSession?.Game is not { } game)
            return;
        var memory = game.Services.GetRequiredService<InMemorySceneDocuments>();
        memory.Remove(DocumentKey);
        var request = memory.Add(model.Document.Clone(), DocumentKey);
        await game.Scenes.LoadAsync(request, SceneTransition.Instant);
        if (!ReferenceEquals(game, _project.EditSession?.Game))
            return;
        _entities.Clear();
        World = null;
        if (game.Scenes.Current is DocumentScene { Entities: { } entities } scene)
        {
            World = scene.World;
            _worldGame = game;
            foreach (var (id, entity) in entities.Entities)
                _entities[id] = entity;
            foreach (var entity in model.Entities.Where(e => e.Editor.Hidden))
                UpdateActive(entity);
        }

        _loaded = model;
    }

    private async Task ApplyAsync(SceneDocumentModel model, SceneChange change)
    {
        if (!ReferenceEquals(model, _loaded) || World is not { } world || Game is not { } game)
            return;
        var registry = game.Services.GetRequiredService<ComponentRegistry>();
        var document = model.Find(change.Entity);
        switch (change.Kind)
        {
            case SceneChangeKind.EntityAdded when document is not null && !_entities.ContainsKey(document.Id):
                if (document.Prefab is not null)
                {
                    await InstantiateAsync(world, game, model, document);
                    return;
                }

                await LoadAssetsAsync(game, registry, document.Components);
                if (ReferenceEquals(model, _loaded) && model.Contains(document.Id) && !_entities.ContainsKey(document.Id))
                    Create(world, game, registry, document);
                return;
            case SceneChangeKind.EntityReplaced when document is not null:
                if (_entities.TryGetValue(document.Id, out var replaced))
                    Destroy(world, registry, replaced);
                _entities.Remove(document.Id);
                await ApplyAsync(model, change with { Kind = SceneChangeKind.EntityAdded });
                return;
            case SceneChangeKind.EntityRemoved:
                if (_entities.Remove(change.Entity, out var removed))
                    Destroy(world, registry, removed);
                return;
            case SceneChangeKind.EntityMoved when document is not null && _entities.TryGetValue(document.Id, out var moved):
                SetParent(world, moved, document);
                ApplyComponent(world, game, registry, moved, document.FindComponent("Transform"));
                return;
            case SceneChangeKind.EntityRenamed when document is not null && _entities.TryGetValue(document.Id, out var renamed):
                world.Set(renamed, new Name(document.Name));
                return;
            case SceneChangeKind.EntityStateChanged when document is not null:
                UpdateActive(document);
                return;
            case SceneChangeKind.ComponentAdded or SceneChangeKind.PropertyChanged when document is not null && _entities.TryGetValue(document.Id, out var target):
                var component = document.FindComponent(change.Component!);
                if (component is null)
                    return;
                await LoadAssetsAsync(game, registry, [component]);
                if (ReferenceEquals(model, _loaded) && world.IsAlive(target))
                    ApplyComponent(world, game, registry, target, document.FindComponent(change.Component!));
                return;
            case SceneChangeKind.ComponentRemoved when _entities.TryGetValue(change.Entity, out var owner) && world.IsAlive(owner):
                if (registry.TryGet(change.Component!, out var definition))
                    definition.Remove(world, owner);
                return;
        }
    }

    private void Create(World world, Game game, ComponentRegistry registry, EntityDocument document)
    {
        var entity = world.Create();
        _entities[document.Id] = entity;
        world.Set(entity, new SceneEntityId(document.Id));
        if (!string.IsNullOrEmpty(document.Name))
            world.Set(entity, new Name(document.Name));
        SetParent(world, entity, document);
        if (!document.Active || document.Editor.Hidden)
            world.Set(entity, new Inactive());
        foreach (var component in document.Components)
            ApplyComponent(world, game, registry, entity, component);
    }

    private async Task InstantiateAsync(World world, Game game, SceneDocumentModel model, EntityDocument document)
    {
        var subtree = model.GetSubtree(document.Id).Select(e => e.Clone()).ToList();
        subtree[0].Parent = null;
        var instantiator = game.Services.GetRequiredService<SceneInstantiator>();
        var result = await instantiator.InstantiateAsync(world, subtree);
        if (!ReferenceEquals(model, _loaded))
            return;
        foreach (var (id, entity) in result.Entities)
            _entities[id] = entity;
        if (_entities.TryGetValue(document.Id, out var root))
        {
            SetParent(world, root, document);
            ApplyComponent(world, game, game.Services.GetRequiredService<ComponentRegistry>(), root, document.FindComponent("Transform"));
        }
    }

    private void SetParent(World world, Entity entity, EntityDocument document)
    {
        if (document.Parent is { } parentId && _entities.TryGetValue(parentId, out var parent) && world.IsAlive(parent))
        {
            world.Set(entity, new Parent(parent));
            if (!world.Has<LocalTransform>(entity))
                world.Set(entity, new LocalTransform());
        }
        else
        {
            world.Remove<Parent>(entity);
            world.Remove<LocalTransform>(entity);
        }
    }

    private void UpdateActive(EntityDocument document)
    {
        if (World is not { } world || !_entities.TryGetValue(document.Id, out var entity) || !world.IsAlive(entity))
            return;
        if (!document.Active || document.Editor.Hidden)
            world.Set(entity, new Inactive());
        else
            world.Remove<Inactive>(entity);
    }

    private void ApplyComponent(World world, Game game, ComponentRegistry registry, Entity entity, ComponentDocument? component)
    {
        if (component is null || !registry.TryGet(component.Type, out var definition))
            return;
        try
        {
            definition.Apply(world, entity, component.Data, new Context(this, game.Services));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogComponentFailed(_logger, ex, component.Type);
        }
    }

    private static void Destroy(World world, ComponentRegistry registry, Entity entity)
    {
        if (!world.IsAlive(entity))
            return;
        var children = new List<Entity>();
        foreach (var archetype in world.Query<Parent>())
        {
            var parents = archetype.GetSpan<Parent>();
            for (var i = 0; i < parents.Length; i++)
            {
                if (parents[i].Value == entity)
                    children.Add(archetype.Entities[i]);
            }
        }

        foreach (var child in children)
            Destroy(world, registry, child);
        foreach (var id in world.GetComponentIds(entity).ToArray())
        {
            if (registry.TryGet(ComponentType.FromId(id).Type, out var definition) && world.IsAlive(entity))
                definition.Remove(world, entity);
        }

        if (world.IsAlive(entity))
            world.Destroy(entity);
    }

    private Task LoadAssetsAsync(Game game, ComponentRegistry registry, IEnumerable<ComponentDocument> components)
    {
        List<AssetDependency>? missing = null;
        foreach (var component in components)
        {
            if (!registry.TryGet(component.Type, out var definition))
                continue;
            foreach (var dependency in definition.GetDependencies(component.Data))
            {
                if (!_assets.ContainsKey(dependency))
                    (missing ??= []).Add(dependency);
            }
        }

        return missing is null ? Task.CompletedTask : LoadAsync(game, missing);
    }

    private async Task LoadAsync(Game game, List<AssetDependency> dependencies)
    {
        var loaded = _assets;
        var assets = game.Services.GetRequiredService<IAssetManager>();
        var catalog = game.Services.GetRequiredService<IAssetCatalog>();
        var references = game.Services.GetRequiredService<AssetReferences>();
        foreach (var dependency in dependencies.Distinct())
        {
            if (!catalog.TryGetPath(dependency.Guid, out var path))
            {
                loaded[dependency] = null;
                continue;
            }

            try
            {
                var asset = await Loaders.GetOrAdd(dependency.AssetType, CreateLoader)(assets, path, CancellationToken.None);
                references.Register(asset, dependency.Guid);
                loaded[dependency] = asset;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogAssetFailed(_logger, ex, path);
                loaded[dependency] = null;
            }
        }
    }

    private static Func<IAssetManager, string, CancellationToken, Task<object>> CreateLoader(Type type) =>
        typeof(EditWorld).GetMethod(nameof(LoadTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(type).CreateDelegate<Func<IAssetManager, string, CancellationToken, Task<object>>>();

    private static async Task<object> LoadTyped<T>(IAssetManager assets, string path, CancellationToken cancellationToken)
        where T : class =>
        await assets.LoadAsync<T>(path, cancellationToken);

    [LoggerMessage(Level = LogLevel.Error, Message = "A scene change could not be applied to the viewport")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Component} component could not be applied in the viewport")]
    private static partial void LogComponentFailed(ILogger logger, Exception exception, string component);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The asset {Path} could not be loaded for the viewport")]
    private static partial void LogAssetFailed(ILogger logger, Exception exception, string path);

    private sealed class Context(EditWorld owner, IServiceProvider services) : IInstantiationContext
    {
        public IServiceProvider Services { get; } = services;

        public T? GetAsset<T>(AssetGuid guid)
            where T : class
        {
            if (owner._assets.TryGetValue(new AssetDependency(guid, typeof(T)), out var exact))
                return exact as T;
            foreach (var (dependency, asset) in owner._assets)
            {
                if (dependency.Guid == guid && asset is T typed)
                    return typed;
            }

            return null;
        }

        public Entity GetEntity(Guid id) => owner._entities.GetValueOrDefault(id, Entity.Null);
    }
}
