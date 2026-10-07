using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Audio;
using Talesmith.Diagnostics;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Input;
using Talesmith.Physics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Scheduling;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Tweens;
using Talesmith.Systems;
using Talesmith.Time;

namespace Talesmith.Scripting;

/// <summary>Runs the scripts of one scene: tracks attachments, drives the lifecycle, dispatches updates and contacts, and isolates failures.</summary>
/// <remarks>
/// Scripts attach when their <see cref="ScriptComponent"/> is added to an entity while the scene plays. Attached scripts are created at the
/// start of the next update phase (or when the scene starts), started before the next fixed or frame update, and dispatched per type in
/// <see cref="ScriptOrderAttribute"/> order, then creation order. The dispatch loops do not allocate.
/// </remarks>
internal sealed partial class ScriptRuntime : ICollisionListener, IDisposable
{
    private static readonly ProfilerCounter ScriptTimeCounter = ProfilerCounter.Get("Script time", CounterKind.PerFrame, "ms");
    private static readonly ProfilerCounter ScriptCounter = ProfilerCounter.Get("Scripts", CounterKind.Gauge, "scripts");
    private static readonly ComponentType ScriptComponentType = ComponentType.Of<ScriptComponent>();
    private static readonly ComponentType InactiveType = ComponentType.Of<Inactive>();
    private static readonly Predicate<Script> IsDone = static s => (s.State & (ScriptState.Destroyed | ScriptState.Started)) != 0;
    private static readonly Predicate<Script> IsDestroyed = static s => s.IsDestroyed;

    private readonly SceneManager _sceneManager;
    private readonly ScriptTypeRegistry _registry;
    private readonly Profiler _profiler;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly IDisposable[] _subscriptions;
    private readonly List<Script> _pending = [];
    private readonly List<Script> _starting = [];
    private readonly List<Script> _live = [];
    private readonly List<Entity> _deferredDestroys = [];
    private readonly ScriptPhase _fixedUpdate;
    private readonly ScriptPhase _update;
    private readonly ScriptPhase _lateUpdate;
    private readonly Dictionary<ScriptTypeInfo, ILogger> _scriptLoggers = new();
    private IDisposable? _physicsListener;
    private bool _liveDirty;
    private bool _stopped;
    private IInputService? _input;
    private IAssetManager? _assets;
    private IAudioService? _audio;
    private IPhysicsWorld? _physics;
    private IGameScheduler? _scheduler;
    private ITweenService? _tweens;
    private IPrefabService? _prefabs;
    private RenderContext? _render;
    private ComponentRegistry? _components;
    private Game? _game;
    private bool _gameResolved;

    public ScriptRuntime(World world, IServiceProvider services, IEventBus events, SceneManager scenes, ScriptTypeRegistry registry, EngineProfilers profilers,
        GameSettings settings, ILoggerFactory loggers)
    {
        World = world;
        Services = services;
        Events = events;
        _sceneManager = scenes;
        _registry = registry;
        _profiler = profilers.Game;
        _loggerFactory = loggers;
        _logger = loggers.CreateLogger("Talesmith.Scripting");
        FixedDeltaTime = 1f / Math.Max(1, settings.FixedUpdateRate);
        Timing = new ScriptTime(this);
        _fixedUpdate = new ScriptPhase(ScriptCallback.FixedUpdate);
        _update = new ScriptPhase(ScriptCallback.Update);
        _lateUpdate = new ScriptPhase(ScriptCallback.LateUpdate);
        _subscriptions =
        [
            events.Subscribe<ComponentAdded>(OnComponentAdded, filter: IsWatched),
            events.Subscribe<ComponentRemoved>(OnComponentRemoved, filter: IsWatched),
            events.Subscribe<EntityDestroyed>(OnEntityDestroyed, filter: IsOwnWorld)
        ];
    }

    public World World { get; }

    public IServiceProvider Services { get; }

    public IEventBus Events { get; }

    /// <summary>The running scene, set when it starts.</summary>
    public Scene? Scene { get; private set; }

    /// <summary>The timing of the phase being dispatched.</summary>
    public GameTime Time { get; private set; }

    public float FixedDeltaTime { get; }

    /// <summary>What <see cref="Script.Time"/> returns, shared by the scene's scripts.</summary>
    internal ScriptTime Timing { get; }

    public IInputService Input => _input ??= Services.GetRequiredService<IInputService>();

    public IAssetManager Assets => _assets ??= Services.GetRequiredService<IAssetManager>();

    public IAudioService Audio => _audio ??= Services.GetRequiredService<IAudioService>();

    public IPhysicsWorld Physics => _physics ??= Services.GetService<IPhysicsWorld>() ??
        throw new InvalidOperationException("The physics module is not registered; add it with services.AddTalesmithPhysics().");

    public IGameScheduler Scheduler => _scheduler ??= Services.GetRequiredService<IGameScheduler>();

    public ITweenService Tweens => _tweens ??= Services.GetRequiredService<ITweenService>();

    public IPrefabService Prefabs => _prefabs ??= Services.GetRequiredService<IPrefabService>();

    public ISceneManager Scenes => _sceneManager;

    public RenderContext Render => _render ??= Services.GetRequiredService<RenderContext>();

    public ComponentRegistry Components => _components ??= Services.GetRequiredService<ComponentRegistry>();

    public Game? Game
    {
        get
        {
            if (!_gameResolved)
            {
                _game = Services.GetService<Game>();
                _gameResolved = true;
            }

            return _game;
        }
    }

    private bool IsPlaying => (_sceneManager.Mode & ExecutionModes.Play) != 0 && !_stopped;

    public static string Describe(World world, Entity entity)
    {
        if (entity.IsNull)
            return "an unattached script";
        return world.IsAlive(entity) && world.TryGet<Name>(entity, out var name) && !string.IsNullOrEmpty(name.Value) ? name.Value : entity.ToString();
    }

    /// <summary>Creates the scene's scripts and tells them the scene loaded.</summary>
    public void Start(Scene scene)
    {
        Scene = scene;
        if (!IsPlaying)
            return;
        if (Services.GetService<IPhysicsWorld>() is { } physics)
            _physicsListener = physics.AddCollisionListener(this);

        CreatePending();
        var count = _live.Count;
        for (var i = 0; i < count; i++)
        {
            var script = _live[i];
            if (!script.IsDestroyed && script.Info!.Overrides(ScriptCallback.SceneLoaded))
                Call(script, ScriptCallback.SceneLoaded);
        }
    }

    /// <summary>Tells the scene's scripts the scene unloads, then destroys them.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        for (var i = 0; i < _live.Count; i++)
        {
            var script = _live[i];
            if (!script.IsDestroyed && script.Info!.Overrides(ScriptCallback.SceneUnloaded))
                Call(script, ScriptCallback.SceneUnloaded);
        }

        for (var i = 0; i < _live.Count; i++)
            DestroyScript(_live[i]);
        _stopped = true;
        _physicsListener?.Dispose();
        _physicsListener = null;
        _pending.Clear();
        _starting.Clear();
    }

    /// <summary>Creates and starts waiting scripts, then runs one update phase.</summary>
    public void Run(ScriptCallback callback, in GameTime time)
    {
        if (Scene is null || !IsPlaying)
            return;
        var started = Stopwatch.GetTimestamp();
        Time = time;
        DestroyDeferred();
        CreatePending();
        if (callback != ScriptCallback.LateUpdate)
            StartPending();

        var phase = callback switch
        {
            ScriptCallback.FixedUpdate => _fixedUpdate,
            ScriptCallback.Update => _update,
            _ => _lateUpdate
        };
        phase.Run(this, _profiler);
        if (_liveDirty)
        {
            _live.RemoveAll(IsDestroyed);
            _liveDirty = false;
        }

        _profiler.Increment(ScriptTimeCounter, (Stopwatch.GetTimestamp() - started) * Profiler.MillisecondsPerTick);
        if (callback == ScriptCallback.Update)
            _profiler.Set(ScriptCounter, _live.Count);
    }

    /// <summary>Queues a script added to a bound component for creation.</summary>
    public void Attach(Script script)
    {
        if (!IsPlaying || script.IsDestroyed)
            return;
        script.Info = _registry.GetOrAdd(script.GetType());
        script.Runtime = this;
        script.State = ScriptState.Pending;
        _pending.Add(script);
    }

    /// <summary>Destroys a script removed from its component.</summary>
    public void Detach(Script script)
    {
        if (ReferenceEquals(script.Runtime, this))
            DestroyScript(script);
    }

    /// <summary>Calls <c>OnEnable</c> or <c>OnDisable</c> when a script's enabled state or its entity's activity changed.</summary>
    /// <param name="entityActive">Overrides reading <see cref="Inactive"/>, for changes reported before they happen.</param>
    public void UpdateActivation(Script script, bool? entityActive = null)
    {
        var state = script.State;
        if (!ReferenceEquals(script.Runtime, this) || (state & ScriptState.Created) == 0 || (state & ScriptState.Destroyed) != 0)
            return;

        var active = script.Enabled && (state & ScriptState.Faulted) == 0 && (entityActive ?? IsEntityActive(script.Entity));
        if (active == ((state & ScriptState.Active) != 0))
            return;
        if (active)
        {
            script.State |= ScriptState.Active;
            Call(script, ScriptCallback.Enable);
        }
        else
        {
            script.State &= ~ScriptState.Active;
            Call(script, ScriptCallback.Disable);
        }
    }

    /// <summary>Destroys an entity and its children, after the current query when one is iterating.</summary>
    public void Destroy(Entity entity)
    {
        if (!World.IsAlive(entity))
            return;
        if (World.IsIterating)
            _deferredDestroys.Add(entity);
        else
            World.DestroyWithChildren(entity);
    }

    /// <summary>Copies an entity's saved components onto a new entity.</summary>
    public Entity Clone(Entity original)
    {
        if (!World.IsAlive(original))
            throw new InvalidOperationException($"{original} is not alive, so it cannot be copied.");

        var context = new IdentityContext(World, Services);
        var captured = new List<(IComponentDefinition Definition, System.Text.Json.Nodes.JsonObject Data)>();
        foreach (var id in World.GetComponentIds(original).ToArray())
        {
            var type = ComponentType.FromId(id).Type;
            if (type == typeof(SceneEntityId) || !Components.TryGet(type, out var definition))
                continue;
            if (definition.Capture(World, original, context) is { } data)
                captured.Add((definition, data));
        }

        var copy = World.Create();
        if (World.TryGet<Name>(original, out var name))
            World.Set(copy, name);
        foreach (var (definition, data) in captured)
            definition.Apply(World, copy, data, context);
        return copy;
    }

    public ILogger LoggerFor(Script script)
    {
        var info = script.Info ?? _registry.GetOrAdd(script.GetType());
        if (!_scriptLoggers.TryGetValue(info, out var logger))
            _scriptLoggers[info] = logger = _loggerFactory.CreateLogger(info.TypeName);
        return logger;
    }

    /// <summary>Runs a callback a script registered, such as an event handler or tween, isolating its failure.</summary>
    public void Guard(Script script, Action action)
    {
        try
        {
            action();
            script.Failures = 0;
        }
        catch (Exception ex)
        {
            Fail(script, "a scheduled callback", ex);
        }
    }

    public void Guard<T>(Script script, Action<T> action, T value)
    {
        try
        {
            action(value);
            script.Failures = 0;
        }
        catch (Exception ex)
        {
            Fail(script, "a tween", ex);
        }
    }

    public void Guard<T>(Script script, EventCallback<T> handler, ref T e)
    {
        try
        {
            handler(ref e);
            script.Failures = 0;
        }
        catch (Exception ex)
        {
            Fail(script, $"its {typeof(T).Name} handler", ex);
        }
    }

    public void ReportRoutineFailure(Script script, Exception error) =>
        _logger.RoutineFailed(error, script.Info?.TypeName ?? script.GetType().Name, Describe(World, script.Entity));

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _physicsListener?.Dispose();
        _physicsListener = null;
        foreach (var script in _live)
        {
            script.State |= ScriptState.Destroyed;
            script.Lifetime?.End();
        }

        _stopped = true;
    }

    internal void Fail(Script script, ScriptCallback callback, Exception error) => Fail(script, CallbackNames[(int)callback], error);

    private void Fail(Script script, string callback, Exception error)
    {
        script.Failures++;
        var type = script.Info?.TypeName ?? script.GetType().Name;
        var entity = Describe(World, script.Entity);
        _logger.ScriptFailed(error, type, callback, entity);
        if (script.Failures < SystemScheduler.MaxConsecutiveFailures || (script.State & ScriptState.Faulted) != 0)
            return;
        script.State |= ScriptState.Faulted;
        _logger.ScriptDisabled(type, entity, script.Failures);
        UpdateActivation(script);
    }

    private void Call(Script script, ScriptCallback callback)
    {
        using (_profiler.Measure(script.Info!.Marker))
        {
            try
            {
                script.Invoke(callback);
                script.Failures = 0;
            }
            catch (Exception ex)
            {
                Fail(script, callback, ex);
            }
        }
    }

    private void CreatePending()
    {
        for (var i = 0; i < _pending.Count; i++)
        {
            var script = _pending[i];
            if (script.IsDestroyed || !ReferenceEquals(script.Runtime, this) || script.Owner is null)
                continue;
            Create(script);
        }

        _pending.Clear();
    }

    private void Create(Script script)
    {
        var info = script.Info!;
        script.State = ScriptState.Created;
        _live.Add(script);
        _starting.Add(script);
        if (info.Overrides(ScriptCallback.FixedUpdate))
            _fixedUpdate.Add(script);
        if (info.Overrides(ScriptCallback.Update))
            _update.Add(script);
        if (info.Overrides(ScriptCallback.LateUpdate))
            _lateUpdate.Add(script);

        Call(script, ScriptCallback.Create);
        UpdateActivation(script);
    }

    private void StartPending()
    {
        if (_starting.Count == 0)
            return;
        var count = _starting.Count;
        for (var i = 0; i < count; i++)
        {
            var script = _starting[i];
            if ((script.State & (ScriptState.Active | ScriptState.Started | ScriptState.Destroyed)) != ScriptState.Active)
                continue;
            script.State |= ScriptState.Started;
            Call(script, ScriptCallback.Start);
        }

        _starting.RemoveAll(IsDone);
    }

    private void DestroyScript(Script script)
    {
        var state = script.State;
        if ((state & ScriptState.Destroyed) != 0 || !ReferenceEquals(script.Runtime, this))
            return;
        if ((state & ScriptState.Active) != 0)
        {
            script.State &= ~ScriptState.Active;
            Call(script, ScriptCallback.Disable);
        }

        if ((state & ScriptState.Created) != 0)
            Call(script, ScriptCallback.Destroy);
        script.State |= ScriptState.Destroyed;
        script.Lifetime?.End();
        _fixedUpdate.MarkDirty();
        _update.MarkDirty();
        _lateUpdate.MarkDirty();
        _liveDirty = true;
    }

    private void DestroyDeferred()
    {
        if (_deferredDestroys.Count == 0)
            return;
        foreach (var entity in _deferredDestroys)
        {
            if (World.IsAlive(entity))
                World.DestroyWithChildren(entity);
        }

        _deferredDestroys.Clear();
    }

    private bool IsEntityActive(Entity entity) => World.IsAlive(entity) && !World.Has<Inactive>(entity);

    private bool IsWatched(in ComponentAdded e) => ReferenceEquals(e.World, World) && (e.Type == ScriptComponentType || e.Type == InactiveType);

    private bool IsWatched(in ComponentRemoved e) => ReferenceEquals(e.World, World) && (e.Type == ScriptComponentType || e.Type == InactiveType);

    private bool IsOwnWorld(in EntityDestroyed e) => ReferenceEquals(e.World, World);

    private void OnComponentAdded(ref ComponentAdded e)
    {
        if (!IsPlaying)
            return;
        if (e.Type == ScriptComponentType)
            Bind(e.Entity);
        else
            UpdateEntity(e.Entity, entityActive: false);
    }

    private void OnComponentRemoved(ref ComponentRemoved e)
    {
        if (!IsPlaying)
            return;
        if (e.Type == ScriptComponentType)
            Unbind(e.Entity);
        else
            UpdateEntity(e.Entity, entityActive: true);
    }

    private void OnEntityDestroyed(ref EntityDestroyed e)
    {
        if (IsPlaying && World.Has<ScriptComponent>(e.Entity))
            Unbind(e.Entity);
    }

    private void Bind(Entity entity)
    {
        var component = World.Get<ScriptComponent>(entity);
        if (component is null)
            return;
        if (component.Runtime is not null)
        {
            if (!ReferenceEquals(component.Runtime, this) || component.Entity != entity)
                _logger.ScriptComponentShared(Describe(World, entity));
            return;
        }

        component.Runtime = this;
        component.Entity = entity;
        foreach (var script in component.Items)
            Attach(script);
    }

    private void Unbind(Entity entity)
    {
        var component = World.Get<ScriptComponent>(entity);
        if (component is null || !ReferenceEquals(component.Runtime, this) || component.Entity != entity)
            return;
        var scripts = component.Items;
        for (var i = 0; i < scripts.Count; i++)
            DestroyScript(scripts[i]);
        component.Runtime = null;
    }

    private void UpdateEntity(Entity entity, bool entityActive)
    {
        if (!World.TryGet<ScriptComponent>(entity, out var component) || component is null || !ReferenceEquals(component.Runtime, this))
            return;
        var scripts = component.Items;
        for (var i = 0; i < scripts.Count; i++)
            UpdateActivation(scripts[i], entityActive);
    }

    void ICollisionListener.OnCollisionEntered(in ContactInfo contact) => Dispatch(ScriptCallback.CollisionEnter, contact);

    void ICollisionListener.OnCollisionStayed(in ContactInfo contact) => Dispatch(ScriptCallback.CollisionStay, contact);

    void ICollisionListener.OnCollisionExited(in ContactInfo contact) => Dispatch(ScriptCallback.CollisionExit, contact);

    void ICollisionListener.OnTriggerEntered(in ContactInfo contact) => Dispatch(ScriptCallback.TriggerEnter, contact);

    void ICollisionListener.OnTriggerStayed(in ContactInfo contact) => Dispatch(ScriptCallback.TriggerStay, contact);

    void ICollisionListener.OnTriggerExited(in ContactInfo contact) => Dispatch(ScriptCallback.TriggerExit, contact);

    private void Dispatch(ScriptCallback callback, in ContactInfo contact)
    {
        if (!World.TryGet<ScriptComponent>(contact.Self, out var component) || component is null || !ReferenceEquals(component.Runtime, this))
            return;

        var started = Stopwatch.GetTimestamp();
        var scripts = component.Items;
        for (var i = 0; i < scripts.Count; i++)
        {
            var script = scripts[i];
            if ((script.State & ScriptState.Active) == 0 || !script.Info!.Overrides(callback))
                continue;
            using (_profiler.Measure(script.Info.Marker))
            {
                try
                {
                    script.Invoke(callback, contact);
                    script.Failures = 0;
                }
                catch (Exception ex)
                {
                    Fail(script, callback, ex);
                }
            }
        }

        _profiler.Increment(ScriptTimeCounter, (Stopwatch.GetTimestamp() - started) * Profiler.MillisecondsPerTick);
    }

    private static readonly string[] CallbackNames =
    [
        "OnCreate", "OnStart", "OnEnable", "OnDisable", "FixedUpdate", "Update", "LateUpdate", "OnDestroy", "OnCollisionEnter",
        "OnCollisionStay", "OnCollisionExit", "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit", "OnSceneLoaded", "OnSceneUnloaded"
    ];
}
