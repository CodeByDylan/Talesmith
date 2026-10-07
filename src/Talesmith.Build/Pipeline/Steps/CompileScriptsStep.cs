using Talesmith.Scripting.Compiler;

namespace Talesmith.Build.Pipeline.Steps;

/// <summary>Compiles the project's scripts in the profile's configuration against the engine and the shipped plugins.</summary>
internal sealed class CompileScriptsStep : IBuildStep
{
    public string Title => "Compiling scripts";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var options = ScriptOptions(context);
        using var compiler = new ScriptCompiler(options);
        if (compiler.FindSources().Count == 0)
        {
            context.Log(BuildLogLevel.Info, "The project has no scripts");
            return;
        }

        context.Progress(null, $"{options.Configuration} configuration");
        var result = await compiler.CompileAsync(cancellationToken).ConfigureAwait(false);
        foreach (var diagnostic in result.Diagnostics)
        {
            var level = diagnostic.Severity switch
            {
                ScriptDiagnosticSeverity.Error => BuildLogLevel.Error,
                ScriptDiagnosticSeverity.Warning => BuildLogLevel.Warning,
                _ => BuildLogLevel.Info
            };
            context.Write(new BuildLogEntry(DateTimeOffset.Now, level, $"{diagnostic.Id}: {diagnostic.Message}")
            {
                File = diagnostic.FilePath,
                Line = diagnostic.Line,
                Column = diagnostic.Column
            });
        }

        if (!result.Success)
        {
            context.Log(BuildLogLevel.Error, $"The scripts do not compile: {result.Errors.Count()} errors");
            return;
        }

        context.Scripts = result;
        context.Log(BuildLogLevel.Info, $"Compiled {result.SourceFiles} scripts in {result.Duration.TotalMilliseconds:N0} ms");
    }

    /// <summary>The compiler options for a build: the profile's configuration and the shipped plugins' assemblies as references.</summary>
    internal static ScriptCompilerOptions ScriptOptions(BuildContext context) => new()
    {
        ProjectDirectory = context.Request.ProjectFolder,
        Configuration = context.Profile.Scripts,
        AdditionalReferences =
        [
            .. context.Plugins
                .Where(p => p.Manifest is not null)
                .Select(p => Path.Combine(p.Directory, p.Manifest!.AssemblyFile))
                .Where(File.Exists)
        ]
    };
}
