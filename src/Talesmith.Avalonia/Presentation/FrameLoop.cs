using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Calls a frame callback on the UI thread, paced by <see cref="FramePacing"/>; drives games that the host ticks itself.</summary>
/// <remarks>
/// With VSync, frames follow the compositor's animation frames (see <see cref="CompositorRefresh"/>), and a frame-rate cap skips refreshes to
/// keep the average at the cap. Without VSync, a pacing thread schedules frames as fast as the cap allows; each frame is posted below input
/// and rendering, so the UI stays responsive. The window still shows the newest frame at the display's refresh rate.
/// </remarks>
internal sealed class FrameLoop
{
    private readonly FramePacing _pacing;
    private readonly Action<double> _frame;
    private readonly CompositorRefresh _refresh;
    private readonly RefreshCap _cap = new();
    private readonly Action _onPacingChanged;
    private readonly Dispatcher _dispatcher;
    private FreeRunner? _freeRunner;
    private bool _running;
    private long _lastFrame;
    private long _lastRefresh;

    /// <param name="frame">Runs one frame; receives the seconds since the previous one.</param>
    /// <param name="displayRate">Gets the refresh rate of the window's monitor in hertz, or null when it is unknown.</param>
    public FrameLoop(TopLevel topLevel, FramePacing pacing, Action<double> frame, Func<double?> displayRate, ILogger logger)
    {
        _pacing = pacing;
        _frame = frame;
        _dispatcher = topLevel.Dispatcher;
        _refresh = new CompositorRefresh(topLevel, displayRate, logger, OnRefresh);
        _onPacingChanged = OnPacingChanged;
    }

    public void Start()
    {
        if (_running)
            return;
        _running = true;
        _lastFrame = Stopwatch.GetTimestamp();
        _pacing.Changed += _onPacingChanged;
        Apply();
    }

    public void Stop()
    {
        if (!_running)
            return;
        _running = false;
        _pacing.Changed -= _onPacingChanged;
        _refresh.Stop();
        StopFreeRunning();
    }

    private void OnPacingChanged()
    {
        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Post(Apply);
    }

    private void Apply()
    {
        if (!_running)
            return;
        if (_pacing.VSync)
        {
            StopFreeRunning();
            _lastRefresh = Stopwatch.GetTimestamp();
            _cap.Reset();
            _refresh.Start();
        }
        else
        {
            _refresh.Stop();
            _freeRunner ??= new FreeRunner(this);
        }
    }

    private void OnRefresh(long now)
    {
        if (!_running || !_pacing.VSync)
            return;
        var refresh = Stopwatch.GetElapsedTime(_lastRefresh, now).TotalSeconds;
        _lastRefresh = now;
        if (_cap.ShouldRun(refresh, Math.Max(_pacing.MinimumFrameTime.TotalSeconds, _refresh.DisplayFrameTime)))
            RunFrame(now);
    }

    private void RunFrame(long now)
    {
        var delta = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
        _lastFrame = now;
        _frame(delta);
    }

    private void StopFreeRunning()
    {
        _freeRunner?.Stop();
        _freeRunner = null;
    }

    /// <summary>A pacing thread that posts one frame at a time to the UI thread and waits for it before scheduling the next.</summary>
    private sealed class FreeRunner
    {
        private const double SleepMarginSeconds = 0.002;

        /// <summary>How long <see cref="Stop"/> waits for the pacing thread, which ends within a millisecond unless something is badly wrong.</summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

        private readonly FrameLoop _loop;
        private readonly object _lock = new();
        private readonly Action _runFrame;
        private readonly Thread _thread;
        private bool _frameDone;
        private volatile bool _running = true;

        public FreeRunner(FrameLoop loop)
        {
            _loop = loop;
            _runFrame = RunFrame;
            _thread = new Thread(Pace) { Name = "Talesmith frame pacing", IsBackground = true };
            _thread.Start();
        }

        /// <summary>Ends the pacing thread and waits for it, so no frame is posted once this returns.</summary>
        public void Stop()
        {
            lock (_lock)
            {
                _running = false;
                _frameDone = true;
                Monitor.PulseAll(_lock);
            }

            _thread.Join(StopTimeout);
        }

        private void Pace()
        {
            var next = Stopwatch.GetTimestamp();
            while (true)
            {
                var minimum = _loop._pacing.MinimumFrameTime;
                if (minimum > TimeSpan.Zero)
                {
                    WaitUntil(next);
                    var now = Stopwatch.GetTimestamp();
                    next += (long)(minimum.TotalSeconds * Stopwatch.Frequency);
                    if (next < now)
                        next = now;
                }

                lock (_lock)
                {
                    if (!_running)
                        return;
                    _frameDone = false;
                }

                _loop._dispatcher.Post(_runFrame, DispatcherPriority.Background);
                lock (_lock)
                {
                    while (!_frameDone)
                        Monitor.Wait(_lock);
                }
            }
        }

        private void RunFrame()
        {
            if (_running && _loop._running)
                _loop.RunFrame(Stopwatch.GetTimestamp());
            lock (_lock)
            {
                _frameDone = true;
                Monitor.PulseAll(_lock);
            }
        }

        private void WaitUntil(long timestamp)
        {
            while (_running)
            {
                var remaining = (timestamp - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency;
                if (remaining <= 0)
                    return;
                if (remaining > SleepMarginSeconds)
                    Thread.Sleep(1);
                else
                    Thread.Yield();
            }
        }
    }
}
