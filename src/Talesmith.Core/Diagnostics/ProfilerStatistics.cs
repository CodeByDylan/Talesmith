namespace Talesmith.Diagnostics;

/// <summary>Average, extremes and percentiles of a series of values.</summary>
public readonly record struct Summary(double Average, double Minimum, double Maximum, double P50, double P95, double P99)
{
    public static Summary Of(Span<double> values)
    {
        if (values.IsEmpty)
            return default;

        values.Sort();
        var sum = 0.0;
        foreach (var value in values)
            sum += value;
        return new Summary(sum / values.Length, values[0], values[^1], Percentile(values, 0.50), Percentile(values, 0.95), Percentile(values, 0.99));
    }

    private static double Percentile(ReadOnlySpan<double> sorted, double fraction)
    {
        var position = fraction * (sorted.Length - 1);
        var lower = (int)position;
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}

/// <summary>Timing of one marked section over a range of frames.</summary>
/// <param name="Milliseconds">Time per frame spent in the section, including nested sections.</param>
/// <param name="AverageCalls">How often the section ran per frame on average.</param>
/// <param name="AverageAllocatedBytes">Managed bytes allocated inside the section per frame on average.</param>
public sealed record MarkerStatistics(string Name, string Category, string? Parent, Summary Milliseconds, double AverageCalls, double AverageAllocatedBytes);

/// <summary>Values of one counter over a range of frames.</summary>
public sealed record CounterStatistics(string Name, string Unit, CounterKind Kind, Summary Value, double Last);

/// <summary>A summary of a profiler's recent frames.</summary>
/// <param name="FrameInterval">Real time between frames in milliseconds; its inverse is the frame rate.</param>
/// <param name="FrameWork">Time spent inside frames in milliseconds.</param>
/// <param name="AllocatedBytes">Managed bytes allocated per frame on the measured thread.</param>
public sealed record ProfilerStatistics(
    string Profiler,
    int Frames,
    double AverageFramesPerSecond,
    Summary FrameInterval,
    Summary FrameWork,
    Summary AllocatedBytes,
    CollectionCounts Collections,
    IReadOnlyList<MarkerStatistics> Markers,
    IReadOnlyList<CounterStatistics> Counters)
{
    internal static ProfilerStatistics Compute(Profiler profiler, List<FrameSample> samples)
    {
        var count = samples.Count;
        var buffer = new double[count];

        Summary Collect(Func<FrameSample, double> select)
        {
            for (var i = 0; i < count; i++)
                buffer[i] = select(samples[i]);
            return Summary.Of(buffer.AsSpan(0, count));
        }

        var interval = Collect(s => s.IntervalMilliseconds);
        var collections = default(CollectionCounts);
        foreach (var sample in samples)
            collections += sample.Collections;

        var markers = new List<MarkerStatistics>();
        foreach (var marker in ProfilerMarker.All)
        {
            var calls = samples.Sum(s => s.MarkerCallCount(marker));
            if (calls == 0)
                continue;
            markers.Add(new MarkerStatistics(marker.Name, marker.Category, profiler.ParentOf(marker)?.Name,
                Collect(s => s.MarkerMilliseconds(marker)), calls / (double)Math.Max(1, count),
                samples.Sum(s => (double)s.MarkerAllocatedBytes(marker)) / Math.Max(1, count)));
        }

        var counters = new List<CounterStatistics>();
        foreach (var counter in ProfilerCounter.All)
        {
            if (samples.All(s => s.CounterValue(counter) == 0))
                continue;
            counters.Add(new CounterStatistics(counter.Name, counter.Unit, counter.Kind, Collect(s => s.CounterValue(counter)),
                count == 0 ? 0 : samples[^1].CounterValue(counter)));
        }

        return new ProfilerStatistics(
            profiler.Name,
            count,
            interval.Average > 0 ? 1000.0 / interval.Average : 0,
            interval,
            Collect(s => s.WorkMilliseconds),
            Collect(s => s.AllocatedBytes),
            collections,
            markers,
            counters);
    }
}
