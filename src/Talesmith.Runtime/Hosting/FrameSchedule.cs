namespace Talesmith.Runtime.Hosting;

/// <summary>Decides when a <see cref="SimulationThread"/> runs frames, on the timestamps of any clock.</summary>
/// <remarks>The thread does the waiting and this class only decides, so the same decisions also run on simulated time.</remarks>
public sealed class FrameSchedule
{
    /// <summary>How many refresh intervals after a reported refresh to wait for the next one before running a frame on the own clock.</summary>
    private const double LateRefreshFactor = 1.5;

    private readonly long _unattendedTicks;
    private readonly long _unattendedIntervalTicks;
    private readonly RefreshCap _cap = new();
    private bool _wasVSync;
    private bool _lastWasReported;
    private long _lastRefresh;
    private long _lastReported;
    private long _nextFrame;

    /// <param name="frequency">The clock's ticks per second.</param>
    /// <param name="start">The clock's time when the thread starts.</param>
    public FrameSchedule(long frequency, long start)
    {
        Frequency = frequency;
        _unattendedTicks = (long)(SimulationThread.UnattendedAfter.TotalSeconds * frequency);
        _unattendedIntervalTicks = frequency / SimulationThread.UnattendedFramesPerSecond;
        _lastRefresh = _lastReported = _nextFrame = start;
    }

    public long Frequency { get; }

    /// <summary>With VSync, until when to wait for a reported refresh before a frame runs on the own clock.</summary>
    /// <param name="refreshInterval">The display's refresh interval in ticks.</param>
    public long RefreshDeadline(long now, long refreshInterval)
    {
        if (!_wasVSync)
        {
            _wasVSync = true;
            _cap.Reset();
            _lastRefresh = _lastReported = now;
        }

        var wait = now - _lastReported > _unattendedTicks ? _unattendedIntervalTicks
            : _lastWasReported ? (long)(refreshInterval * LateRefreshFactor) : refreshInterval;
        return _lastRefresh + wait;
    }

    /// <summary>With VSync, whether a frame runs after waiting for a refresh until <paramref name="deadline"/>.</summary>
    /// <param name="reported">Whether a refresh was reported, which ends the wait early.</param>
    /// <param name="minimumSeconds">The shortest average time between frames, such as for a frame-rate cap; zero for none.</param>
    public bool RunsRefreshFrame(long now, long deadline, bool reported, long refreshInterval, double minimumSeconds)
    {
        long refresh;
        if (reported)
            refresh = _lastReported = now;
        else if (now >= deadline)
            refresh = now - deadline < refreshInterval ? deadline : now;
        else
            return false;

        _lastWasReported = reported;
        var elapsed = (refresh - _lastRefresh) / (double)Frequency;
        _lastRefresh = refresh;
        return _cap.ShouldRun(elapsed, minimumSeconds);
    }

    /// <summary>Without VSync, when the next frame is due.</summary>
    public long FrameDue(long now)
    {
        if (_wasVSync)
        {
            _wasVSync = false;
            _nextFrame = now;
        }

        return _nextFrame;
    }

    /// <summary>Without VSync, records a frame starting at <paramref name="now"/>, so the next one is due <paramref name="minimumTicks"/> later.</summary>
    public void FrameStarted(long now, long minimumTicks)
    {
        _nextFrame += minimumTicks;
        if (_nextFrame < now)
            _nextFrame = now;
    }
}
