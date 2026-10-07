using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Talesmith.Scripting.Compiler.Analyzers;

namespace Talesmith.Scripting.Compiler;

/// <summary>Compiles a project's scripts with Roslyn, incrementally: unchanged files keep their syntax trees and changed files are reparsed incrementally.</summary>
/// <remarks>
/// Every compilation reads the scripts folder again, so nothing needs to tell the compiler which files changed. The assembly carries a
/// portable PDB with the sources embedded, so exceptions show script lines and debuggers can step through scripts. Scripts get the
/// <see cref="GlobalUsings"/> without writing them. Compilations run one at a time on the calling task's thread; call
/// <see cref="CompileAsync"/> from the thread pool to keep a UI responsive.
/// </remarks>
public sealed class ScriptCompiler : IDisposable
{
    /// <summary>The namespaces every script can use without a <c>using</c> directive.</summary>
    public static IReadOnlyList<string> GlobalUsings { get; } =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Numerics",
        "System.Threading.Tasks",
        "Talesmith.Assets",
        "Talesmith.Assets.Textures",
        "Talesmith.Audio",
        "Talesmith.Authoring",
        "Talesmith.Ecs",
        "Talesmith.Input",
        "Talesmith.Mathematics",
        "Talesmith.Physics",
        "Talesmith.Runtime.Components",
        "Talesmith.Scripting",
        "Talesmith.Systems"
    ];

    private const string GlobalUsingsPath = "<global usings>";

    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers = [new GameLoopAnalyzer()];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, SourceFile> _files = new(StringComparer.Ordinal);
    private readonly CSharpParseOptions _parseOptions;
    private CSharpCompilation? _compilation;
    private int _generation;

    public ScriptCompiler(ScriptCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        var defines = new List<string> { "TALESMITH", "TRACE" };
        if (options.Configuration == ScriptConfiguration.Debug)
            defines.Add("DEBUG");
        defines.AddRange(options.Defines);
        _parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, preprocessorSymbols: defines);
    }

    public ScriptCompilerOptions Options { get; }

    /// <summary>Loads Roslyn and the references and compiles a small script, so the first real compilation is fast; run it off the UI thread.</summary>
    public Task WarmUpAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        const string source = "public sealed class WarmUp : Script { protected override void Update() => Position += Vector2.One; }";
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), _parseOptions, "WarmUp.cs", cancellationToken);
        var compilation = CreateCompilation([tree, GlobalUsingsTree()]);
        using var stream = new MemoryStream();
        compilation.Emit(stream, cancellationToken: cancellationToken);
    }, cancellationToken);

    /// <summary>Compiles every script of the project; files that did not change since the last compilation are not parsed again.</summary>
    public async Task<ScriptCompilationResult> CompileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var (compilation, sourceFiles, reused, parsed) = Update(cancellationToken);
            var generation = ++_generation;
            var (success, diagnostics, image, symbols) = await EmitAsync(compilation, cancellationToken).ConfigureAwait(false);
            return new ScriptCompilationResult(Options, generation, success, diagnostics, image, symbols, stopwatch.Elapsed, sourceFiles, reused, parsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets every cached syntax tree and the compilation, so the next compilation starts from scratch.</summary>
    public void Reset()
    {
        _gate.Wait();
        try
        {
            _files.Clear();
            _compilation = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>The script files that would be compiled, as full paths in a stable order.</summary>
    public IReadOnlyList<string> FindSources()
    {
        var root = Options.SourceRoot;
        if (!Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Whether a file under the scripts folder is skipped because it is in a <c>bin</c>, <c>obj</c> or hidden folder.</summary>
    public static bool IsExcluded(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (segment is ".." or "bin" or "obj" || segment.StartsWith('.'))
                return true;
        }

        return false;
    }

    private (CSharpCompilation Compilation, int SourceFiles, int Reused, int Parsed) Update(CancellationToken cancellationToken)
    {
        var paths = FindSources();
        var reused = 0;
        var parsed = 0;
        var trees = new List<SyntaxTree>(paths.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen.Add(path);
            var info = new FileInfo(path);
            if (_files.TryGetValue(path, out var cached) && cached.LastWrite == info.LastWriteTimeUtc && cached.Length == info.Length)
            {
                trees.Add(cached.Tree);
                reused++;
                continue;
            }

            SourceText text;
            try
            {
                text = Read(path);
            }
            catch (IOException) when (cached is not null)
            {
                trees.Add(cached.Tree);
                reused++;
                continue;
            }

            SyntaxTree tree;
            if (cached is not null && cached.Tree.GetText(cancellationToken).GetChecksum().SequenceEqual(text.GetChecksum()))
            {
                tree = cached.Tree;
                reused++;
            }
            else
            {
                tree = cached is not null
                    ? cached.Tree.WithChangedText(text)
                    : CSharpSyntaxTree.ParseText(text, _parseOptions, path, cancellationToken);
                parsed++;
            }

            _files[path] = new SourceFile(tree, info.LastWriteTimeUtc, info.Length);
            trees.Add(tree);
        }

        foreach (var removed in _files.Keys.Where(path => !seen.Contains(path)).ToList())
            _files.Remove(removed);

        _compilation = _compilation is null ? CreateCompilation([GlobalUsingsTree(), .. trees]) : Apply(_compilation, trees);
        return (_compilation, paths.Count, reused, parsed);
    }

    /// <summary>Changes only the trees that differ, so Roslyn keeps what it computed for the others.</summary>
    private static CSharpCompilation Apply(CSharpCompilation compilation, List<SyntaxTree> trees)
    {
        var current = compilation.SyntaxTrees.Where(t => t.FilePath != GlobalUsingsPath).ToDictionary(t => t.FilePath, StringComparer.Ordinal);
        var wanted = trees.ToDictionary(t => t.FilePath, StringComparer.Ordinal);
        var removed = current.Values.Where(t => !wanted.ContainsKey(t.FilePath)).ToArray();
        if (removed.Length > 0)
            compilation = compilation.RemoveSyntaxTrees(removed);
        foreach (var tree in trees)
        {
            if (!current.TryGetValue(tree.FilePath, out var existing))
                compilation = compilation.AddSyntaxTrees(tree);
            else if (!ReferenceEquals(existing, tree))
                compilation = compilation.ReplaceSyntaxTree(existing, tree);
        }

        return compilation;
    }

    private CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees)
    {
        var references = Options.References.Concat(Options.AdditionalReferences)
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var release = Options.Configuration == ScriptConfiguration.Release;
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: release ? OptimizationLevel.Release : OptimizationLevel.Debug,
            nullableContextOptions: NullableContextOptions.Enable,
            concurrentBuild: true,
            deterministic: true);
        return CSharpCompilation.Create(Options.AssemblyName, trees, references, options);
    }

    private SyntaxTree GlobalUsingsTree()
    {
        var source = new StringBuilder();
        foreach (var name in GlobalUsings)
            source.Append("global using ").Append(name).AppendLine(";");
        return CSharpSyntaxTree.ParseText(SourceText.From(source.ToString(), Encoding.UTF8), _parseOptions, GlobalUsingsPath);
    }

    private async Task<(bool Success, IReadOnlyList<ScriptDiagnostic> Diagnostics, byte[]? Image, byte[]? Symbols)> EmitAsync(CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        using var image = new MemoryStream();
        using var symbols = new MemoryStream();
        var embedded = compilation.SyntaxTrees
            .Where(t => t.FilePath != GlobalUsingsPath)
            .Select(t => EmbeddedText.FromSource(t.FilePath, t.GetText(cancellationToken)))
            .ToArray();
        var emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: Options.AssemblyName + ".pdb");
        var emit = compilation.Emit(image, symbols, embeddedTexts: embedded, options: emitOptions, cancellationToken: cancellationToken);

        var diagnostics = new List<Diagnostic>(emit.Diagnostics);
        if (Options.RunAnalyzers && !emit.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var analyzed = compilation.WithAnalyzers(Analyzers, new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, concurrentAnalysis: true,
                logAnalyzerExecutionTime: false));
            diagnostics.AddRange(await analyzed.GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false));
        }

        var converted = diagnostics
            .Where(d => d.Severity != DiagnosticSeverity.Hidden)
            .Select(Convert)
            .OrderBy(d => d.RelativePath ?? "", StringComparer.Ordinal)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();
        return emit.Success ? (true, converted, image.ToArray(), symbols.ToArray()) : (false, converted, null, null);
    }

    private ScriptDiagnostic Convert(Diagnostic diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => ScriptDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => ScriptDiagnosticSeverity.Warning,
            _ => ScriptDiagnosticSeverity.Info
        };
        var help = diagnostic.Descriptor.HelpLinkUri switch
        {
            null or "" => null,
            var link when link.Contains("roslyn.query", StringComparison.Ordinal) => $"https://learn.microsoft.com/search/?terms={diagnostic.Id}",
            var link => link
        };
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree?.FilePath is not { Length: > 0 } file || file == GlobalUsingsPath)
            return new ScriptDiagnostic(diagnostic.Id, severity, message, null, null, 0, 0, 0, 0, help);

        var span = diagnostic.Location.GetLineSpan();
        var relative = Path.GetRelativePath(Options.ProjectDirectory, file).Replace('\\', '/');
        return new ScriptDiagnostic(diagnostic.Id, severity, message, file, relative, span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1,
            span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1, help);
    }

    private static SourceText Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return SourceText.From(bytes, bytes.Length, Encoding.UTF8, SourceHashAlgorithm.Sha256, throwIfBinaryDetected: false, canBeEmbedded: true);
    }

    private sealed record SourceFile(SyntaxTree Tree, DateTime LastWrite, long Length);
}
