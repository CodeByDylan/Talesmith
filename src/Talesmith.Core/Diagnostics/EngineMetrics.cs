using System.Diagnostics.Metrics;

namespace Talesmith.Diagnostics;

/// <summary>Publishes profiler data through <see cref="System.Diagnostics.Metrics"/> so standard tools can watch a running game.</summary>
/// <remarks>
/// Run <c>dotnet-counters monitor --name &lt;process&gt; --counters Talesmith</c> to see frame rate, frame times, allocations, every
/// profiler marker and every counter live, or collect them with any OpenTelemetry exporter. Values are averaged over the last
/// <see cref="WindowFrames"/> frames of each profiler.
/// </remarks>
public sealed class EngineMetrics : IDisposable
{
    public const string MeterName = "Talesmith";
    public const int WindowFrames = 60;

    private readonly Meter _meter;
    private readonly List<Profiler> _profilers;
    private readonly List<FrameSample> _samples = new(WindowFrames);
    private readonly Lock _lock = new();

    public EngineMetrics(IEnumerable<Profiler> profilers)
    {
        _profilers = profilers.ToList();
        _meter = new Meter(MeterName, EngineInfo.Version.ToString());
        _meter.CreateObservableGauge("talesmith.fps", () => Each(s => s.AverageFramesPerSecond), "{frame}/s", "Frames per second.");
        _meter.CreateObservableGauge("talesmith.frame.interval", () => Each(s => s.FrameInterval.Average), "ms", "Average time between frames.");
        _meter.CreateObservableGauge("talesmith.frame.interval.p99", () => Each(s => s.FrameInterval.P99), "ms", "99th percentile time between frames.");
        _meter.CreateObservableGauge("talesmith.frame.work", () => Each(s => s.FrameWork.Average), "ms", "Average time spent inside a frame.");
        _meter.CreateObservableGauge("talesmith.frame.allocated", () => Each(s => s.AllocatedBytes.Average), "By", "Managed bytes allocated per frame.");
        _meter.CreateObservableGauge("talesmith.marker.duration", MarkerMeasurements, "ms", "Average time per frame in each profiler marker.");
        _meter.CreateObservableGauge("talesmith.counter", CounterMeasurements, null, "Average value per frame of each profiler counter.");
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<double>> Each(Func<ProfilerStatistics, double> select)
    {
        foreach (var stats in Snapshot())
            yield return new Measurement<double>(select(stats), new KeyValuePair<string, object?>("profiler", stats.Profiler));
    }

    private IEnumerable<Measurement<double>> MarkerMeasurements()
    {
        foreach (var stats in Snapshot())
        {
            foreach (var marker in stats.Markers)
                yield return new Measurement<double>(marker.Milliseconds.Average, new("profiler", stats.Profiler), new("marker", marker.Name));
        }
    }

    private IEnumerable<Measurement<double>> CounterMeasurements()
    {
        foreach (var stats in Snapshot())
        {
            foreach (var counter in stats.Counters)
                yield return new Measurement<double>(counter.Value.Average, new("profiler", stats.Profiler), new("counter", counter.Name));
        }
    }

    // Collection happens on a metrics thread; frames are read under a lock, and a torn read of a frame being reused only skews one sample.
    private List<ProfilerStatistics> Snapshot()
    {
        lock (_lock)
        {
            var result = new List<ProfilerStatistics>(_profilers.Count);
            foreach (var profiler in _profilers)
            {
                profiler.GetRecent(WindowFrames, _samples);
                result.Add(ProfilerStatistics.Compute(profiler, _samples));
            }

            return result;
        }
    }
}
