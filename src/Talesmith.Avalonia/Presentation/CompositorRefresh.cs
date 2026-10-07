using System.Diagnostics;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Reports every animation frame of a window's compositor on the UI thread, and checks that they follow the display.</summary>
/// <remarks>When animation frames arrive faster than the display refreshes, <see cref="DisplayFrameTime"/> becomes the display's refresh
/// interval and a warning is logged.</remarks>
internal sealed partial class CompositorRefresh
{
    private readonly TopLevel _topLevel;
    private readonly CompositorPacingCheck _pacingCheck;
    private readonly ILogger _logger;
    private readonly Action<long> _refresh;
    private readonly Action<TimeSpan> _onAnimationFrame;
    private bool _running;
    private bool _pending;

    /// <param name="displayRate">Gets the refresh rate of the window's monitor in hertz, or null when it is unknown.</param>
    /// <param name="refresh">Called on the UI thread for each animation frame with its <see cref="Stopwatch"/> timestamp.</param>
    public CompositorRefresh(TopLevel topLevel, Func<double?> displayRate, ILogger logger, Action<long> refresh)
    {
        _topLevel = topLevel;
        _pacingCheck = new CompositorPacingCheck(displayRate);
        _logger = logger;
        _refresh = refresh;
        _onAnimationFrame = OnAnimationFrame;
    }

    /// <summary>The shortest time between frames in seconds when the compositor does not follow the display, or zero when it does.</summary>
    public double DisplayFrameTime { get; private set; }

    /// <summary>How often animation frames arrive, once the pacing check is complete: the display's rate if known, otherwise as measured.</summary>
    public double? RefreshRate => _pacingCheck.IsComplete ? _pacingCheck.DisplayRate ?? _pacingCheck.FrameRate : null;

    public void Start()
    {
        if (_running)
            return;
        _running = true;
        Request();
    }

    public void Stop() => _running = false;

    private void Request()
    {
        if (_pending)
            return;
        _pending = true;
        _topLevel.RequestAnimationFrame(_onAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _pending = false;
        if (!_running)
            return;

        var now = Stopwatch.GetTimestamp();
        var wasChecked = _pacingCheck.IsComplete;
        if (_pacingCheck.Observe(now))
        {
            DisplayFrameTime = 1 / _pacingCheck.DisplayRate!.Value;
            LogCompositorNotSynced(_logger, _pacingCheck.FrameRate, _pacingCheck.DisplayRate.Value);
        }
        else if (!wasChecked && _pacingCheck.IsComplete)
        {
            LogCompositorPacing(_logger, _pacingCheck.FrameRate, _pacingCheck.DisplayRate);
        }

        _refresh(now);
        if (_running)
            Request();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "VSync: the window's compositor runs {FrameRate:0.0} frames per second; the display refreshes at {DisplayRate:0.0} Hz")]
    private static partial void LogCompositorPacing(ILogger logger, double frameRate, double? displayRate);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VSync: the window's compositor runs {FrameRate:0} frames per second on a {DisplayRate:0} Hz display, so it " +
        "does not wait for the display. Frames are capped at the display's refresh rate instead, but may still stutter. For smooth VSync, start " +
        "with --opengl-compositor, or use a Vulkan driver that honors MESA_VK_WSI_PRESENT_MODE=fifo")]
    private static partial void LogCompositorNotSynced(ILogger logger, double frameRate, double displayRate);
}
