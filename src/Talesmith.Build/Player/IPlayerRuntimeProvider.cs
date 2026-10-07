namespace Talesmith.Build.Player;

/// <summary>Where a build step reports what it is doing.</summary>
public interface IBuildOutput
{
    void Log(BuildLogLevel level, string message);

    /// <summary>Reports the step's progress from 0 to 1, or null when unknown, and what it is working on.</summary>
    void Progress(double? fraction, string? detail = null);
}

/// <summary>The player a build needs: the target and profile, and the game's name, version and icon for executables that carry them.</summary>
public sealed record PlayerRequest(BuildTarget Target, BuildProfile Profile)
{
    public string Title { get; init; } = "";

    public string Version { get; init; } = "1.0.0";

    /// <summary>The application icon as PNG bytes, or null.</summary>
    public byte[]? Icon { get; init; }
}

/// <summary>Provides the published player, the executable that runs exported games.</summary>
public interface IPlayerRuntimeProvider
{
    /// <summary>Returns a folder containing the player's executable and native libraries; the build copies from it and never changes it.</summary>
    /// <exception cref="BuildException">The player cannot be provided, such as when the .NET SDK is missing.</exception>
    Task<string> GetAsync(PlayerRequest request, IBuildOutput output, CancellationToken cancellationToken);
}

/// <summary>A problem that stops a build, with a message for the user.</summary>
public sealed class BuildException(string message, Exception? innerException = null) : Exception(message, innerException);
