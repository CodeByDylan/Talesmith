using Talesmith.Assets.Database;
using Talesmith.Build.Player;

namespace Talesmith.Build;

/// <summary>What to build: a project, its settings, the target and profile, and where to put the result.</summary>
public sealed record BuildRequest
{
    /// <summary>The project folder, with <c>assets/config/game.json</c>.</summary>
    public required string ProjectFolder { get; init; }

    public BuildSettings Settings { get; init; } = new();

    public BuildTarget Target { get; init; } = BuildTargets.Current;

    public BuildProfile Profile { get; init; } = BuildProfile.Release;

    /// <summary>The folder the build goes into; null uses the settings' output folder.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>The project's scanned asset database, such as the editor's; null scans the project with the engine's importers.</summary>
    public AssetDatabase? Assets { get; init; }

    /// <summary>Provides the published player; null publishes it from the engine's sources into the user's cache.</summary>
    public IPlayerRuntimeProvider? Player { get; init; }

    /// <summary>Stops after writing the game folder, without a player or packaging, as tests and previews do.</summary>
    public bool ContentOnly { get; init; }

    public string AssetRoot => Path.Combine(ProjectFolder, "assets");

    /// <summary>Creates a request from a project's saved build settings.</summary>
    /// <exception cref="InvalidDataException">The build settings are not valid.</exception>
    public static BuildRequest FromProject(string projectFolder)
    {
        var settings = BuildSettings.Load(projectFolder);
        return new BuildRequest
        {
            ProjectFolder = Path.GetFullPath(projectFolder),
            Settings = settings,
            Target = settings.ResolveTarget(),
            Profile = BuildProfile.For(settings.Profile)
        };
    }
}

public enum BuildLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>A message from a build, optionally pointing at a file and line, such as a script error.</summary>
public sealed record BuildLogEntry(DateTimeOffset Time, BuildLogLevel Level, string Message)
{
    /// <summary>The file the message is about: an absolute path or one relative to the asset folder.</summary>
    public string? File { get; init; }

    public int Line { get; init; }

    public int Column { get; init; }

    /// <summary>The step that wrote the message.</summary>
    public string? Step { get; init; }

    public override string ToString() => File is null ? Message : Line > 0 ? $"{File}({Line},{Column}): {Message}" : $"{File}: {Message}";
}

/// <summary>Where a build is.</summary>
/// <param name="Step">The running step's index, from 0.</param>
/// <param name="StepCount">The number of steps.</param>
/// <param name="Title">The running step's title.</param>
/// <param name="Fraction">The step's own progress from 0 to 1, or null when it is unknown.</param>
/// <param name="Detail">What the step is doing, such as the file being packed.</param>
public sealed record BuildProgress(int Step, int StepCount, string Title, double? Fraction = null, string? Detail = null)
{
    /// <summary>The whole build's progress from 0 to 1.</summary>
    public double Overall => StepCount == 0 ? 1 : Math.Clamp((Step + (Fraction ?? 0)) / StepCount, 0, 1);
}
