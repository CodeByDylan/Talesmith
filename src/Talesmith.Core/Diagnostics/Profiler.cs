using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Talesmith.Diagnostics;

/// <summary>Measures frame times, marked code sections and counters for one thread, keeping a history of recent frames.</summary>
/// <remarks>
/// Call <see cref="BeginFrame"/> and <see cref="EndFrame"/> around each frame and wrap sections in
/// <c>using (profiler.Measure(marker)) { … }</c>. Measuring costs two timestamp reads and does not allocate. A profiler belongs to
/// one thread; use one per thread, such as the game loop and the render thread. Nested sections are timed inclusively, and each
/// marker's parent is learned from the nesting so tools can show a tree.
/// <para>Other threads, such as overlays on the UI thread, may read completed frames through <see cref="GetRecent"/> and
/// <see cref="GetStatistics"/>; a reader that falls a whole history behind can see a recycled frame's newer values.</para>
/// </remarks>
public sealed class Profiler
{
    /// <summary>Converts <see cref="Stopwatch"/> ticks to milliseconds.</summary>
    public static readonly double MillisecondsPerTick = 1000.0 / Stopwatch.Frequency;

    private readonly FrameSample[] _history;
    private readonly int[] _stack = new int[128];
    private readonly long[] _allocationStack = new long[128];
    private int[] _parents = [];
    private double[] _gauges = [];
    private FrameSample? _current;
    private int _head = -1;
    private volatile int _completed = -1;
    private long _frameCount;
    private int _depth;
    private long _frameStart;
    private long _previousFrameStart;
    private long _allocatedAtStart;
    private int _gen0AtStart;
    private int _gen1AtStart;
    private int _gen2AtStart;

    public Profiler(string name, int historyLength = 600)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(historyLength, 2);
        Name = name;
        _history = new FrameSample[historyLength];
        for (var i = 0; i < historyLength; i++)
            _history[i] = new FrameSample();
    }

    /// <summary>A label such as "Game" or "Render" used in reports and the overlay.</summary>
    public string Name { get; }

    /// <summary>When false, frames and sections are not recorded; measuring then costs almost nothing.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The number of completed frames.</summary>
    public long FrameCount => Volatile.Read(ref _frameCount);

    public int HistoryLength => _history.Length;

    /// <summary>The most recently completed frame, or null before the first one.</summary>
    public FrameSample? LastFrame => _completed is var completed and >= 0 ? _history[completed] : null;

    /// <summary>Raised on the profiling thread after each completed frame.</summary>
    public event Action<Profiler>? FrameCompleted;

    public void BeginFrame()
    {
        if (!Enabled)
            return;

        var now = Stopwatch.GetTimestamp();
        _head = (_head + 1) % _history.Length;
        _current = _history[_head];
        _current.Reset(ProfilerMarker.Count, ProfilerCounter.Count);
        EnsureCapacity();

        _current.Frame = _frameCount + 1;
        _current.IntervalMilliseconds = _previousFrameStart == 0 ? 0 : (now - _previousFrameStart) * MillisecondsPerTick;
        for (var i = 0; i < _gauges.Length; i++)
            _current.CounterValues[i] = _gauges[i];

        _previousFrameStart = now;
        _frameStart = now;
        _depth = 0;
        _allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
        _gen0AtStart = GC.CollectionCount(0);
        _gen1AtStart = GC.CollectionCount(1);
        _gen2AtStart = GC.CollectionCount(2);
    }

    public void EndFrame()
    {
        if (_current is null)
            return;

        _current.WorkMilliseconds = (Stopwatch.GetTimestamp() - _frameStart) * MillisecondsPerTick;
        _current.AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _allocatedAtStart;
        _current.Collections = new CollectionCounts(GC.CollectionCount(0) - _gen0AtStart, GC.CollectionCount(1) - _gen1AtStart, GC.CollectionCount(2) - _gen2AtStart);
        _current = null;
        _completed = _head;
        Volatile.Write(ref _frameCount, _frameCount + 1);
        FrameCompleted?.Invoke(this);
    }

    /// <summary>Starts timing a section; dispose the returned scope to stop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ProfilerScope Measure(ProfilerMarker marker) =>
        _current is null ? default : new ProfilerScope(this, marker.Id, Begin(marker.Id));

    /// <summary>Adds to a counter for the current frame.</summary>
    public void Increment(ProfilerCounter counter, double amount = 1)
    {
        if (_current is null)
            return;
        if (!Fits(counter))
            EnsureCapacity();
        _current.CounterValues[counter.Id] += amount;
        if (counter.Kind == CounterKind.Gauge)
            _gauges[counter.Id] = _current.CounterValues[counter.Id];
    }

    /// <summary>Sets a counter for the current frame; gauges keep the value for later frames.</summary>
    public void Set(ProfilerCounter counter, double value)
    {
        if (!Fits(counter))
            EnsureCapacity();
        if (counter.Kind == CounterKind.Gauge)
            _gauges[counter.Id] = value;
        if (_current is not null)
            _current.CounterValues[counter.Id] = value;
    }

    /// <summary>Gets the section that most often encloses <paramref name="marker"/>, or null for top-level sections.</summary>
    public ProfilerMarker? ParentOf(ProfilerMarker marker)
    {
        var parents = _parents;
        return marker.Id < parents.Length && parents[marker.Id] > 0 ? ProfilerMarker.All[parents[marker.Id] - 1] : null;
    }

    /// <summary>Copies up to <paramref name="count"/> recent completed frames, oldest first, into <paramref name="destination"/>.</summary>
    /// <remarks>At most <see cref="HistoryLength"/> minus one frames are available, so the frame being recorded is never included.</remarks>
    public void GetRecent(int count, List<FrameSample> destination)
    {
        destination.Clear();
        var head = _completed;
        if (head < 0)
            return;
        var available = (int)Math.Min(Math.Min(count, FrameCount), _history.Length - 1);
        for (var i = available - 1; i >= 0; i--)
        {
            var index = (head - i + _history.Length) % _history.Length;
            destination.Add(_history[index]);
        }
    }

    /// <summary>Summarizes up to <paramref name="frames"/> recent frames.</summary>
    public ProfilerStatistics GetStatistics(int frames = 120)
    {
        var samples = new List<FrameSample>(Math.Min(frames, _history.Length));
        GetRecent(frames, samples);
        return ProfilerStatistics.Compute(this, samples);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long Begin(int markerId)
    {
        if (_depth < _stack.Length)
        {
            _stack[_depth] = markerId;
            _allocationStack[_depth] = GC.GetAllocatedBytesForCurrentThread();
        }

        _depth++;
        return Stopwatch.GetTimestamp();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void End(int markerId, long start)
    {
        var elapsed = Stopwatch.GetTimestamp() - start;
        _depth = Math.Max(0, _depth - 1);
        var current = _current;
        if (current is null)
            return;
        if (markerId >= current.MarkerTicks.Length || markerId >= _parents.Length)
            EnsureCapacity();

        current.MarkerTicks[markerId] += elapsed;
        current.MarkerCalls[markerId]++;
        if (_depth < _allocationStack.Length)
            current.MarkerAllocated[markerId] += GC.GetAllocatedBytesForCurrentThread() - _allocationStack[_depth];
        if (_depth > 0 && _depth <= _stack.Length && markerId < _parents.Length && _parents[markerId] == 0)
            _parents[markerId] = _stack[_depth - 1] + 1;
    }

    /// <summary>
    /// Whether the counter has a slot already. Growing only for counters that do not keeps sections from being charged with the
    /// profiler's own allocations when other code registers markers or counters while a frame runs.
    /// </summary>
    private bool Fits(ProfilerCounter counter) =>
        counter.Id < _gauges.Length && (_current is null || counter.Id < _current.CounterValues.Length);

    private void EnsureCapacity()
    {
        var markers = ProfilerMarker.Count;
        if (_parents.Length < markers)
            Array.Resize(ref _parents, markers);
        var counters = ProfilerCounter.Count;
        if (_gauges.Length < counters)
            Array.Resize(ref _gauges, counters);
        if (_current is not null && _current.MarkerTicks.Length < markers)
        {
            Array.Resize(ref _current.MarkerTicks, markers);
            Array.Resize(ref _current.MarkerCalls, markers);
            Array.Resize(ref _current.MarkerAllocated, markers);
        }

        if (_current is not null && _current.CounterValues.Length < counters)
            Array.Resize(ref _current.CounterValues, counters);
    }
}

/// <summary>Times a section until disposed. Created by <see cref="Profiler.Measure"/>.</summary>
public readonly ref struct ProfilerScope
{
    private readonly Profiler? _profiler;
    private readonly int _markerId;
    private readonly long _start;

    internal ProfilerScope(Profiler profiler, int markerId, long start)
    {
        _profiler = profiler;
        _markerId = markerId;
        _start = start;
    }

    public void Dispose() => _profiler?.End(_markerId, _start);
}
