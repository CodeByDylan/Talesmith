using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Talesmith.Audio;
using Talesmith.Diagnostics;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Input;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Scheduling;
using Talesmith.Runtime.Tweens;
using Talesmith.Systems;
using Talesmith.Time;

namespace Talesmith.Runtime.Hosting;

/// <summary>A running game: advances simulation one frame per <see cref="Tick"/> and publishes frames for a renderer.</summary>
/// <remarks>
/// <para>Frames run on the game thread: a <see cref="SimulationThread"/> (<see cref="GameThreading.Dedicated"/>), or a thread of the host
/// that calls <see cref="Tick"/> itself (<see cref="GameThreading.Host"/>), such as a UI thread or a headless loop. The render thread draws
/// the newest frame from <see cref="Frames"/>. Awaits in game code resume on the game thread at the start of the next frame.</para>
/// <para>Game state belongs to the game thread. Other threads reach it with <see cref="Post"/> and <see cref="InvokeAsync{T}(Func{T})"/>.</para>
/// </remarks>
public sealed class Game : IAsyncDisposable
{
    /// <summary>Longer frames are treated as this long, so a stall or a debugger break does not fast-forward the simulation.</summary>
    public const double MaxFrameSeconds = 0.25;

    private static readonly QueryDescription ActiveCameras = QueryDescription.With<Camera>().Without<Inactive>();
    private static readonly SendOrPostCallback RunAction = static state => ((Action)state!)();

    private readonly IServiceProvider _services;
    private readonly EngineProfilers _profilers;
    private readonly GameSynchronizationContext _context;
    private readonly IInputService _input;
    private readonly IEventBus _events;
    private readonly IAudioService _audio;
    private readonly GameScheduler _scheduler;
    private readonly TweenService _tweens;
    private readonly SceneManager _scenes;
    private readonly RenderContext _render;
    private readonly IRenderer _renderer;
    private readonly Assets.IAssetManager _assets;
    private readonly ILogger<Game> _logger;
    private readonly FixedTimestep _fixedStep;
    private readonly Action<Exception> _onContinuationError;
    private readonly CancellationTokenSource _disposing = new();
    private readonly TaskCompletionSource _startScene = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SimulationThread? _simulation;
    private int _tickingThread;
    private int _started;
    private volatile bool _isActive = true;
    private float _timeScale = 1;
    private Camera2D? _cameraOverride;
    private double _scaledTotal;
    private double _unscaledTotal;
    private double _fixedTotal;
    private bool _paused;

    public Game(IServiceProvider services, GameSettings settings, EngineProfilers profilers, GameSynchronizationContext context, IInputService input,
        IEventBus events, IAudioService audio, GameScheduler scheduler, TweenService tweens, SceneManager scenes, RenderContext render,
        IRenderer renderer, FrameExchange frames, Viewport viewport, Assets.IAssetManager assets, EngineMetrics metrics, ILogger<Game> logger)
    {
        _assets = assets;
        Metrics = metrics;
        _services = services;
        Settings = settings;
        _profilers = profilers;
        _context = context;
        _input = input;
        _events = events;
        _audio = audio;
        _scheduler = scheduler;
        _tweens = tweens;
        _scenes = scenes;
        _render = render;
        _renderer = renderer;
        Frames = frames;
        Viewport = viewport;
        _logger = logger;
        _fixedStep = new FixedTimestep(1.0 / Math.Max(1, settings.FixedUpdateRate), Math.Max(1, settings.MaxFixedStepsPerFrame));
        _onContinuationError = OnContinuationError;
    }

    public IServiceProvider Services => _services;

    public GameSettings Settings { get; }

    /// <summary>Frames ready for the render thread.</summary>
    public FrameExchange Frames { get; }

    public Viewport Viewport { get; }

    public ISceneManager Scenes => _scenes;

    public EngineProfilers Profilers => _profilers;

    /// <summary>Publishes the profilers to <c>dotnet-counters</c> and OpenTelemetry under the meter "Talesmith".</summary>
    public EngineMetrics Metrics { get; }

    public IRenderer Renderer => _renderer;

    /// <summary>Which thread runs the game's frames.</summary>
    public GameThreading Threading => Simulation is null ? GameThreading.Host : GameThreading.Dedicated;

    /// <summary>The thread that runs the game's frames, or null while the host ticks the game itself.</summary>
    public SimulationThread? Simulation => Volatile.Read(ref _simulation);

    /// <summary>Whether the calling thread runs the game's frames: the simulation thread, or for a host-ticked game the thread of the last frame.</summary>
    public bool IsGameThread => _context.CheckAccess();

    /// <summary>Multiplies game time; 0.5 is slow motion. Unscaled time is unaffected.</summary>
    public float TimeScale
    {
        get => _timeScale;
        set
        {
            DebugVerifyAccess();
            _timeScale = value;
        }
    }

    /// <summary>Stops scaled time; input, rendering and unscaled waits keep running. Changes raise <see cref="PauseStateChanged"/>.</summary>
    public bool IsPaused
    {
        get => _paused;
        set
        {
            DebugVerifyAccess();
            if (_paused == value)
                return;
            _paused = value;
            _events.Publish(new PauseStateChanged(value));
        }
    }

    /// <summary>Whether the game's window is in the foreground; hosts keep it up to date.</summary>
    /// <remarks>
    /// While false, scaled time stops as if paused when <see cref="GameSettings.PauseWhenInactive"/> is set. Safe to set from any thread; each
    /// frame reads it once at its start.
    /// </remarks>
    public bool IsActive
    {
        get => _isActive;
        set => _isActive = value;
    }

    public long FrameCount { get; private set; }

    /// <summary>Which systems run: the editor authors scenes in <see cref="ExecutionModes.Edit"/>; games run in <see cref="ExecutionModes.Play"/>.</summary>
    public ExecutionModes Mode
    {
        get => _scenes.Mode;
        set
        {
            DebugVerifyAccess();
            _scenes.Mode = value;
        }
    }

    /// <summary>When set, frames are drawn through this camera instead of the scene's cameras, such as the editor's free camera.</summary>
    /// <remarks>Such frames ignore <see cref="GameSettings.View"/>: they fill the whole target, with zoom in logical pixels per world unit.</remarks>
    public Camera2D? CameraOverride
    {
        get => _cameraOverride;
        set
        {
            DebugVerifyAccess();
            _cameraOverride = value;
        }
    }

    /// <summary>Raised on the game thread after each frame was published, so hosts can request a repaint.</summary>
    public event Action? FramePublished;

    /// <summary>Completes once <see cref="Start"/> has loaded the start scene and it has faded in; faults with the reason when it could not
    /// load, which is logged too.</summary>
    /// <remarks>Hosts wait for it to take down a loading screen. It is canceled when the game is disposed first.</remarks>
    public Task WhenStarted => _startScene.Task;

    /// <summary>Applies the input profile and starts loading the start scene on the first frame.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;
        ApplyInputProfile();
        _context.Post(_ => _ = LoadStartSceneAsync(), null);
    }

    /// <summary>Queues an action to run on the game thread at the start of the next frame; safe to call from any thread.</summary>
    /// <remarks>Actions run in the order they were posted, before input is applied. Exceptions they throw are logged.</remarks>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _context.Post(RunAction, action);
    }

    /// <summary>Runs an action on the game thread; safe to call from any thread.</summary>
    /// <remarks>
    /// On the game thread the action runs immediately. From other threads it runs at the start of the next frame, in order with
    /// <see cref="Post"/>, and the task completes on the caller's synchronization context or the thread pool. Exceptions fault the task; it
    /// is canceled when the game is disposed first.
    /// </remarks>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InvokeAsync<object?>(() =>
        {
            action();
            return null;
        });
    }

    /// <summary>Runs a function on the game thread and returns its result; safe to call from any thread.</summary>
    /// <remarks>
    /// On the game thread the function runs immediately. From other threads it runs at the start of the next frame, in order with
    /// <see cref="Post"/>, and the task completes on the caller's synchronization context or the thread pool. Exceptions fault the task; it
    /// is canceled when the game is disposed first.
    /// </remarks>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (IsGameThread)
        {
            try
            {
                return Task.FromResult(func());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        if (_disposing.IsCancellationRequested)
            return Task.FromCanceled<T>(new CancellationToken(canceled: true));
        var invocation = new Invocation<T>(func, _disposing.Token);
        _context.Post(Invocation<T>.Run, invocation);
        return invocation.Task;
    }

    /// <summary>Throws when a <see cref="SimulationThread"/> runs the game and the caller is on another thread.</summary>
    /// <remarks>Hosts that tick the game themselves are responsible for using it from one thread at a time.</remarks>
    /// <exception cref="InvalidOperationException">The caller is not on the simulation thread.</exception>
    public void VerifyAccess()
    {
        if (Simulation is not null && !IsGameThread)
            throw new InvalidOperationException("The game runs on its simulation thread; reach it with Game.Post or Game.InvokeAsync.");
    }

    /// <summary>Runs one frame: input, events, simulation, then building and publishing the frame to draw.</summary>
    /// <param name="realDeltaSeconds">Real time since the previous call.</param>
    /// <exception cref="InvalidOperationException">A frame is already running, or a simulation thread runs the game.</exception>
    public void Tick(double realDeltaSeconds)
    {
        if (Interlocked.CompareExchange(ref _tickingThread, Environment.CurrentManagedThreadId, 0) != 0)
            throw new InvalidOperationException("A frame of this game is already running; frames run one at a time on the game thread.");
        try
        {
            VerifyAccess();
            RunTick(realDeltaSeconds);
        }
        finally
        {
            Volatile.Write(ref _tickingThread, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Simulation?.Dispose();
        _disposing.Cancel();
        _startScene.TrySetCanceled(_disposing.Token);
        _scenes.Dispose();
        if (_services is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    internal void AttachSimulation(SimulationThread simulation)
    {
        if (Interlocked.CompareExchange(ref _simulation, simulation, null) is not null)
            throw new InvalidOperationException("Another simulation thread already runs this game.");
        _context.Unbind();
    }

    /// <summary>Makes the calling simulation thread the game thread before its first frame.</summary>
    internal void BindGameThread() => _context.Bind();

    internal void DetachSimulation(SimulationThread simulation)
    {
        if (Interlocked.CompareExchange(ref _simulation, null, simulation) == simulation)
            _context.Unbind();
    }

    [Conditional("DEBUG")]
    private void DebugVerifyAccess() => VerifyAccess();

    private void RunTick(double realDeltaSeconds)
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_context);
        var profiler = _profilers.Game;
        profiler.BeginFrame();
        try
        {
            using (profiler.Measure(RuntimeMarkers.Frame))
                RunFrame(Math.Clamp(realDeltaSeconds, 0, MaxFrameSeconds), profiler);
        }
        catch (Exception ex)
        {
            _logger.FrameFailed(ex, FrameCount);
        }
        finally
        {
            profiler.EndFrame();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        FramePublished?.Invoke();
    }

    private void RunFrame(double realDelta, Profiler profiler)
    {
        var unscaled = (float)realDelta;
        var paused = IsPaused || (!IsActive && Settings.PauseWhenInactive);
        var scaled = paused ? 0 : unscaled * TimeScale;
        _unscaledTotal += unscaled;
        _scaledTotal += scaled;
        FrameCount++;

        using (profiler.Measure(RuntimeMarkers.Continuations))
            _context.Pump(_onContinuationError);
        using (profiler.Measure(RuntimeMarkers.Input))
            _input.Update();
        using (profiler.Measure(RuntimeMarkers.Events))
            _events.DispatchQueued();
        using (profiler.Measure(RuntimeMarkers.Audio))
            _audio.Update(unscaled);
        using (profiler.Measure(RuntimeMarkers.Scheduling))
        {
            _scheduler.Update(scaled, unscaled);
            _tweens.Update(scaled, unscaled);
        }

        var time = new GameTime(scaled, _scaledTotal, unscaled, _unscaledTotal, FrameCount, 0);
        var systems = _scenes.Systems;
        if (systems is not null)
        {
            using (profiler.Measure(RuntimeMarkers.PreUpdate))
                systems.Run(SystemPhase.PreUpdate, time);

            var steps = _fixedStep.Advance(scaled);
            profiler.Increment(RuntimeMarkers.FixedSteps, steps);
            using (profiler.Measure(RuntimeMarkers.FixedUpdate))
            {
                var step = (float)_fixedStep.StepSeconds;
                for (var i = 0; i < steps; i++)
                {
                    _fixedTotal += step;
                    systems.Run(SystemPhase.FixedUpdate, time with { DeltaTime = step, TotalTime = _fixedTotal });
                }
            }

            time = time with { Interpolation = _fixedStep.Interpolation };
            using (profiler.Measure(RuntimeMarkers.Update))
                systems.Run(SystemPhase.Update, time);
            using (profiler.Measure(RuntimeMarkers.LateUpdate))
                systems.Run(SystemPhase.LateUpdate, time);
        }

        BuildFrame(systems, time, profiler);
        RecordCounters(profiler);
    }

    private void BuildFrame(SystemScheduler? systems, in GameTime time, Profiler profiler)
    {
        var world = _scenes.Current?.World;
        var layout = _cameraOverride is null ? Viewport.Layout : Viewport.UnscaledLayout;
        var frame = Frames.BeginWrite();
        frame.Begin(ActiveCamera(world), layout, _scenes.Current?.ClearColor ?? Settings.ClearColor, Viewport.View.BorderColor, time.TotalTime,
            _renderer.WhiteTexture);
        _render.Begin(frame);
        try
        {
            using (profiler.Measure(RuntimeMarkers.PreRender))
                systems?.Run(SystemPhase.PreRender, time);
            if (_scenes.FadeAmount > 0)
            {
                var fade = _scenes.FadeColor;
                frame.FillRect(new Rect2(0, 0, layout.ViewSize.X, layout.ViewSize.Y),
                    fade.WithAlpha((byte)(fade.A * Math.Clamp(_scenes.FadeAmount, 0, 1))), int.MaxValue, RenderSpace.Screen);
            }
        }
        finally
        {
            _render.End();
        }

        using (profiler.Measure(RuntimeMarkers.Publish))
            Frames.Publish(frame);
        profiler.Increment(RuntimeMarkers.BatchesBuilt, frame.Batches.Length);
        profiler.Increment(RuntimeMarkers.MeshInstancesSubmitted, frame.MeshInstanceCount);
    }

    private Camera2D ActiveCamera(World? world)
    {
        var view = _cameraOverride ?? new Camera2D(default, 1);
        if (world is not null && _cameraOverride is null)
        {
            var priority = int.MinValue;
            foreach (var archetype in world.Query(ActiveCameras))
            {
                foreach (ref readonly var camera in archetype.GetSpan<Camera>())
                {
                    if (camera.Active && camera.Priority > priority)
                    {
                        priority = camera.Priority;
                        view = camera.View;
                    }
                }
            }
        }

        return view;
    }

    private void RecordCounters(Profiler profiler)
    {
        if (_scenes.Current?.World is { } world)
        {
            profiler.Set(RuntimeMarkers.Entities, world.EntityCount);
            profiler.Set(RuntimeMarkers.Archetypes, world.Archetypes.Count);
        }

        profiler.Set(RuntimeMarkers.AssetsLoaded, _assets.LoadedCount);
        profiler.Set(RuntimeMarkers.AudioVoices, _audio.ActiveSounds);
        profiler.Set(RuntimeMarkers.PendingContinuations, _context.Pending + _scheduler.PendingCount + _tweens.ActiveCount);
    }

    private void ApplyInputProfile()
    {
        var root = _assets.Source;
        if (!root.Exists(Settings.InputProfile))
            return;
        try
        {
            using var stream = root.OpenRead(Settings.InputProfile);
            using var reader = new StreamReader(stream);
            _input.Actions.Apply(InputProfile.FromJson(reader.ReadToEnd()));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            _logger.InputProfileUnreadable(ex, Settings.InputProfile);
        }
    }

    private async Task LoadStartSceneAsync()
    {
        try
        {
            await _scenes.LoadAsync(Settings.StartScene);
            _startScene.TrySetResult();
        }
        catch (Exception ex)
        {
            _logger.StartSceneFailed(ex, Settings.StartScene.ToString());
            _startScene.TrySetException(ex);
        }
    }

    private void OnContinuationError(Exception error) => _logger.GameTaskFailed(error);

    /// <summary>A function waiting for the game thread, completing a task with its result.</summary>
    private sealed class Invocation<T> : TaskCompletionSource<T>
    {
        public static readonly SendOrPostCallback Run = static state => ((Invocation<T>)state!).Execute();

        private readonly Func<T> _func;
        private readonly CancellationTokenRegistration _registration;

        public Invocation(Func<T> func, CancellationToken disposing)
            : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            _func = func;
            _registration = disposing.UnsafeRegister(static state => ((Invocation<T>)state!).TrySetCanceled(), this);
        }

        private void Execute()
        {
            _registration.Dispose();
            if (Task.IsCompleted)
                return;
            try
            {
                TrySetResult(_func());
            }
            catch (Exception ex)
            {
                TrySetException(ex);
            }
        }
    }
}
