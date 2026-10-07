using System.Text.Json;
using System.Text.Json.Serialization;
using Talesmith.Assets;

namespace Talesmith.Build;

/// <summary>A scene to ship, in the order the build settings list it.</summary>
public sealed record BuildScene(string Path, bool Enabled = true);

/// <summary>A project's export settings, saved in <c>&lt;project&gt;/build.json</c> so they can be versioned with the game.</summary>
public sealed record BuildSettings
{
    public const string FileName = "build.json";

    /// <summary>The prefix of <see cref="AlwaysInclude"/> entries that name an asset label instead of a path.</summary>
    public const string LabelPrefix = "label:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>The runtime identifier of the target, such as <c>linux-x64</c>; null builds for the current machine.</summary>
    public string? Target { get; init; }

    public BuildProfileKind Profile { get; init; } = BuildProfileKind.Release;

    /// <summary>Where builds go: absolute, or relative to the project folder.</summary>
    public string OutputFolder { get; init; } = "builds";

    /// <summary>Scenes to ship besides the start scene, such as levels loaded by name.</summary>
    public IReadOnlyList<BuildScene> Scenes { get; init; } = [];

    /// <summary>Asset files and folders, or <c>label:name</c> for assets with a label, shipped even when nothing refers to them.</summary>
    public IReadOnlyList<string> AlwaysInclude { get; init; } = [];

    /// <summary>The game's version, written into the executable's metadata and archive names.</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>The executable's name without extension; null uses the game's title.</summary>
    public string? ExecutableName { get; init; }

    /// <summary>A PNG asset used as the application icon where the platform supports it.</summary>
    public string? Icon { get; init; }

    /// <summary>Compresses assets in the content pack, except formats that are compressed already.</summary>
    public bool Compress { get; init; } = true;

    public BuildTarget ResolveTarget() => BuildTargets.Find(Target) ?? BuildTargets.Current;

    public string ResolveOutputFolder(string projectFolder) => Path.GetFullPath(Path.Combine(projectFolder, OutputFolder));

    /// <summary>Reads the settings of a project, or defaults when it has none.</summary>
    /// <exception cref="InvalidDataException">The file exists but is not valid.</exception>
    public static BuildSettings Load(string projectFolder)
    {
        var path = Path.Combine(projectFolder, FileName);
        if (!File.Exists(path))
            return new BuildSettings();
        try
        {
            return JsonSerializer.Deserialize<BuildSettings>(File.ReadAllText(path), JsonOptions) ?? new BuildSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not valid: {ex.Message}", ex);
        }
    }

    public void Save(string projectFolder)
    {
        AtomicFile.WriteAllText(Path.Combine(projectFolder, FileName), JsonSerializer.Serialize(this, JsonOptions) + "\n");
    }
}
