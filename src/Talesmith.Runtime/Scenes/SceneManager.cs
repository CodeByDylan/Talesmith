using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Mathematics;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Scheduling;
using Talesmith.Runtime.Tweens;
using Talesmith.Systems;

namespace Talesmith.Runtime.Scenes;

/// <summary>The default <see cref="ISceneManager"/>: loads scenes in their own service scope and fades between them.</summary>
public sealed class SceneManager : ISceneManager, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, SceneRegistration> _registrations;
    private readonly IReadOnlyList<SystemDescriptor> _systems;
    private readonly IEventBus _events;
    private readonly EngineProfilers _profilers;
    private readonly GameScheduler _scheduler;
    private readonly TweenService _tweens;
    private readonly ILogger<SceneManager> _logger;
    private ExecutionModes _mode = ExecutionModes.Play;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private ActiveScene? _active;
    private volatile bool _isLoading;
    private SceneLoadProgress? _loadProgress;

    public SceneManager(IServiceProvider services, IEnumerable<SceneRegistration> registrations, IEnumerable<SystemDescriptor> systems,
        IEventBus events, EngineProfilers profilers, GameScheduler scheduler, TweenService tweens, ILogger<SceneManager> logger)
    {
        _services = services;
        _registrations = new Dictionary<string, SceneRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
            _registrations[registration.Name] = registration;
        _systems = systems.ToList();
        _events = events;
        _profilers = profilers;
        _scheduler = scheduler;
        _tweens = tweens;
        _logger = logger;
    }

    public Scene? Current => _active?.Scene;

    public bool IsLoading => _isLoading;

    public SceneLoadProgress? LoadProgress => Volatile.Read(ref _loadProgress);

    public IReadOnlyCollection<string> RegisteredScenes => _registrations.Keys;

    /// <summary>The execution mode applied to every scene's systems; the editor uses <see cref="ExecutionModes.Edit"/> while authoring.</summary>
    public ExecutionModes Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (_active is not null)
                _active.Systems.Mode = value;
        }
    }

    /// <summary>The active scene's systems, run by the game loop.</summary>
    public SystemScheduler? Systems => _active?.Systems;

    /// <summary>How much the transition color covers the screen, from 0 to 1.</summary>
    public float FadeAmount { get; private set; }

    public Color FadeColor { get; private set; } = Color.Black;

    public async Task LoadAsync(SceneRequest request, SceneTransition? transition = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_registrations.TryGetValue(request.Name, out var registration))
            throw new InvalidOperationException($"No scene named '{request.Name}' is registered. Registered scenes: {string.Join(", ", _registrations.Keys)}.");

        transition ??= SceneTransition.Default;
        await _loadLock.WaitAsync(cancellationToken);
        _isLoading = true;
        try
        {
            _events.Publish(new SceneLoading(request));
            FadeColor = transition.Color ?? Color.Black;
            if (_active is not null && transition.FadeOutSeconds > 0)
                await _tweens.Run(transition.FadeOutSeconds, t => FadeAmount = Math.Max(FadeAmount, t), scaled: false, cancellationToken: cancellationToken);
            else if (_active is null)
                FadeAmount = transition.FadeInSeconds > 0 ? 1 : 0;

            ActiveScene loaded;
            var progress = new SceneLoadProgress();
            Volatile.Write(ref _loadProgress, progress);
            try
            {
                loaded = await CreateAsync(registration, request, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.SceneLoadFailed(ex, request.ToString());
                _events.Publish(new SceneLoadFailed(request, ex));
                FadeAmount = 0;
                throw;
            }
            finally
            {
                Volatile.Write(ref _loadProgress, null);
            }

            await UnloadAsync();
            _active = loaded;
            loaded.Systems.Start();
            loaded.Scene.OnStarted();
            foreach (var listener in loaded.Listeners)
                listener.OnSceneStarted(loaded.Scene);
            _events.Publish(new SceneLoaded(request, loaded.Scene));
            if (_logger.IsEnabled(LogLevel.Information))
                _logger.SceneActive(request.ToString());

            if (transition.FadeInSeconds > 0)
                await _tweens.Run(transition.FadeInSeconds, t => FadeAmount = 1 - t, Easing.QuadOut, scaled: false, cancellationToken: cancellationToken);
            FadeAmount = 0;
        }
        finally
        {
            _isLoading = false;
            _loadLock.Release();
        }
    }

    public Task ReloadAsync(SceneTransition? transition = null) =>
        _active is { } active ? LoadAsync(active.Scene.Request, transition) : Task.CompletedTask;

    public void Dispose()
    {
        UnloadAsync().AsTask().GetAwaiter().GetResult();
        _loadLock.Dispose();
    }

    private async Task<ActiveScene> CreateAsync(SceneRegistration registration, SceneRequest request, SceneLoadProgress progress,
        CancellationToken cancellationToken)
    {
        var scope = _services.CreateAsyncScope();
        try
        {
            var world = scope.ServiceProvider.GetRequiredService<World>();
            var scene = (Scene)scope.ServiceProvider.GetRequiredService(registration.Type);
            scene.World = world;
            scene.Services = scope.ServiceProvider;
            scene.Request = request;
            scene.LoadProgress = progress;

            var systems = _systems.Select(d => new ScheduledSystem(d, (ISystem)scope.ServiceProvider.GetRequiredService(d.Type))).ToList();
            var scheduler = new SystemScheduler(world, systems, _profilers.Game, OnSystemError) { Mode = _mode };
            await scene.LoadAsync(cancellationToken);
            return new ActiveScene(scope, scene, world, scheduler, scope.ServiceProvider.GetServices<ISceneListener>().ToList());
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private async ValueTask UnloadAsync()
    {
        if (_active is not { } active)
            return;

        _active = null;
        foreach (var listener in active.Listeners)
            listener.OnSceneStopping(active.Scene);
        active.Scene.OnUnloading();
        active.Systems.Stop();
        _scheduler.CancelAll();
        _tweens.CancelAll();
        await active.Scope.DisposeAsync();
        _events.Publish(new SceneUnloaded(active.Scene.Request));
    }

    private void OnSystemError(ScheduledSystem system, Exception error)
    {
        if (system.Enabled)
            _logger.SystemFailed(error, system.Descriptor.Type.Name);
        else
            _logger.SystemDisabled(error, system.Descriptor.Type.Name, system.ConsecutiveFailures);
    }

    private sealed record ActiveScene(AsyncServiceScope Scope, Scene Scene, World World, SystemScheduler Systems, IReadOnlyList<ISceneListener> Listeners);
}
