using System.Runtime.InteropServices;

namespace Talesmith.Build;

public enum BuildPlatform
{
    Windows,
    Linux
}

/// <summary>A platform a game can be exported for, identified by its .NET runtime identifier.</summary>
public sealed record BuildTarget(string RuntimeIdentifier, BuildPlatform Platform, Architecture Architecture, string DisplayName)
{
    /// <summary>The executable's file name for a base name, such as <c>Hex Quest.exe</c> on Windows.</summary>
    public string ExecutableName(string baseName) => Platform == BuildPlatform.Windows ? baseName + ".exe" : baseName;

    /// <summary>The name the published player has before it is renamed.</summary>
    public string PlayerExecutableName => ExecutableName(BuildTargets.PlayerName);

    public override string ToString() => DisplayName;
}

/// <summary>The supported targets.</summary>
public static class BuildTargets
{
    /// <summary>The player's executable name, from the Talesmith.Player project.</summary>
    public const string PlayerName = "talesmith-player";

    public static BuildTarget WindowsX64 { get; } = new("win-x64", BuildPlatform.Windows, Architecture.X64, "Windows x64");

    public static BuildTarget LinuxX64 { get; } = new("linux-x64", BuildPlatform.Linux, Architecture.X64, "Linux x64");

    public static IReadOnlyList<BuildTarget> All { get; } = [WindowsX64, LinuxX64];

    /// <summary>The target matching the machine the editor runs on, or Linux x64 when it is not supported.</summary>
    public static BuildTarget Current => OperatingSystem.IsWindows() ? WindowsX64 : LinuxX64;

    /// <summary>The target with a runtime identifier, or null.</summary>
    public static BuildTarget? Find(string? runtimeIdentifier) =>
        All.FirstOrDefault(t => string.Equals(t.RuntimeIdentifier, runtimeIdentifier, StringComparison.OrdinalIgnoreCase));
}
