using Talesmith.Build;

namespace Talesmith.Editor.Build;

/// <summary>Builds the open project in the background with its saved build settings, keeping the progress, log and report of the latest build.</summary>
public interface IBuildService
{
    /// <summary>The project's build settings from <c>build.json</c>.</summary>
    BuildSettings Settings { get; }

    bool IsBuilding { get; }

    /// <summary>The running build's progress, or the final progress of the latest build.</summary>
    BuildProgress? Progress { get; }

    /// <summary>The messages of the running or latest build.</summary>
    IReadOnlyList<BuildLogEntry> Log { get; }

    /// <summary>The report of the latest finished build, or null.</summary>
    BuildReport? LastReport { get; }

    /// <summary>Raised on the UI thread when a build starts, progresses or finishes, or the settings change.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised on the UI thread for each message of the running build.</summary>
    event EventHandler<BuildLogEntry>? LogAdded;

    /// <summary>Saves the build settings to the project.</summary>
    /// <exception cref="IOException">The settings could not be written.</exception>
    void SaveSettings(BuildSettings settings);

    /// <summary>Builds with the saved settings, asking to save the open scene first; returns null when the user cancelled before it started.</summary>
    /// <param name="run">Starts the game once the build succeeded, when it targets this computer.</param>
    Task<BuildReport?> BuildAsync(bool run = false);

    void Cancel();

    /// <summary>Whether a build's game can start on this computer.</summary>
    bool CanRun(BuildReport report);

    /// <summary>Starts a built game.</summary>
    bool Run(BuildReport report);
}
