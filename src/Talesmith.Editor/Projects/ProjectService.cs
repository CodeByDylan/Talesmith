using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Database.Dependencies;
using Talesmith.Avalonia.Hosting;
using Talesmith.Editor.Console;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Settings;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Scripting;
using Talesmith.Systems;

namespace Talesmith.Editor.Projects;

/// <summary>The default <see cref="IProjectService"/>: creates the edit game, then scans and watches the asset folder in the background.</summary>
public sealed partial class ProjectService : IProjectService, IAsyncDisposable
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly IGameSessionFactory _sessions;
    private readonly IConsole _console;
    private readonly ISettingsService _settings;
    private readonly IEditorHost _host;
    private readonly IReadOnlyList<AssetKindRegistration> _kinds;
    private readonly ILogger<ProjectService> _logger;
    private readonly ILoggerFactory _gameLoggers;
    private readonly ILoggerFactory _editLoggers;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;
    private bool _disposed;

    public ProjectService(EditorProject project, PluginManager plugins, IGameSessionFactory sessions, IConsole console, ISettingsService settings,
        IEditorHost host, IEnumerable<AssetKindRegistration> kinds, ILogger<ProjectService> logger)
    {
        Project = project;
        Plugins = plugins;
        _sessions = sessions;
        _console = console;
        _settings = settings;
        _host = host;
        _kinds = [.. kinds];
        _logger = logger;
        _gameLoggers = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(new ConsoleLoggerProvider(console, ConsoleSource.Game)));
        _editLoggers = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Warning).AddProvider(new ConsoleLoggerProvider(console, ConsoleSource.Game)));
        Settings = LoadSettings();
    }

    public EditorProject Project { get; }

    public GameSettings Settings { get; private set; }

    public AssetCatalog Catalog { get; } = new();

    public AssetDatabase? Database { get; private set; }

    public AssetHotReload? HotReload { get; private set; }

    public PluginManager Plugins { get; }

    public GameSession? EditSession { get; private set; }

    public bool IsReady { get; private set; }

    public AssetScanProgress? ScanProgress { get; private set; }

    public Task WhenReady => _ready.Task;

    public event EventHandler? StatusChanged;

    public event EventHandler<EditSessionChangedEventArgs>? EditSessionChanged;

    /// <summary>Starts opening the project: creates the edit game and scans the assets. Call once on the UI thread.</summary>
    public void Start()
    {
        if (_started)
            return;
        _started = true;
        _ = OpenAsync();
    }

    public async Task<GameSession> CreateSessionAsync(GameSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var edit = request.Kind == GameSessionKind.Edit;
        var options = new DesktopGameOptions
        {
            AssetRoot = Project.AssetRoot,
            Renderer = _settings.Current.Renderer,
            DeveloperTools = false,
            Audio = !edit,
            VulkanCompositing = false,
            PluginManager = Plugins,
            Scripts = request.Scripts,
            Configure = builder =>
            {
                var services = builder.Services;
                services.AddSingleton(Catalog);
                var startScene = request.StartScene ?? (edit ? new SceneRequest(EmptyScene.SceneName) : builder.Settings.StartScene);
                services.AddSingleton(builder.Settings with
                {
                    StartScene = startScene,
                    PauseWhenInactive = !edit && builder.Settings.PauseWhenInactive,
                    ShowPerformanceOverlay = false
                });
                services.AddScene<EmptyScene>(EmptyScene.SceneName);
                foreach (var source in request.DocumentSources)
                    services.AddSingleton(source);
                request.Configure?.Invoke(builder);
            }
        };

        var session = await _sessions.CreateAsync(options, edit ? _editLoggers : _gameLoggers, cancellationToken);
        session.Game.Mode = edit ? ExecutionModes.Edit : ExecutionModes.Play;
        return session;
    }

    public async Task ReplaceEditSessionAsync(ScriptAssembly? scripts, CancellationToken cancellationToken = default)
    {
        if (EditSession is null)
            throw new InvalidOperationException("The edit game can be replaced once the project has opened.");
        var next = await CreateSessionAsync(new GameSessionRequest(GameSessionKind.Edit) { Scripts = scripts }, cancellationToken);
        if (_disposed || EditSession is not { } previous)
        {
            await next.DisposeAsync();
            return;
        }

        EditSession = next;
        if (HotReload is { } hotReload)
            hotReload.Assets = next.Game.Services.GetRequiredService<IAssetManager>();
        var args = new EditSessionChangedEventArgs(previous, next);
        EditSessionChanged?.Invoke(this, args);
        try
        {
            await args.WhenReleased;
        }
        finally
        {
            await previous.DisposeAsync();
        }
    }

    public async Task UpdateSettingsAsync(Action<JsonObject> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var path = Project.SettingsFile;
        JsonObject settings;
        try
        {
            var text = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : "{}";
            settings = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not valid: {ex.Message}", ex);
        }

        change(settings);
        var previous = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, settings.ToJsonString(WriteOptions), cancellationToken);
        try
        {
            Settings = GameSettings.Load(Project.AssetRoot);
        }
        catch (InvalidDataException)
        {
            if (previous is not null)
                await File.WriteAllBytesAsync(path, previous, CancellationToken.None);
            throw;
        }

        RaiseStatusChanged();
    }

    public async Task RescanAsync()
    {
        if (Database is null)
            return;
        await ScanAsync(Database);
    }

    public Task<bool> OpenAsync(string projectFolder) => _host.OpenProjectAsync(projectFolder);

    public Task<bool> CloseAsync() => _host.CloseProjectAsync();

    public Task<bool> ReloadAsync() => _host.OpenProjectAsync(Project.Folder);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        HotReload?.Dispose();
        if (Database is { } database)
        {
            await database.StopWatchingAsync();
            await database.DisposeAsync();
        }

        if (EditSession is { } session)
            await session.DisposeAsync();
        _gameLoggers.Dispose();
        _editLoggers.Dispose();
    }

    private async Task OpenAsync()
    {
        try
        {
            EditSession = await CreateSessionAsync(new GameSessionRequest(GameSessionKind.Edit));
            var services = EditSession.Game.Services;
            var kinds = new AssetKindRegistry(_kinds.Concat(services.GetServices<AssetKindRegistration>()));
            var database = new AssetDatabase(
                AssetDatabaseOptions.ForProject(Project.Folder) with { PluginsFolder = Settings.PluginsFolder },
                services.GetServices<IAssetImporter>(),
                _gameLoggers.CreateLogger<AssetDatabase>(),
                DefaultDependencyExtractors.Create().Concat(services.GetServices<IAssetDependencyExtractor>()),
                kinds,
                Catalog);
            Database = database;
            database.ScanProgressChanged += OnScanProgress;
            RaiseStatusChanged();

            await ScanAsync(database);
            database.StartWatching();
            HotReload = new AssetHotReload(database, EditSession.Game.Services.GetRequiredService<IAssetManager>(), _gameLoggers.CreateLogger<AssetHotReload>());
            IsReady = true;
            RaiseStatusChanged();
            _ready.TrySetResult();
        }
        catch (Exception ex)
        {
            LogOpenFailed(_logger, ex, Project.Folder);
            _console.Error($"The project could not open: {ex.Message}", ex);
            _ready.TrySetException(ex);
            RaiseStatusChanged();
        }
    }

    private async Task ScanAsync(AssetDatabase database)
    {
        var clock = Stopwatch.StartNew();
        ScanProgress = new AssetScanProgress(0, 0, null);
        RaiseStatusChanged();
        try
        {
            var result = await Task.Run(() => database.ScanAsync());
            _console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Info, ConsoleSource.Assets,
                $"Scanned {result.AssetCount:N0} assets in {clock.Elapsed.TotalMilliseconds:N0} ms" +
                (result.MetasCreated > 0 ? $", created {result.MetasCreated:N0} .meta files" : "")));
            foreach (var repair in result.Repairs)
                _console.Warning($"{repair.RepairedPath} had the same guid as {repair.KeptPath} and got a new one", ConsoleSource.Assets, new AssetTarget(repair.NewGuid, repair.RepairedPath));
            foreach (var invalid in result.InvalidMetas)
                _console.Warning($"{invalid.Path}.meta could not be read and was replaced: {invalid.Error}", ConsoleSource.Assets, new AssetTarget(default, invalid.Path));
        }
        finally
        {
            ScanProgress = null;
            RaiseStatusChanged();
        }
    }

    private void OnScanProgress(object? sender, AssetScanProgress progress)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ScanProgress is null)
                return;
            ScanProgress = progress;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private GameSettings LoadSettings()
    {
        try
        {
            return GameSettings.Load(Project.AssetRoot);
        }
        catch (InvalidDataException ex)
        {
            _console.Error(ex.Message, source: ConsoleSource.Editor, target: new FileTarget(Project.SettingsFile));
            return new GameSettings();
        }
    }

    private void RaiseStatusChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
            StatusChanged?.Invoke(this, EventArgs.Empty);
        else
            Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(this, EventArgs.Empty));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The project {Folder} could not open")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string folder);
}
