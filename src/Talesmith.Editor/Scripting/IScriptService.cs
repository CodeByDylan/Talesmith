using Talesmith.Scripting;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Editor.Scripting;

/// <summary>Keeps the project's scripts compiled while it is open and reports the results.</summary>
/// <remarks>
/// Compilation starts when the project opens and again shortly after script files change. Diagnostics go to the console with their file
/// and line. Each successful compilation is loaded, and <see cref="EditSessionScripts"/> brings it into the edit game, so the inspector sees new
/// script types and fields.
/// </remarks>
public interface IScriptService
{
    ScriptCompilerOptions Options { get; }

    /// <summary>The latest compilation, or null before the first finished.</summary>
    ScriptCompilationResult? LastResult { get; }

    /// <summary>The scripts of the latest successful compilation, or null.</summary>
    ScriptAssembly? Assembly { get; }

    bool IsCompiling { get; }

    /// <summary>Whether the latest compilation failed.</summary>
    bool HasErrors { get; }

    /// <summary>Completes when no compilation is running or waiting.</summary>
    Task WhenIdle { get; }

    /// <summary>Raised on the UI thread when compiling starts or finishes.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised on the UI thread with the newly loaded scripts after a successful compilation.</summary>
    event EventHandler<ScriptAssembly>? AssemblyChanged;

    /// <summary>Compiles now, after a compilation in progress.</summary>
    Task<ScriptCompilationResult> CompileAsync();

    /// <summary>Keeps <paramref name="assembly"/> loaded until the lease is disposed, such as while a play session runs it.</summary>
    IDisposable Use(ScriptAssembly assembly);

    /// <summary>Writes the IDE project for the scripts when it changed and returns its path.</summary>
    string WriteProjectFile();
}
