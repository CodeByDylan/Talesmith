using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Build;

/// <summary>The size of one kind of content in a build.</summary>
/// <param name="Size">Bytes as shipped, after compression.</param>
/// <param name="OriginalSize">Bytes before compression.</param>
public sealed record BuildSizeCategory(string Name, int Files, long Size, long OriginalSize);

/// <summary>One shipped asset, for the list of the largest.</summary>
public sealed record BuildAssetSize(string Path, string Kind, long Size, long StoredSize, string Reason);

/// <summary>How long a step took.</summary>
public sealed record BuildStepTiming(string Title, TimeSpan Duration);

/// <summary>The outcome of a build: where it went, what it contains, how long it took and what went wrong.</summary>
public sealed record BuildReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public const string FileSuffix = ".report.json";

    /// <summary>The size category of the player executable and its native libraries.</summary>
    public const string PlayerCategory = "Engine and player";

    public bool Success { get; init; }

    public bool Cancelled { get; init; }

    public required string Game { get; init; }

    public required string Target { get; init; }

    public required BuildProfileKind Profile { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>The folder with the game, or null when the build failed.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>The game's executable, or null for content-only and failed builds.</summary>
    public string? Executable { get; init; }

    /// <summary>The archive distribution builds write, or null.</summary>
    public string? Archive { get; init; }

    /// <summary>Where this report was saved.</summary>
    public string? ReportFile { get; init; }

    public long TotalSize => Categories.Sum(c => c.Size);

    public int AssetCount { get; init; }

    public IReadOnlyList<BuildSizeCategory> Categories { get; init; } = [];

    public IReadOnlyList<BuildAssetSize> LargestAssets { get; init; } = [];

    public IReadOnlyList<BuildStepTiming> Steps { get; init; } = [];

    public IReadOnlyList<BuildLogEntry> Warnings { get; init; } = [];

    public IReadOnlyList<BuildLogEntry> Errors { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <exception cref="JsonException">The text is not a build report.</exception>
    public static BuildReport? FromJson(string json) => JsonSerializer.Deserialize<BuildReport>(json, JsonOptions);
}
