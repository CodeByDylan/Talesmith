namespace Talesmith.Diagnostics;

/// <summary>Everything a <see cref="Profiler"/> measured during one frame.</summary>
/// <remarks>Instances are reused by the profiler's ring buffer; copy what you need instead of keeping a reference.</remarks>
public sealed class FrameSample
{
    internal long[] MarkerTicks = [];
    internal int[] MarkerCalls = [];
    internal long[] MarkerAllocated = [];
    internal double[] CounterValues = [];

    /// <summary>The frame number, counted from 1.</summary>
    public long Frame { get; internal set; }

    /// <summary>Real time between the start of this frame and the start of the previous one, in milliseconds.</summary>
    public double IntervalMilliseconds { get; internal set; }

    /// <summary>Time spent between <see cref="Profiler.BeginFrame"/> and <see cref="Profiler.EndFrame"/>, in milliseconds.</summary>
    public double WorkMilliseconds { get; internal set; }

    /// <summary>Managed bytes allocated on the measuring thread during the frame.</summary>
    public long AllocatedBytes { get; internal set; }

    /// <summary>Garbage collections of generations 0, 1 and 2 that happened during the frame.</summary>
    public CollectionCounts Collections { get; internal set; }

    public double MarkerMilliseconds(ProfilerMarker marker) =>
        marker.Id < MarkerTicks.Length ? MarkerTicks[marker.Id] * Profiler.MillisecondsPerTick : 0;

    public int MarkerCallCount(ProfilerMarker marker) => marker.Id < MarkerCalls.Length ? MarkerCalls[marker.Id] : 0;

    /// <summary>Managed bytes allocated inside the section during the frame, including nested sections.</summary>
    public long MarkerAllocatedBytes(ProfilerMarker marker) => marker.Id < MarkerAllocated.Length ? MarkerAllocated[marker.Id] : 0;

    public double CounterValue(ProfilerCounter counter) => counter.Id < CounterValues.Length ? CounterValues[counter.Id] : 0;

    internal void Reset(int markerCount, int counterCount)
    {
        if (MarkerTicks.Length < markerCount)
        {
            MarkerTicks = new long[markerCount];
            MarkerCalls = new int[markerCount];
            MarkerAllocated = new long[markerCount];
        }
        else
        {
            Array.Clear(MarkerTicks);
            Array.Clear(MarkerCalls);
            Array.Clear(MarkerAllocated);
        }

        if (CounterValues.Length < counterCount)
            CounterValues = new double[counterCount];
        else
            Array.Clear(CounterValues);
    }
}
