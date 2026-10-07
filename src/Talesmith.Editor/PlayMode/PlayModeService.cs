using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Avalonia.Hosting;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Events;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.PlayMode;

/// <summary>The default <see cref="IPlayModeService"/>: runs each play session on a <see cref="SimulationThread"/>.</summary>
/// <remarks>Members belong to the UI thread except <see cref="Dispatch"/> and <see cref="InvokeAsync{T}"/>.</remarks>
public sealed class PlayModeService(IProjectService project, ISceneDocumentService documents, IConsole console, IToastService toasts,
    IEnumerable<IPlaySessionContributor> contributors, EditorPluginGuard plugins)
    : IPlayModeService, IEditorCommandContributor, ICloseGuard, IAsyncDisposable
{
    private const string PlayKey = "editor.play";
    private readonly IPlaySessionContributor[] _contributors = [.. contributors];
    private Func<Task>? _lastStart;

    /// <summary>How long stopping waits for the play game to finish its frame before giving up on it.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    public PlayState State { get; private set; }

    public bool IsPlaying => State is PlayState.Playing or PlayState.Paused;

    public bool IsPaused => State == PlayState.Paused;

    public GameSession? Session { get; private set; }

    public Game? Game => Session?.Game;

    public event EventHandler? StateChanged;

    public Task PlayAsync()
    {
        if (documents.Active is not { } model)
            return Task.CompletedTask;
        var source = new PlaySceneSource(project.Catalog);
        source.Add(PlayKey, model.Path, model.Document.Clone());
        var request = new SceneRequest(DocumentScene.SceneName, new Dictionary<string, string> { [InMemorySceneDocuments.DocumentParameter] = PlayKey });
        _lastStart = PlayAsync;
        return BeginAsync(request, source, $"Playing {model.FileName}");
    }

    public Task StartAsync()
    {
        var source = new PlaySceneSource(project.Catalog);
        if (documents.Active is { Path: { } path, IsDirty: true } model)
            source.Add(null, path, model.Document.Clone());
        _lastStart = StartAsync;
        return BeginAsync(project.Settings.StartScene, source, "Playing from the start scene");
    }

    public Task TogglePlayAsync() => State == PlayState.Stopped ? PlayAsync() : StopAsync();

    public async Task RestartAsync()
    {
        var start = _lastStart ?? PlayAsync;
        await StopAsync();
        await start();
    }

    public void Pause()
    {
        if (State != PlayState.Playing)
            return;
        Dispatch(static game => game.IsPaused = true);
        SetState(PlayState.Paused);
    }

    public void Resume()
    {
        if (State != PlayState.Paused)
            return;
        Dispatch(static game => game.IsPaused = false);
        SetState(PlayState.Playing);
    }

    public void TogglePause()
    {
        if (State == PlayState.Paused)
            Resume();
        else
            Pause();
    }

    public void Step()
    {
        if (!IsPlaying)
            return;
        if (State == PlayState.Playing)
            Pause();
        else
            Dispatch(StepFrame);
    }

    public async Task StopAsync()
    {
        if (Session is not { } session)
            return;
        Session = null;
        var game = session.Game;
        var exited = game.InvokeAsync(() => game.Services.GetRequiredService<IEventBus>().Publish(new PlayModeExited()));
        if (await Task.WhenAny(exited, Task.Delay(StopTimeout)) != exited)
            console.Warning("The play session did not reach its next frame in time to see PlayModeExited", ConsoleSource.Game);
        else if (exited.Exception?.InnerException is { } error)
            console.Error($"A handler of PlayModeExited failed: {error.Message}", error, ConsoleSource.Game);

        var simulation = game.Simulation;
        var stopped = simulation is null || await Task.Run(() => simulation.Stop(StopTimeout)).ConfigureAwait(true);
        SetState(PlayState.Stopped);
        console.Info("Stopped playing");
        if (!stopped)
        {
            console.Error("The play session's frame did not end, so the session is left running until it does; a script or system may be stuck in a loop",
                source: ConsoleSource.Game);
            _ = Task.Run(async () =>
            {
                if (simulation!.Stop(Timeout.InfiniteTimeSpan))
                    await DisposeSessionAsync(session).ConfigureAwait(false);
            });
            return;
        }

        await DisposeSessionAsync(session);
    }

    private async Task DisposeSessionAsync(GameSession session)
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            console.Error($"The play session did not shut down cleanly: {ex.Message}", ex, ConsoleSource.Game);
        }
    }

    public void Dispatch(Action<Game> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Game is not { } game)
            return;
        game.InvokeAsync(() => action(game)).ContinueWith(
            task => console.Error($"Play mode code on the game thread failed: {task.Exception!.InnerException!.Message}", task.Exception.InnerException,
                ConsoleSource.Game),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public Task<T> InvokeAsync<T>(Func<Game, T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return Game is { } game
            ? game.InvokeAsync(() => func(game))
            : Task.FromException<T>(new InvalidOperationException("Nothing is playing."));
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string Play = "Play";
        builder.Add("play.toggle", "Play", Play, TogglePlayAsync, () => documents.Active is not null && State != PlayState.Starting, "Ctrl+P", Icons.Play,
            "Plays the open scene; press again to stop.");
        builder.Add("play.start", "Play from start scene", Play, StartAsync, () => State == PlayState.Stopped, "Ctrl+Alt+Shift+P", Icons.SkipForward,
            "Starts the game from the project's start scene.");
        builder.Add("play.pause", "Pause", Play, TogglePause, () => IsPlaying, "Ctrl+Shift+P", Icons.Pause, "Pauses or resumes the play session.");
        builder.Add("play.step", "Step one frame", Play, Step, () => IsPlaying, "F10", Icons.StepForward, "Runs one frame and pauses.");
        builder.Add("play.stop", "Stop", Play, StopAsync, () => IsPlaying, "Shift+F5", Icons.Stop, "Stops playing and returns to editing.");
        builder.Menu(MenuPaths.Scene, "play.toggle", "play");
        builder.Menu(MenuPaths.Scene, "play.start", "play");
        builder.Menu(MenuPaths.Scene, "play.pause", "play");
        builder.Menu(MenuPaths.Scene, "play.step", "play");
        builder.Add("play.restart", "Restart play mode", Play, RestartAsync, () => IsPlaying, "Ctrl+Shift+F5", Icons.RotateCcw,
            "Stops and starts playing again, with the latest scripts.");
        builder.Menu(MenuPaths.Scene, "play.stop", "play");
        builder.Menu(MenuPaths.Scene, "play.restart", "play");
    }

    async Task<bool> ICloseGuard.CanCloseAsync()
    {
        await StopAsync();
        return true;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task BeginAsync(SceneRequest request, PlaySceneSource source, string message)
    {
        if (State != PlayState.Stopped)
            return;
        SetState(PlayState.Starting);
        try
        {
            await project.WhenReady;
            foreach (var contributor in _contributors)
            {
                if (await plugins.RunAsync(contributor, "prepare play mode", () => contributor.PrepareAsync(CancellationToken.None).AsTask(), null) is not { } reason)
                    continue;
                SetState(PlayState.Stopped);
                console.Warning($"Play mode did not start: {reason}");
                toasts.Show("Cannot play yet", reason, ToastKind.Warning);
                return;
            }

            var sessionRequest = _contributors.Aggregate(new GameSessionRequest(GameSessionKind.Play) { StartScene = request, DocumentSources = [source] },
                (current, contributor) => plugins.Run(contributor, "prepare play mode", () => contributor.Contribute(current), current));
            var session = await project.CreateSessionAsync(sessionRequest);
            Session = session;
            var game = session.Game;
            game.FramePublished += AnnounceOnce;
            game.Services.GetRequiredService<GameLifetime>().QuitRequested += () => Dispatcher.UIThread.Post(() => StopQuit(session));
            new SimulationThread(game, game.Services.GetRequiredService<ILogger<SimulationThread>>()).Start();
            console.Info(message);
            SetState(PlayState.Playing);

            void AnnounceOnce()
            {
                game.FramePublished -= AnnounceOnce;
                game.Services.GetRequiredService<IEventBus>().Publish(new PlayModeEntered(request));
            }
        }
        catch (Exception ex)
        {
            if (Session is { } failed)
                await failed.DisposeAsync();
            Session = null;
            SetState(PlayState.Stopped);
            console.Error($"Play mode could not start: {ex.Message}", ex, ConsoleSource.Game);
            toasts.Show("Could not start playing", ex.Message, ToastKind.Error);
        }
    }

    /// <summary>Stops play mode when its game asked to quit, as a standalone game would close.</summary>
    private void StopQuit(GameSession session)
    {
        if (!ReferenceEquals(Session, session))
            return;
        console.Info("The game quit, so play mode stopped", ConsoleSource.Game);
        _ = StopAsync();
    }

    /// <summary>Unpauses for one frame; runs on the game thread, where the frame then publishes and pauses again.</summary>
    private static void StepFrame(Game game)
    {
        game.IsPaused = false;
        game.FramePublished += PauseAgain;

        void PauseAgain()
        {
            game.FramePublished -= PauseAgain;
            game.IsPaused = true;
        }
    }

    private void SetState(PlayState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gives the play session the editor's copies of scenes: the played document by key, and unsaved scenes by path.</summary>
    private sealed class PlaySceneSource(IAssetCatalog catalog) : ISceneDocumentSource
    {
        private readonly Dictionary<string, SceneDocument> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SceneDocument> _byPath = new(AssetPath.Comparer);

        public void Add(string? key, string? path, SceneDocument document)
        {
            if (key is not null)
                _byKey[key] = document;
            if (path is not null)
                _byPath[AssetPath.Normalize(path)] = document;
        }

        public ValueTask<SceneDocument?> TryGetAsync(SceneRequest request, CancellationToken cancellationToken)
        {
            if (request.Get(InMemorySceneDocuments.DocumentParameter) is { } key && _byKey.TryGetValue(key, out var byKey))
                return ValueTask.FromResult<SceneDocument?>(byKey.Clone());
            var path = request.Get(DocumentScene.PathParameter);
            if (path is null && AssetGuid.TryParse(request.Get(DocumentScene.GuidParameter), null, out var guid) && catalog.TryGetPath(guid, out var guidPath))
                path = guidPath;
            return ValueTask.FromResult(path is not null && _byPath.TryGetValue(AssetPath.Normalize(path), out var byPath) ? byPath.Clone() : null);
        }
    }
}
