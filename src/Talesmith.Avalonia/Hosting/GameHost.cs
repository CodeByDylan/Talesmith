using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Diagnostics;
using Talesmith.Avalonia.Overlays;
using Talesmith.Avalonia.Presentation;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Hosting;

/// <summary>A game view with the registered <see cref="IGameOverlay"/>s stacked above it on its view and, optionally, the developer tools.</summary>
/// <remarks>
/// With developer tools enabled: F3 toggles the performance overlay, F4 cycles debug views, F9 saves a profiler report and F12 saves a
/// screenshot.
/// </remarks>
public sealed partial class GameHost : Grid
{
    private readonly PerformanceOverlay? _performance;
    private readonly DeveloperTools? _tools;
    private readonly ILogger<GameHost> _logger;

    /// <param name="captureDirectory">Where developer tools save reports and screenshots; null disables developer tools.</param>
    public GameHost(Game game, IGamePresenter presenter, string? captureDirectory)
    {
        _logger = game.Services.GetRequiredService<ILogger<GameHost>>();
        View = new GameView(game, presenter);
        Overlays = new GameOverlayLayer(game.Viewport);
        Children.Add(View);
        Children.Add(Overlays);
        foreach (var overlay in game.Services.GetServices<IGameOverlay>().OrderBy(o => o.Order))
        {
            if (CreateOverlay(overlay, game.Services) is { } control)
                Overlays.Overlays.Add(control);
        }

        if (captureDirectory is null)
            return;
        _tools = new DeveloperTools(game, presenter, captureDirectory);
        _performance = new PerformanceOverlay(game) { IsVisible = game.Settings.ShowPerformanceOverlay };
        Children.Add(_performance);
        AddHandler(KeyDownEvent, OnDeveloperKey, RoutingStrategies.Tunnel);
    }

    public GameView View { get; }

    /// <summary>The layer holding the game's overlays on its view; developer tools stay outside it, unscaled.</summary>
    public GameOverlayLayer Overlays { get; }

    private Control? CreateOverlay(IGameOverlay overlay, IServiceProvider services)
    {
        try
        {
            return overlay.Create(services);
        }
        catch (Exception ex)
        {
            LogOverlayFailed(_logger, ex, overlay.GetType().FullName ?? overlay.GetType().Name);
            return null;
        }
    }

    private void OnDeveloperKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F3:
                _performance!.IsVisible = !_performance.IsVisible;
                break;
            case Key.F4:
                _ = CycleDebugViewsAsync();
                break;
            case Key.F9:
                _ = SaveReportAsync();
                break;
            case Key.F12:
                _ = SaveScreenshotAsync();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private async Task CycleDebugViewsAsync()
    {
        try
        {
            Report(await _tools!.CycleDebugViewsAsync());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveReportAsync()
    {
        try
        {
            Report("Saved " + await _tools!.SaveProfilerReportAsync());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("Could not save the report: " + ex.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveScreenshotAsync()
    {
        try
        {
            Report("Saved " + await _tools!.SaveScreenshotAsync());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report("Could not save the screenshot: " + ex.Message);
        }
    }

    private void Report(string message)
    {
        LogDeveloperTools(_logger, message);
        _performance!.IsVisible = true;
        _performance.ShowStatus(message);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The overlay {Overlay} could not be created and is left out")]
    private static partial void LogOverlayFailed(ILogger logger, Exception exception, string overlay);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Message}")]
    private static partial void LogDeveloperTools(ILogger logger, string message);
}
