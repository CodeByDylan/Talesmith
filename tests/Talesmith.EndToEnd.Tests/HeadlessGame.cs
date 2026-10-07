using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Ecs;
using Talesmith.Imaging;
using Talesmith.Input;
using Talesmith.Plugins;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Scripting;

namespace Talesmith.EndToEnd.Tests;

/// <summary>A game session without a window, ticked by the test on its own thread, rendering with Skia on the CPU.</summary>
internal sealed class HeadlessGame : IAsyncDisposable
{
    public const int Width = 1280;
    public const int Height = 720;

    private HeadlessGame(GameSession session) => Session = session;

    public GameSession Session { get; }

    public Game Game => Session.Game;

    public World World => Game.Scenes.Current?.World ?? throw new InvalidOperationException("No scene is active.");

    public IInputSink Input => Game.Services.GetRequiredService<IInputSink>();

    /// <summary>Starts the game in an asset folder and waits until its start scene is active.</summary>
    public static async Task<HeadlessGame> StartAsync(string assetRoot, ILoggerFactory loggers, ScriptAssembly? scripts = null, PluginManager? plugins = null)
    {
        var session = GameSession.Create(new DesktopGameOptions
        {
            AssetRoot = assetRoot,
            Renderer = RendererPreference.Skia,
            Audio = false,
            DeveloperTools = false,
            Threading = GameThreading.Host,
            Scripts = scripts,
            PluginManager = plugins
        }, loggers, WindowGraphics.None);
        var game = new HeadlessGame(session);
        session.Game.Viewport.Size = new Vector2(Width, Height);
        game.Input.FocusChanged(true);
        session.Game.Start();

        var clock = Stopwatch.StartNew();
        while (session.Game.Scenes.Current is null || session.Game.Scenes.IsLoading)
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(60))
                throw new TimeoutException("The start scene did not load.");
            session.Game.Tick(1.0 / 60);
            await Task.Delay(2, TestContext.Current.CancellationToken);
        }

        return game;
    }

    /// <summary>Runs frames of 1/60 second, optionally rendering each one offscreen.</summary>
    public void Run(int frames, bool render = false, Action<int>? beforeFrame = null)
    {
        var renderer = (IOffscreenRenderer)Session.Backend.Renderer;
        for (var i = 0; i < frames; i++)
        {
            beforeFrame?.Invoke(i);
            Game.Tick(1.0 / 60);
            if (!render || Game.Frames.BeginRead() is not { } frame)
                continue;
            try
            {
                renderer.RenderOffscreen(frame, Width, Height);
            }
            finally
            {
                Game.Frames.EndRead(frame);
            }
        }
    }

    /// <summary>Renders the newest frame to pixels.</summary>
    public ImageData Capture()
    {
        Game.Tick(1.0 / 60);
        var frame = Game.Frames.BeginRead() ?? throw new InvalidOperationException("No frame was published.");
        try
        {
            return ((IOffscreenRenderer)Session.Backend.Renderer).RenderToImage(frame, Width, Height);
        }
        finally
        {
            Game.Frames.EndRead(frame);
        }
    }

    /// <summary>The entity with a <see cref="Name"/>, or <see cref="Entity.Null"/>.</summary>
    public Entity Find(string name)
    {
        foreach (var archetype in World.Query<Name>())
        {
            var names = archetype.GetSpan<Name>();
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i].Value == name)
                    return archetype.Entities[i];
            }
        }

        return Entity.Null;
    }

    public int Count<T>()
    {
        var count = 0;
        foreach (var archetype in World.Query<T>())
            count += archetype.Count;
        return count;
    }

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}
