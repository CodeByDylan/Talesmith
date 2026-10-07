using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Audio.OpenAL;
using Talesmith.Avalonia.Overlays;
using Talesmith.Avalonia.Presentation;
using Talesmith.Events;
using Talesmith.Lighting;
using Talesmith.Physics;
using Talesmith.Plugins;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Hosting;
using Talesmith.Scripting;
using Talesmith.VFX;

namespace Talesmith.Avalonia.Hosting;

/// <summary>A started game with its render backend and plugins; disposing it shuts the game down and releases the renderer.</summary>
/// <remarks>
/// The game's event bus receives <see cref="RenderBackendChanged"/> and a <see cref="PluginLoaded"/> per loaded plugin on its first frame,
/// and a <see cref="PluginUnloaded"/> per plugin when the session is disposed, before the game shuts down.
/// </remarks>
public sealed class GameSession : IAsyncDisposable
{
    /// <summary>Plugin switches, relative to the asset root.</summary>
    public const string PluginConfigurationFile = "config/plugins.json";

    private readonly IEventBus _events;
    private int _disposed;

    private GameSession(Game game, IRenderBackend backend, PluginLoadReport plugins, string backendReason)
    {
        Game = game;
        Backend = backend;
        Plugins = plugins;
        _events = game.Services.GetRequiredService<IEventBus>();
        _events.Enqueue(new RenderBackendChanged(null, backend.Renderer.Info, backendReason));
        foreach (var plugin in LoadedPlugins())
            _events.Enqueue(new PluginLoaded(plugin));
    }

    public Game Game { get; }

    public IRenderBackend Backend { get; }

    public PluginLoadReport Plugins { get; }

    /// <summary>Creates a session for a game shown in Avalonia windows, choosing the renderer that gets frames to the window fastest.</summary>
    /// <remarks>Call it on the UI thread once Avalonia is initialized. The game is built on the thread pool, so the UI stays responsive and
    /// loading screens keep moving meanwhile.</remarks>
    /// <exception cref="InvalidDataException">The game's settings or plugin configuration are not valid.</exception>
    public static async Task<GameSession> CreateForWindowAsync(DesktopGameOptions options, ILoggerFactory loggers, CancellationToken cancellationToken = default)
    {
        var window = await WindowGraphics.QueryAsync();
        return await Task.Run(() => Create(options, loggers, window), cancellationToken);
    }

    /// <summary>Reads the game's settings, creates the renderer, loads plugins and builds the game.</summary>
    /// <param name="window">What the window's compositor supports; <see cref="WindowGraphics.None"/> for headless sessions.</param>
    /// <exception cref="InvalidDataException">The game's settings or plugin configuration are not valid.</exception>
    public static GameSession Create(DesktopGameOptions options, ILoggerFactory loggers, WindowGraphics window)
    {
        var builder = GameBuilder.Create(options.AssetRoot);
        var logger = loggers.CreateLogger<GameSession>();
        var profilers = new EngineProfilers();
        var backend = RenderBackends.Create(options.Renderer ?? builder.Settings.Renderer, profilers.Render, loggers, window, out var backendReason);
        try
        {
            var services = builder.Services;
            services.AddSingleton(loggers);
            services.AddSingleton(profilers);
            if (Application.Current is not null)
                services.AddSingleton<IGameUi, AvaloniaGameUi>();
            if (options.Audio)
                services.AddOpenALAudio();
            services.AddTalesmithParticles();
            var physics = PhysicsConfiguration.Load(builder.AssetRoot);
            services.AddTalesmithPhysics(physics.ApplyTo);
            builder.UseRenderer(backend.Renderer);
            services.AddTalesmithLighting();

            var pluginOptions = new PluginLoadOptions
            {
                PluginsDirectory = Path.Combine(builder.AssetRoot, builder.Settings.PluginsFolder),
                ConfigurationFile = Path.Combine(builder.AssetRoot, PluginConfigurationFile)
            };
            var plugins = options.PluginManager is { } manager
                ? services.AddTalesmithPlugins(manager)
                : services.AddTalesmithPlugins(pluginOptions, logger);
            services.AddTalesmithScripting(scripting =>
            {
                scripting.AssetRoot = builder.AssetRoot;
                scripting.Assembly = options.Scripts;
            });

            options.Configure?.Invoke(builder);
            return new GameSession(builder.Build(), backend, plugins, backendReason);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Game.Simulation?.Dispose();
        foreach (var plugin in LoadedPlugins())
            _events.Publish(new PluginUnloaded(plugin));
        await Game.DisposeAsync().ConfigureAwait(false);
        Backend.Dispose();
    }

    private IEnumerable<PluginInfo> LoadedPlugins() => Plugins.Loaded.Select(entry => entry.Info).OfType<PluginInfo>();
}
