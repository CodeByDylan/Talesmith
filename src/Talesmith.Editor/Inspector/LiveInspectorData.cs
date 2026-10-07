using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Editor.PlayMode;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Inspector;

/// <summary>A component of a play world entity, captured as saved JSON.</summary>
public sealed record LiveComponent(string Type, JsonObject Data);

/// <summary>A play world entity as the inspector shows it, copied on the game thread.</summary>
/// <param name="DocumentId">The id of the scene entity it was created for, or <see cref="Guid.Empty"/> for spawned entities.</param>
public sealed record LiveEntitySnapshot(string Name, Guid DocumentId, IReadOnlyList<LiveComponent> Components)
{
    /// <summary>The components in the order of the scene entity's, with the transform first and others after.</summary>
    public LiveEntitySnapshot OrderedLike(IList<string> documentOrder)
    {
        ArgumentNullException.ThrowIfNull(documentOrder);
        int Rank(LiveComponent component) =>
            component.Type == "Transform" ? -1 : documentOrder.IndexOf(component.Type) is var index and >= 0 ? index : int.MaxValue;
        return this with { Components = [.. Components.OrderBy(Rank)] };
    }

    /// <summary>The component types in order, which decide the inspector's sections.</summary>
    public string Signature => string.Join('|', Components.Select(c => c.Type));
}

/// <summary>An entity of the play session: its components are captured from the play world through <see cref="IPlayModeService.InvokeAsync{T}"/>
/// and edits are put on it through the component definitions with <see cref="IPlayModeService.Dispatch"/>, so they never reach the scene
/// document and end with Stop.</summary>
public sealed class LiveInspectorData : InspectorData
{
    private static readonly TimeSpan EditHold = TimeSpan.FromMilliseconds(400);
    private static readonly ConcurrentDictionary<Type, Func<IAssetManager, string, Task<object>>> Loaders = new();

    private readonly IPlayModeService _play;
    private readonly Dictionary<string, JsonObject> _data = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _editedAt = new(StringComparer.Ordinal);

    public LiveInspectorData(IPlayModeService play, Entity entity, LiveEntitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _play = play;
        Entity = entity;
        Snapshot = snapshot;
        foreach (var component in snapshot.Components)
            _data[component.Type] = component.Data;
    }

    public Entity Entity { get; }

    /// <summary>The last captured state.</summary>
    public LiveEntitySnapshot Snapshot { get; private set; }

    public override int Count => 1;

    public override bool IsLive => true;

    /// <summary>Copies an entity of a game's current scene, or null when it no longer exists; call on the game thread.</summary>
    public static LiveEntitySnapshot? Capture(Game game, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Scenes.Current?.World is not { } world || !world.IsAlive(entity))
            return null;
        var registry = game.Services.GetRequiredService<ComponentRegistry>();
        var context = new CaptureContext(world, game.Services.GetRequiredService<AssetReferences>());
        var definitions = new List<IComponentDefinition>();
        foreach (var id in world.GetComponentIds(entity))
        {
            if (registry.TryGet(ComponentType.FromId(id).Type, out var definition) && !definition.Info.Hidden)
                definitions.Add(definition);
        }

        var order = registry.Definitions;
        definitions.Sort((a, b) => IndexOf(order, a).CompareTo(IndexOf(order, b)));
        var components = new List<LiveComponent>(definitions.Count);
        foreach (var definition in definitions)
        {
            try
            {
                if (definition.CaptureEffective(world, entity, context) is { } data)
                    components.Add(new LiveComponent(definition.TypeName, data));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                game.Services.GetService<ILoggerFactory>()?.CreateLogger<LiveInspectorData>().LogLiveCaptureFailed(ex, definition.TypeName);
            }
        }

        var name = world.TryGet<Name>(entity, out var named) && !string.IsNullOrEmpty(named.Value) ? named.Value : $"Entity {entity.Id}";
        var documentId = world.TryGet<SceneEntityId>(entity, out var sceneId) ? sceneId.Value : Guid.Empty;
        return new LiveEntitySnapshot(name, documentId, components);
    }

    public override JsonNode? Get(int target, string component, string path)
    {
        if (!_data.TryGetValue(component, out var data))
            return null;
        return path.Length == 0 ? data : JsonPaths.Get(data, path);
    }

    /// <summary>Takes a new capture and notifies the values of components that changed, except ones edited a moment ago, so a drag in progress
    /// does not jump back to an older value.</summary>
    public void Update(LiveEntitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        var now = Stopwatch.GetTimestamp();
        foreach (var component in snapshot.Components)
        {
            if (_editedAt.TryGetValue(component.Type, out var edited) && Stopwatch.GetElapsedTime(edited, now) < EditHold)
                continue;
            if (_data.TryGetValue(component.Type, out var previous) && JsonNode.DeepEquals(previous, component.Data))
                continue;
            _data[component.Type] = component.Data;
            Notify(component.Type, "");
        }
    }

    public override void Set(string component, string path, JsonNode? value)
    {
        if (!_data.TryGetValue(component, out var current))
            return;
        JsonObject updated;
        if (path.Length == 0)
        {
            if (value is not JsonObject whole)
                return;
            updated = (JsonObject)whole.DeepClone();
        }
        else
        {
            updated = (JsonObject)current.DeepClone();
            JsonPaths.Set(updated, path, value?.DeepClone());
        }

        _data[component] = updated;
        _editedAt[component] = Stopwatch.GetTimestamp();
        Notify(component, path);
        var entity = Entity;
        var data = (JsonObject)updated.DeepClone();
        var play = _play;
        play.Dispatch(game => Apply(play, game, entity, component, data));
    }

    /// <summary>Puts component data on a play world entity once the assets it references are loaded; call on the game thread.</summary>
    private static void Apply(IPlayModeService play, Game game, Entity entity, string component, JsonObject data)
    {
        var registry = game.Services.GetRequiredService<ComponentRegistry>();
        if (!registry.TryGet(component, out var definition))
            return;
        var catalog = game.Services.GetRequiredService<IAssetCatalog>();
        var assets = game.Services.GetRequiredService<IAssetManager>();
        var loads = new Dictionary<AssetGuid, Task<object>>();
        foreach (var dependency in definition.GetDependencies(data))
        {
            if (!loads.ContainsKey(dependency.Guid) && catalog.TryGetPath(dependency.Guid, out var path))
                loads[dependency.Guid] = Loaders.GetOrAdd(dependency.AssetType, CreateLoader)(assets, path);
        }

        if (loads.Values.All(t => t.IsCompleted))
        {
            ApplyLoaded(game, entity, definition, data, loads);
            return;
        }

        _ = Task.WhenAll(loads.Values).ContinueWith(_ => play.Dispatch(g =>
        {
            if (ReferenceEquals(g, game))
                ApplyLoaded(g, entity, definition, data, loads);
        }), TaskScheduler.Default);
    }

    private static void ApplyLoaded(Game game, Entity entity, IComponentDefinition definition, JsonObject data, Dictionary<AssetGuid, Task<object>> loads)
    {
        if (game.Scenes.Current?.World is not { } world || !world.IsAlive(entity))
            return;
        try
        {
            definition.Apply(world, entity, data, new InstantiationContext(game, world, loads));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            game.Services.GetService<ILoggerFactory>()?.CreateLogger<LiveInspectorData>().LogLiveEditFailed(ex, definition.TypeName);
        }
    }

    private static int IndexOf(IReadOnlyList<IComponentDefinition> list, IComponentDefinition definition)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], definition))
                return i;
        }

        return int.MaxValue;
    }

    private static Func<IAssetManager, string, Task<object>> CreateLoader(Type type) =>
        typeof(LiveInspectorData).GetMethod(nameof(LoadTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(type).CreateDelegate<Func<IAssetManager, string, Task<object>>>();

    private static async Task<object> LoadTyped<T>(IAssetManager assets, string path)
        where T : class =>
        await assets.LoadAsync<T>(path);

    private sealed class CaptureContext(World world, AssetReferences references) : ICaptureContext
    {
        public AssetGuid GetGuid(object asset) => references.GetGuid(asset);

        public Guid GetEntityId(Entity entity) => world.IsAlive(entity) && world.TryGet<SceneEntityId>(entity, out var id) ? id.Value : Guid.Empty;
    }

    private sealed class InstantiationContext(Game game, World world, Dictionary<AssetGuid, Task<object>> loads) : IInstantiationContext
    {
        public IServiceProvider Services => game.Services;

        public T? GetAsset<T>(AssetGuid guid)
            where T : class =>
            loads.TryGetValue(guid, out var load) && load.IsCompletedSuccessfully ? load.Result as T : null;

        public Entity GetEntity(Guid id) => LiveSelection.FindByDocumentId(game, id) is var entity && world.IsAlive(entity) ? entity : Entity.Null;
    }
}

internal static partial class LiveInspectorLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the live {Component} failed")]
    public static partial void LogLiveCaptureFailed(this ILogger logger, Exception exception, string component);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Applying the live edit of {Component} failed")]
    public static partial void LogLiveEditFailed(this ILogger logger, Exception exception, string component);
}
