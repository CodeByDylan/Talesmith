using Microsoft.Extensions.Logging;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Build;

public enum BuildProfileKind
{
    /// <summary>For testing: debug symbols, verbose logging, developer tools and unoptimized scripts.</summary>
    Development,

    /// <summary>For playing: optimized and precompiled, with minimal logging.</summary>
    Release,

    /// <summary>For shipping: a release build packaged as an archive, ready to upload.</summary>
    Distribution
}

/// <summary>How a build compiles, publishes and packages a game.</summary>
/// <remarks>
/// Builds never trim the .NET libraries: plugins and scripts are loaded at run time and may use any framework or engine API, which
/// trimming would remove without warning.
/// </remarks>
public sealed record BuildProfile
{
    public required BuildProfileKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    /// <summary>How scripts are compiled.</summary>
    public ScriptConfiguration Scripts { get; init; } = ScriptConfiguration.Release;

    /// <summary>Precompiles the engine to native code so games start faster.</summary>
    public bool ReadyToRun { get; init; }

    /// <summary>Embeds debug symbols in the player and ships script symbols, so stack traces show file names and lines.</summary>
    public bool DebugSymbols { get; init; }

    /// <summary>The least severe messages games log.</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Warning;

    /// <summary>Enables the F3, F4, F9 and F12 developer tools in the game.</summary>
    public bool DeveloperTools { get; init; }

    /// <summary>Packs the output into a zip or tar.gz archive.</summary>
    public bool Archive { get; init; }

    public static BuildProfile Development { get; } = new()
    {
        Kind = BuildProfileKind.Development,
        DisplayName = "Development",
        Description = "Debug symbols, verbose logging, developer tools and scripts compiled for debugging.",
        Scripts = ScriptConfiguration.Debug,
        DebugSymbols = true,
        LogLevel = LogLevel.Debug,
        DeveloperTools = true
    };

    public static BuildProfile Release { get; } = new()
    {
        Kind = BuildProfileKind.Release,
        DisplayName = "Release",
        Description = "Optimized scripts and a precompiled engine with minimal logging.",
        ReadyToRun = true
    };

    public static BuildProfile Distribution { get; } = Release with
    {
        Kind = BuildProfileKind.Distribution,
        DisplayName = "Distribution",
        Description = "A release build packed into an archive, ready to share.",
        Archive = true
    };

    public static IReadOnlyList<BuildProfile> All { get; } = [Development, Release, Distribution];

    public static BuildProfile For(BuildProfileKind kind) => kind switch
    {
        BuildProfileKind.Development => Development,
        BuildProfileKind.Distribution => Distribution,
        _ => Release
    };

    /// <summary>Identifies published players that this profile can share; distribution builds reuse release players.</summary>
    internal string PlayerKey => $"{(ReadyToRun ? "r2r" : "jit")}-{(DebugSymbols ? "symbols" : "nosymbols")}";
}
