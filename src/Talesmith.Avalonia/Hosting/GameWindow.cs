using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Avalonia.Loading;
using Talesmith.Avalonia.Presentation;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Hosting;

/// <summary>A window that shows one game: its loading screen from the moment it opens, then the game once its session is attached. It closes
/// when the game quits and ends the session when closed.</summary>
public sealed partial class GameWindow : Window
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly Panel _root = new();
    private readonly string? _captureDirectory;
    private readonly GameThreading _threading;
    private readonly Action _onQuitRequested;
    private GameSession? _session;
    private IGamePresenter? _presenter;
    private bool _closed;

    /// <summary>Opens on the game's loading screen, before the game exists; <see cref="Attach"/> brings in the game.</summary>
    /// <param name="settings">The game's settings, for the window's title and size and the loading screen.</param>
    /// <param name="assets">Where the loading screen reads its image from; null shows the game's title.</param>
    /// <param name="captureDirectory">Where developer tools save files; null disables developer tools.</param>
    /// <param name="threading">With <see cref="GameThreading.Dedicated"/>, the game runs on its simulation thread as soon as it is attached.</param>
    /// <param name="logger">Where the loading screen reports an image it cannot show.</param>
    public GameWindow(GameSettings settings, IAssetSource? assets, string? captureDirectory, GameThreading threading = GameThreading.Dedicated,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _captureDirectory = captureDirectory;
        _threading = threading;
        _onQuitRequested = OnQuitRequested;
        Title = settings.Title;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
        MinWidth = 320;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var clear = settings.ClearColor;
        Background = new SolidColorBrush(Color.FromRgb(clear.R, clear.G, clear.B));
        LoadingScreen = new LoadingScreen(settings, assets, logger) { CanClose = true };
        LoadingScreen.CloseRequested += (_, _) => Close();
        _root.Children.Add(LoadingScreen);
        Content = _root;
    }

    /// <summary>Shows a session that exists already, starting on its loading screen until the start scene is shown.</summary>
    /// <param name="captureDirectory">Where developer tools save files; null disables developer tools.</param>
    /// <param name="threading">With <see cref="GameThreading.Dedicated"/>, the game starts running on its simulation thread right away.</param>
    public GameWindow(GameSession session, string? captureDirectory, GameThreading threading = GameThreading.Dedicated)
        : this(session.Game.Settings, session.Game.Services.GetRequiredService<IAssetManager>().Source, captureDirectory, threading,
            session.Game.Services.GetRequiredService<ILogger<LoadingScreen>>()) =>
        Attach(session);

    /// <summary>The game view with its overlays, or null until a session is attached.</summary>
    public GameHost? Host { get; private set; }

    /// <summary>Covers the game while it starts and during slow scene changes, and shows why the game could not start.</summary>
    public LoadingScreen LoadingScreen { get; }

    /// <summary>Starts the game in the window, under the loading screen until its start scene is shown.</summary>
    /// <remarks>When the window has closed in the meantime, as a player may do during a long start, the session is ended instead.</remarks>
    /// <exception cref="InvalidOperationException">The window already shows a game.</exception>
    public void Attach(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_session is not null)
            throw new InvalidOperationException("The window already shows a game.");
        _session = session;
        if (_closed)
        {
            _ = EndAsync(session);
            return;
        }

        var game = session.Game;
        _presenter = session.Backend.CreatePresenter(game);
        game.Services.GetRequiredService<GameLifetime>().QuitRequested += _onQuitRequested;
        if (_threading == GameThreading.Dedicated)
            new SimulationThread(game, game.Services.GetRequiredService<ILogger<SimulationThread>>()).Start();
        Host = new GameHost(game, _presenter, _captureDirectory);
        _root.Children.Insert(0, Host);
        LoadingScreen.Follow(game);
        Host.View.Focus();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Host?.View.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _closed = true;
        Content = null;
        if (_session is { } session)
            _ = EndAsync(session);
    }

    /// <summary>Stops the game and releases its renderer and session.</summary>
    private async Task EndAsync(GameSession session)
    {
        var game = session.Game;
        var logger = game.Services.GetRequiredService<ILogger<GameWindow>>();
        game.Services.GetRequiredService<GameLifetime>().QuitRequested -= _onQuitRequested;
        if (game.Simulation is { } simulation && !simulation.Stop(StopTimeout))
            return;
        _presenter?.Dispose();
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            LogDisposeFailed(logger, ex);
        }
    }

    /// <remarks>Raised on the thread that asked to quit, which may be the simulation thread.</remarks>
    private void OnQuitRequested() => Dispatcher.UIThread.Post(Close);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ending the game session failed")]
    private static partial void LogDisposeFailed(ILogger logger, Exception exception);
}
