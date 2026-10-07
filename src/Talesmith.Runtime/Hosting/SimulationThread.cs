using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Talesmith.Runtime.Hosting;

/// <summary>Runs a game's frames on a dedicated thread paced by <see cref="FramePacing"/>, so stalls of the host's UI thread do not delay the simulation.</summary>
/// <remarks>
/// <para>With VSync, a frame runs on each display refresh the host reports through <see cref="SignalRefresh"/>, such as from the compositor's
/// animation frames, and a frame-rate cap skips refreshes to keep the average at the cap. When reports stop arriving, for example while the
/// UI thread is blocked, frames continue on the thread's own clock at <see cref="RefreshRate"/>; after <see cref="UnattendedAfter"/> without
/// reports, such as while no view shows the game, they slow down to <see cref="UnattendedFramesPerSecond"/>. Without VSync, frames run as fast
/// as <see cref="FramePacing.MaxFramesPerSecond"/> allows.</para>
/// <para>The thread never waits for the UI thread. Exceptions escaping a frame are logged and the next frame runs as usual.</para>
/// </remarks>
public sealed partial class SimulationThread : IDisposable
{
    /// <summary>How long without reported refreshes before frames slow down to <see cref="UnattendedFramesPerSecond"/>.</summary>
    public static readonly TimeSpan UnattendedAfter = TimeSpan.FromSeconds(1);

    /// <summary>The frame rate with VSync while no refreshes are reported for longer than <see cref="UnattendedAfter"/>.</summary>
    public const int UnattendedFramesPerSecond = 20;

    private readonly Game _game;
    private readonly FramePacing _pacing;
    private readonly ILogger _logger;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action _onPacingChanged;
    private Thread? _thread;
    private volatile bool _stopping;
    private bool _disposed;
    private int _refreshPending;
    private long _refreshIntervalTicks = Stopwatch.Frequency / 60;
    private long _minimumRefreshTicks;

    public SimulationThread(Game game, ILogger<SimulationThread>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;
        _pacing = game.Services.GetRequiredService<FramePacing>();
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _onPacingChanged = OnPacingChanged;
    }

    public Game Game => _game;

    /// <summary>Whether the thread was started and has not ended yet.</summary>
    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>The display's refresh rate in hertz, which paces VSync frames while no refreshes are reported; 60 by default.</summary>
    public double RefreshRate
    {
        get => Stopwatch.Frequency / (double)Interlocked.Read(ref _refreshIntervalTicks);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            Interlocked.Exchange(ref _refreshIntervalTicks, Math.Max(1, (long)(Stopwatch.Frequency / value)));
        }
    }

    /// <summary>The shortest time between VSync frames, for compositors whose refreshes arrive faster than the display refreshes; zero by default.</summary>
    public TimeSpan MinimumRefreshInterval
    {
        get => TimeSpan.FromSeconds(Interlocked.Read(ref _minimumRefreshTicks) / (double)Stopwatch.Frequency);
        set => Interlocked.Exchange(ref _minimumRefreshTicks, (long)(Math.Max(0, value.TotalSeconds) * Stopwatch.Frequency));
    }

    /// <summary>Starts the game and the thread that runs its frames.</summary>
    /// <exception cref="InvalidOperationException">The thread was started before, or another simulation thread runs the game.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thread is not null)
            throw new InvalidOperationException("The simulation thread was already started.");
        _game.AttachSimulation(this);
        _game.Start();
        _pacing.Changed += _onPacingChanged;
        _thread = new Thread(Run) { Name = "Talesmith simulation", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Reports a display refresh, such as an animation frame of the window's compositor; safe to call from any thread.</summary>
    public void SignalRefresh()
    {
        Volatile.Write(ref _refreshPending, 1);
        _wake.Set();
    }

    /// <summary>Asks the thread to end after its current frame and waits for it.</summary>
    /// <returns>Whether the thread ended in time; false when called on the simulation thread itself, which ends after this frame.</returns>
    public bool Stop(TimeSpan timeout)
    {
        if (_thread is not { IsAlive: true } thread)
            return true;
        _stopping = true;
        _wake.Set();
        if (thread == Thread.CurrentThread)
            return false;
        if (thread.Join(timeout))
            return true;
        LogStopTimedOut(_logger, timeout.TotalSeconds);
        return false;
    }

    /// <summary>Stops the thread and waits until it ended.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (Stop(Timeout.InfiniteTimeSpan))
            _wake.Dispose();
    }

    private void Run()
    {
        _game.BindGameThread();
        var last = Stopwatch.GetTimestamp();
        var schedule = new FrameSchedule(Stopwatch.Frequency, last);
        try
        {
            while (!_stopping)
            {
                if (!WaitForFrame(schedule))
                    continue;
                var now = Stopwatch.GetTimestamp();
                var delta = (now - last) / (double)Stopwatch.Frequency;
                last = now;
                try
                {
                    _game.Tick(delta);
                }
                catch (Exception ex)
                {
                    LogFrameFailed(_logger, ex);
                }
            }
        }
        finally
        {
            _pacing.Changed -= _onPacingChanged;
            _game.DetachSimulation(this);
        }
    }

    /// <summary>Waits until the next frame is due; false when woken early, such as by a pacing change or <see cref="Stop"/>.</summary>
    private bool WaitForFrame(FrameSchedule schedule) => _pacing.VSync ? WaitForRefresh(schedule) : WaitForDeadline(schedule);

    private bool WaitForRefresh(FrameSchedule schedule)
    {
        var interval = Interlocked.Read(ref _refreshIntervalTicks);
        var deadline = schedule.RefreshDeadline(Stopwatch.GetTimestamp(), interval);
        var reported = WaitForReport(deadline);
        if (_stopping || !_pacing.VSync)
            return false;

        var minimum = Math.Max(_pacing.MinimumFrameTime.TotalSeconds, Interlocked.Read(ref _minimumRefreshTicks) / (double)Stopwatch.Frequency);
        return schedule.RunsRefreshFrame(Stopwatch.GetTimestamp(), deadline, reported, interval, minimum);
    }

    /// <summary>Waits for a reported refresh until <paramref name="deadline"/>; false when none came, or when woken for another reason.</summary>
    private bool WaitForReport(long deadline)
    {
        while (true)
        {
            if (Interlocked.Exchange(ref _refreshPending, 0) != 0)
                return true;
            var remaining = deadline - Stopwatch.GetTimestamp();
            if (_stopping || !_pacing.VSync || remaining <= 0)
                return false;
            _wake.WaitOne(TimeSpan.FromSeconds(remaining / (double)Stopwatch.Frequency));
        }
    }

    private bool WaitForDeadline(FrameSchedule schedule)
    {
        var now = Stopwatch.GetTimestamp();
        var due = schedule.FrameDue(now);
        var minimum = _pacing.MinimumFrameTime;
        if (minimum <= TimeSpan.Zero)
            return true;

        while (now < due)
        {
            if (_stopping || _pacing.VSync)
                return false;
            if ((due - now) / (double)Stopwatch.Frequency > 0.002)
                _wake.WaitOne(1);
            else
                Thread.Yield();
            now = Stopwatch.GetTimestamp();
        }

        schedule.FrameStarted(now, (long)(minimum.TotalSeconds * Stopwatch.Frequency));
        return true;
    }

    private void OnPacingChanged()
    {
        if (!_disposed)
            _wake.Set();
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A frame on the simulation thread failed")]
    private static partial void LogFrameFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The simulation thread did not end within {Seconds:0.#} seconds; a frame may be stuck in game code")]
    private static partial void LogStopTimedOut(ILogger logger, double seconds);
}
