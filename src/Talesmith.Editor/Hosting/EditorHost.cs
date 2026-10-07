using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Talesmith.Avalonia.Hosting;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hub;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Viewport;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Hosting;

/// <summary>The default <see cref="IEditorHost"/>: shows the hub, and builds the editor's services and window for each opened project.</summary>
/// <remarks>Each project gets its own service provider, so editor plugins from the project's plugins can register services, and closing the
/// project disposes everything it used.</remarks>
public sealed partial class EditorHost(
    IServiceProvider services,
    ISettingsService settings,
    IThemeManager theme,
    IGameSessionFactory sessions,
    ILoggerFactory loggers) : IEditorHost
{
    public const string ThumbnailFile = "thumbnail.png";

    private readonly ILogger<EditorHost> _logger = loggers.CreateLogger<EditorHost>();
    private OpenProject? _open;
    private Window? _hub;

    public EditorProject? CurrentProject => _open?.Project;

    /// <summary>The editor window of the open project, or null.</summary>
    public EditorWindow? EditorWindow => _open?.Window;

    /// <summary>The open project's services, or null.</summary>
    internal IServiceProvider? ProjectServices => _open?.Provider;

    /// <summary>Raised after a project's editor window was shown.</summary>
    public event EventHandler? ProjectOpened;

    /// <summary>Shows the project hub as the main window.</summary>
    public void ShowHub()
    {
        _hub ??= CreateHub();
        SetMainWindow(_hub);
        _hub.Show();
        _hub.Activate();
    }

    public async Task<bool> OpenProjectAsync(string folder)
    {
        if (!EditorProject.TryResolve(folder, out var projectFolder))
            throw new IOException($"\"{folder}\" is not a Talesmith project: it has no assets/config/game.json.");
        var switching = _open is not null;
        if (switching && !await CloseAsync(showHub: false))
            return false;

        var project = new EditorProject(projectFolder);
        var splash = switching ? await ShowSplashAsync(project) : null;
        try
        {
            Remember(project);
            var console = new EditorConsole();
            var provider = await BuildAsync(project, console);
            var window = provider.GetRequiredService<EditorWindow>();
            _open = new OpenProject(project, provider, window);
            provider.GetRequiredService<ProjectService>().Start();
            window.Closed += OnEditorClosed;
            SetMainWindow(window);
            window.Show();
            window.Activate();
            CloseHub();
            _ = provider.GetRequiredService<ISceneDocumentService>().OpenStartupSceneAsync();
            ProjectOpened?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            splash?.CloseAfterFirstFrameOf(_open?.Window);
        }
    }

    /// <summary>Covers the moment between one project's window closing and the next one's opening, when no other window is up.</summary>
    private static async Task<SplashWindow> ShowSplashAsync(EditorProject project)
    {
        var splash = new SplashWindow { Status = $"Opening {project.Name}…" };
        splash.Show();
        await splash.WhenDrawnAsync();
        return splash;
    }

    public Task<bool> CloseProjectAsync() => CloseAsync(showHub: true);

    /// <summary>Builds the services of a project's editor: the app's shared services, the project, its plugins and every editor feature.</summary>
    public async Task<ServiceProvider> BuildAsync(EditorProject project, IConsole console)
    {
        var settingsFile = GameSettingsOrDefault(project);
        var pluginManager = new PluginManager(new PluginLoadOptions
        {
            PluginsDirectory = Path.Combine(project.AssetRoot, settingsFile.PluginsFolder),
            ConfigurationFile = Path.Combine(project.AssetRoot, GameSession.PluginConfigurationFile),
            LoadEditorAssemblies = true,
            LoadInMemory = true
        }, loggers.CreateLogger("Talesmith.Plugins"));

        try
        {
            var report = await Task.Run(() => pluginManager.Configure(new ServiceCollection()));
            foreach (var failed in report.Failed)
                console.Error($"The plugin {failed.DisplayName} did not load: {failed.Reason}", source: ConsoleSource.Plugin);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            console.Error($"The project's plugins could not be loaded: {ex.Message}", ex, ConsoleSource.Plugin);
        }

        var guard = new EditorPluginGuard(console, pluginManager.Assemblies);
        var editorPlugins = EditorPluginLoader.Load(pluginManager.Assemblies, guard);
        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(new ConsoleLoggerProvider(console, ConsoleSource.Editor)));
        collection.AddSingleton(project);
        collection.AddSingleton(pluginManager);
        collection.AddSingleton(console);
        collection.AddSingleton<IConsole>(console);
        collection.AddSingleton(settings);
        collection.AddSingleton(theme);
        collection.AddSingleton(sessions);
        collection.AddSingleton<IEditorHost>(this);
        collection.AddSingleton(guard);
        collection.AddTalesmithEditor();
        foreach (var plugin in editorPlugins)
            ConfigurePlugin(plugin, collection, guard);

        return collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });
    }

    private static void ConfigurePlugin(LoadedEditorPlugin plugin, IServiceCollection collection, EditorPluginGuard guard)
    {
        IServiceCollection staged = new ServiceCollection();
        foreach (var descriptor in collection)
            staged.Add(descriptor);
        if (!guard.Run(plugin.Instance, "register its editor services", () => plugin.Instance.ConfigureServices(staged)))
            return;
        collection.Clear();
        foreach (var descriptor in staged)
            collection.Add(descriptor);
    }

    private async Task<bool> CloseAsync(bool showHub)
    {
        if (_open is not { } open)
        {
            if (showHub)
                ShowHub();
            return true;
        }

        var shell = open.Provider.GetRequiredService<ShellViewModel>();
        if (!await shell.CanCloseAsync())
            return false;

        await SaveThumbnailAsync(open);
        open.Provider.GetService<ViewportService>()?.Persist();
        _open = null;
        if (showHub)
            ShowHub();
        open.Window.Closed -= OnEditorClosed;
        open.Window.CloseApproved = true;
        open.Window.Close();
        await DisposeAsync(open);
        return true;
    }

    private async void OnEditorClosed(object? sender, EventArgs e)
    {
        if (_open is not { } open || !ReferenceEquals(sender, open.Window))
            return;
        _open = null;
        try
        {
            open.Provider.GetService<ViewportService>()?.Persist();
            await DisposeAsync(open);
        }
        catch (Exception ex)
        {
            LogCloseFailed(_logger, ex, open.Project.Folder);
        }
        finally
        {
            Shutdown();
        }
    }

    internal void CloseHub()
    {
        if (_hub is not { } hub)
            return;
        _hub = null;
        hub.Close();
    }

    private static void Shutdown()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static async Task DisposeAsync(OpenProject open)
    {
        var plugins = open.Provider.GetRequiredService<PluginManager>();
        await open.Provider.DisposeAsync();
        plugins.Unload();
    }

    private static async Task SaveThumbnailAsync(OpenProject open)
    {
        if (open.Provider.GetService<SceneViewportPanel>() is not { } viewport || await viewport.CaptureAsync(TimeSpan.FromMilliseconds(400)) is not { } image)
            return;
        try
        {
            await Task.Run(() =>
            {
                using var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                System.Runtime.InteropServices.Marshal.Copy(image.Pixels, 0, bitmap.GetPixels(), image.Pixels.Length);
                var width = 480;
                var height = Math.Max(1, image.Height * width / Math.Max(1, image.Width));
                using var scaled = bitmap.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
                using var encoded = (scaled ?? bitmap).Encode(SKEncodedImageFormat.Png, 90);
                Directory.CreateDirectory(open.Project.StateFolder);
                File.WriteAllBytes(open.Project.GetStatePath(ThumbnailFile), encoded.ToArray());
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Remember(EditorProject project)
    {
        settings.Update(s =>
        {
            s.RecentProjects.RemoveAll(p => string.Equals(p.Path, project.Folder, StringComparison.Ordinal));
            s.RecentProjects.Insert(0, new RecentProject(project.Folder, project.Name, DateTime.UtcNow));
            if (s.RecentProjects.Count > 20)
                s.RecentProjects.RemoveRange(20, s.RecentProjects.Count - 20);
        });
    }

    private static GameSettings GameSettingsOrDefault(EditorProject project)
    {
        try
        {
            return GameSettings.Load(project.AssetRoot);
        }
        catch (InvalidDataException)
        {
            return new GameSettings();
        }
    }

    private HubWindow CreateHub()
    {
        var hub = services.GetRequiredService<HubWindow>();
        hub.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_hub, hub))
                return;
            _hub = null;
            if (_open is null)
                Shutdown();
        };
        return hub;
    }

    private static void SetMainWindow(Window window)
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The project {Folder} did not close cleanly")]
    private static partial void LogCloseFailed(ILogger logger, Exception exception, string folder);

    private sealed record OpenProject(EditorProject Project, ServiceProvider Provider, EditorWindow Window);
}
