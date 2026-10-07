using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Diagnostics;

/// <summary>A snapshot of one or more profilers, with the environment it was taken in, that can be saved as JSON or CSV.</summary>
public sealed class ProfilerReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private ProfilerReport(DateTimeOffset createdAt, IReadOnlyDictionary<string, string> environment, IReadOnlyList<ProfilerStatistics> profilers, IReadOnlyList<FrameTable> frames)
    {
        CreatedAt = createdAt;
        Environment = environment;
        Profilers = profilers;
        Frames = frames;
    }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Machine, runtime and engine details, plus any metadata passed to <see cref="Capture"/> such as the render backend.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    public IReadOnlyList<ProfilerStatistics> Profilers { get; }

    /// <summary>Per-frame values of each profiler, oldest first.</summary>
    [JsonIgnore]
    public IReadOnlyList<FrameTable> Frames { get; }

    /// <summary>Captures up to <paramref name="frames"/> recent frames of each profiler.</summary>
    public static ProfilerReport Capture(IEnumerable<Profiler> profilers, int frames = 600, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var environment = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["engine"] = EngineInfo.Version.ToString(),
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["processors"] = System.Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["gcServer"] = System.Runtime.GCSettings.IsServerGC.ToString(),
            ["workingSetBytes"] = System.Environment.WorkingSet.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var (key, value) in metadata ?? new Dictionary<string, string>())
            environment[key] = value;

        var statistics = new List<ProfilerStatistics>();
        var tables = new List<FrameTable>();
        var samples = new List<FrameSample>();
        foreach (var profiler in profilers)
        {
            profiler.GetRecent(frames, samples);
            statistics.Add(ProfilerStatistics.Compute(profiler, samples));
            tables.Add(FrameTable.From(profiler.Name, samples));
        }

        return new ProfilerReport(DateTimeOffset.Now, environment, statistics, tables);
    }

    public void WriteJson(Stream stream) => JsonSerializer.Serialize(stream, this, JsonOptions);

    /// <summary>Writes one row per frame and profiler, with a column per marker (milliseconds) and counter.</summary>
    public void WriteCsv(TextWriter writer)
    {
        var markers = ProfilerMarker.All;
        var counters = ProfilerCounter.All;
        var header = new StringBuilder("profiler,frame,intervalMs,workMs,allocatedBytes,gen0,gen1,gen2");
        foreach (var marker in markers)
            header.Append(',').Append(Escape(marker.Name + " (ms)"));
        foreach (var marker in markers)
            header.Append(',').Append(Escape(marker.Name + " (B)"));
        foreach (var counter in counters)
            header.Append(',').Append(Escape(counter.Name));
        writer.WriteLine(header);

        var invariant = CultureInfo.InvariantCulture;
        foreach (var table in Frames)
        {
            foreach (var row in table.Rows)
            {
                var line = new StringBuilder();
                line.Append(Escape(table.Profiler)).Append(',').Append(row.Frame.ToString(invariant))
                    .Append(',').Append(row.IntervalMilliseconds.ToString("0.###", invariant))
                    .Append(',').Append(row.WorkMilliseconds.ToString("0.###", invariant))
                    .Append(',').Append(row.AllocatedBytes.ToString(invariant))
                    .Append(',').Append(row.Collections.Gen0.ToString(invariant))
                    .Append(',').Append(row.Collections.Gen1.ToString(invariant))
                    .Append(',').Append(row.Collections.Gen2.ToString(invariant));
                for (var i = 0; i < markers.Count; i++)
                    line.Append(',').Append((i < row.MarkerMilliseconds.Length ? row.MarkerMilliseconds[i] : 0).ToString("0.####", invariant));
                for (var i = 0; i < markers.Count; i++)
                    line.Append(',').Append((i < row.MarkerAllocatedBytes.Length ? row.MarkerAllocatedBytes[i] : 0).ToString(invariant));
                for (var i = 0; i < counters.Count; i++)
                    line.Append(',').Append((i < row.Counters.Length ? row.Counters[i] : 0).ToString(invariant));
                writer.WriteLine(line);
            }
        }
    }

    /// <summary>Saves the report as JSON, or CSV when the path ends in ".csv", and returns the full path.</summary>
    public string Save(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var stream = File.Create(fullPath);
        if (fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            WriteCsv(writer);
        }
        else
        {
            WriteJson(stream);
        }

        return fullPath;
    }

    /// <summary>Formats the statistics as a plain-text table for consoles and logs.</summary>
    public string ToText()
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        foreach (var stats in Profilers)
        {
            text.AppendLine(invariant, $"{stats.Profiler}: {stats.Frames} frames, {stats.AverageFramesPerSecond:0.0} fps");
            text.AppendLine(invariant, $"  frame interval  avg {stats.FrameInterval.Average,8:0.000} ms  p95 {stats.FrameInterval.P95,8:0.000}  p99 {stats.FrameInterval.P99,8:0.000}  max {stats.FrameInterval.Maximum,8:0.000}");
            text.AppendLine(invariant, $"  frame work      avg {stats.FrameWork.Average,8:0.000} ms  p95 {stats.FrameWork.P95,8:0.000}  p99 {stats.FrameWork.P99,8:0.000}  max {stats.FrameWork.Maximum,8:0.000}");
            text.AppendLine(invariant, $"  allocated       avg {stats.AllocatedBytes.Average,8:0} B/frame  GC {stats.Collections.Gen0}/{stats.Collections.Gen1}/{stats.Collections.Gen2}");
            foreach (var marker in stats.Markers)
                text.AppendLine(invariant, $"  {(marker.Parent is null ? string.Empty : "  ")}{marker.Name,-34} avg {marker.Milliseconds.Average,8:0.000} ms  p95 {marker.Milliseconds.P95,8:0.000}  max {marker.Milliseconds.Maximum,8:0.000}  ×{marker.AverageCalls:0.#}  {marker.AverageAllocatedBytes,6:0} B");
            foreach (var counter in stats.Counters)
                text.AppendLine(invariant, $"  {counter.Name,-36} avg {counter.Value.Average,10:0.##}  max {counter.Value.Maximum,10:0.##} {counter.Unit}");
        }

        return text.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}

/// <summary>Per-frame values copied from a profiler's history.</summary>
public sealed record FrameTable(string Profiler, IReadOnlyList<FrameRow> Rows)
{
    internal static FrameTable From(string profiler, List<FrameSample> samples)
    {
        var markers = ProfilerMarker.All;
        var counters = ProfilerCounter.All;
        var rows = new List<FrameRow>(samples.Count);
        foreach (var sample in samples)
        {
            var markerMilliseconds = new double[markers.Count];
            var markerAllocated = new long[markers.Count];
            for (var i = 0; i < markers.Count; i++)
            {
                markerMilliseconds[i] = sample.MarkerMilliseconds(markers[i]);
                markerAllocated[i] = sample.MarkerAllocatedBytes(markers[i]);
            }
            var counterValues = new double[counters.Count];
            for (var i = 0; i < counters.Count; i++)
                counterValues[i] = sample.CounterValue(counters[i]);
            rows.Add(new FrameRow(sample.Frame, sample.IntervalMilliseconds, sample.WorkMilliseconds, sample.AllocatedBytes, sample.Collections, markerMilliseconds, markerAllocated, counterValues));
        }

        return new FrameTable(profiler, rows);
    }
}

/// <summary>One frame's values; marker and counter arrays follow the order of <see cref="ProfilerMarker.All"/> and <see cref="ProfilerCounter.All"/>.</summary>
public sealed record FrameRow(long Frame, double IntervalMilliseconds, double WorkMilliseconds, long AllocatedBytes, CollectionCounts Collections,
    double[] MarkerMilliseconds, long[] MarkerAllocatedBytes, double[] Counters);
