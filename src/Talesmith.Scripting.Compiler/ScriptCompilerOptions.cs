namespace Talesmith.Scripting.Compiler;

/// <summary>Whether scripts are compiled for debugging or for speed.</summary>
public enum ScriptConfiguration
{
    /// <summary>Unoptimized, with <c>DEBUG</c> defined, so debuggers step through scripts line by line.</summary>
    Debug,

    /// <summary>Optimized, for shipped games.</summary>
    Release
}

/// <summary>What a <see cref="ScriptCompiler"/> compiles and how.</summary>
public sealed record ScriptCompilerOptions
{
    /// <summary>The scripts folder used unless configured otherwise, relative to the project folder.</summary>
    public const string DefaultSourceDirectory = "assets/scripts";

    /// <summary>The project folder, which contains <c>assets/</c>.</summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>The folder whose <c>.cs</c> files are compiled, including subfolders, relative to <see cref="ProjectDirectory"/>.</summary>
    /// <remarks>Folders named <c>bin</c> or <c>obj</c> and folders starting with a dot are skipped.</remarks>
    public string SourceDirectory { get; init; } = DefaultSourceDirectory;

    public string AssemblyName { get; init; } = ScriptAssembly.DefaultName;

    public ScriptConfiguration Configuration { get; init; } = ScriptConfiguration.Debug;

    /// <summary>The assemblies scripts compile against; <see cref="ScriptReferences.Default"/> gives the framework and the engine's runtime assemblies.</summary>
    public IReadOnlyList<string> References { get; init; } = ScriptReferences.Default();

    /// <summary>More assemblies to compile against, such as the game's plugins.</summary>
    public IReadOnlyList<string> AdditionalReferences { get; init; } = [];

    /// <summary>Extra preprocessor symbols; <c>TALESMITH</c>, and <c>DEBUG</c> for <see cref="ScriptConfiguration.Debug"/>, are always defined.</summary>
    public IReadOnlyList<string> Defines { get; init; } = [];

    /// <summary>Whether to run the game-loop analyzers, which find blocking calls, allocations and string building in update methods.</summary>
    public bool RunAnalyzers { get; init; } = true;

    /// <summary>The full path of the scripts folder.</summary>
    public string SourceRoot => Path.GetFullPath(Path.Combine(ProjectDirectory, SourceDirectory));
}
