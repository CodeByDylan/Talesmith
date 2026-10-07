using System.Reflection;

namespace Talesmith.Scripting.Compiler;

/// <summary>The outcome of compiling a project's scripts: the assembly and its symbols when it succeeded, and every diagnostic.</summary>
public sealed class ScriptCompilationResult
{
    internal ScriptCompilationResult(ScriptCompilerOptions options, int generation, bool success, IReadOnlyList<ScriptDiagnostic> diagnostics, byte[]? image,
        byte[]? symbols, TimeSpan duration, int sourceFiles, int reusedTrees, int parsedTrees)
    {
        AssemblyName = options.AssemblyName;
        Configuration = options.Configuration;
        Generation = generation;
        Success = success;
        Diagnostics = diagnostics;
        Image = image;
        Symbols = symbols;
        Duration = duration;
        SourceFiles = sourceFiles;
        ReusedTrees = reusedTrees;
        ParsedTrees = parsedTrees;
    }

    /// <summary>Whether the scripts compiled without errors, so <see cref="Image"/> is set.</summary>
    public bool Success { get; }

    /// <summary>Errors, warnings and infos ordered by file, line and column.</summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }

    public IEnumerable<ScriptDiagnostic> Errors => Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error);

    public IEnumerable<ScriptDiagnostic> Warnings => Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Warning);

    /// <summary>The compiled assembly, or null when compilation failed.</summary>
    public byte[]? Image { get; }

    /// <summary>The portable PDB with the scripts' sources embedded, so debuggers can show them.</summary>
    public byte[]? Symbols { get; }

    public string AssemblyName { get; }

    public ScriptConfiguration Configuration { get; }

    /// <summary>Counts compilations of a compiler, starting at 1.</summary>
    public int Generation { get; }

    public TimeSpan Duration { get; }

    public int SourceFiles { get; }

    /// <summary>Source files whose syntax trees were reused from the previous compilation because they did not change.</summary>
    public int ReusedTrees { get; }

    /// <summary>Source files that were parsed because they are new or changed.</summary>
    public int ParsedTrees { get; }

    /// <summary>Loads the compiled assembly into a new collectible load context.</summary>
    /// <param name="references">Assemblies the scripts use that the host did not load in its default context, such as plugins loaded in memory.</param>
    /// <exception cref="InvalidOperationException">Compilation failed.</exception>
    public ScriptAssembly Load(IEnumerable<Assembly>? references = null) =>
        ScriptAssembly.Load(Image ?? throw new InvalidOperationException("The scripts did not compile, so there is nothing to load."), Symbols, references);

    /// <summary>Writes the assembly and its symbols to a folder, such as a build's <c>assets/scripts/bin</c>; returns the assembly's path.</summary>
    /// <exception cref="InvalidOperationException">Compilation failed.</exception>
    public string WriteTo(string directory)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        var image = Image ?? throw new InvalidOperationException("The scripts did not compile, so there is nothing to write.");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, AssemblyName + ".dll");
        File.WriteAllBytes(path, image);
        if (Symbols is not null)
            File.WriteAllBytes(Path.ChangeExtension(path, ".pdb"), Symbols);
        return path;
    }

    public override string ToString() =>
        $"{(Success ? "Compiled" : "Failed to compile")} {SourceFiles} scripts in {Duration.TotalMilliseconds:0} ms with {Errors.Count()} errors and {Warnings.Count()} warnings";
}
