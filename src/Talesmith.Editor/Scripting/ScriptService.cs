using System.Globalization;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Talesmith.Editor.Console;
using Talesmith.Editor.Projects;
using Talesmith.Plugins;
using Talesmith.Scripting;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Editor.Scripting;

/// <summary>The default <see cref="IScriptService"/>, built on <see cref="ScriptCompilationService"/>.</summary>
/// <remarks>Starts compiling as soon as it is created. Compiled scripts reference the project's plugins, and the IDE project is rewritten when the
/// plugins change.</remarks>
public sealed class ScriptService : IScriptService, IDisposable
{
    private readonly IProjectService _project;
    private readonly IConsole _console;
    private readonly ILoggerFactory _loggers;
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;
    private readonly ScriptCompilationService _compilation;
    private readonly Dictionary<ScriptAssembly, int> _leases = new(ReferenceEqualityComparer.Instance);
    private readonly List<ScriptAssembly> _loaded = [];
    private HashSet<string> _reportedWarnings = new(StringComparer.Ordinal);
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _compilations;
    private bool _disposed;

    public ScriptService(IProjectService project, IConsole console)
    {
        _project = project;
        _console = console;
        _loggers = LoggerFactory.Create(logging => logging.AddProvider(new ConsoleLoggerProvider(console, ConsoleSource.Script, LogLevel.Warning)));
        Options = new ScriptCompilerOptions
        {
            ProjectDirectory = project.Project.Folder,
            Configuration = ScriptConfiguration.Debug,
            AdditionalReferences = PluginReferences(project.Plugins)
        };
        _compilation = new ScriptCompilationService(Options, _loggers.CreateLogger<ScriptCompilationService>());
        _compilation.CompilationStarted += (_, _) => Post(OnStarted);
        _compilation.CompilationFinished += (_, e) => Post(() => OnFinished(e));
        project.Plugins.Changed += OnPluginsChanged;
        IsCompiling = true;
        _ = StartAsync();
    }

    public ScriptCompilerOptions Options { get; }

    public ScriptCompilationResult? LastResult { get; private set; }

    public ScriptAssembly? Assembly { get; private set; }

    public bool IsCompiling { get; private set; }

    public bool HasErrors => LastResult is { Success: false };

    public Task WhenIdle => _idle.Task;

    public event EventHandler? StateChanged;

    public event EventHandler<ScriptAssembly>? AssemblyChanged;

    public Task<ScriptCompilationResult> CompileAsync() => _compilation.CompileAsync();

    public IDisposable Use(ScriptAssembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _leases[assembly] = _leases.GetValueOrDefault(assembly) + 1;
        return new Lease(this, assembly);
    }

    public string WriteProjectFile()
    {
        var options = Options with { AdditionalReferences = PluginReferences(_project.Plugins) };
        ScriptProjectFile.Write(options);
        return ScriptProjectFile.PathFor(options);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _project.Plugins.Changed -= OnPluginsChanged;
        _compilation.Dispose();
        _loggers.Dispose();
    }

    /// <summary>The runtime assemblies of the plugins that load, which scripts may use.</summary>
    public static IReadOnlyList<string> PluginReferences(PluginManager plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return
        [
            .. plugins.Scan.Plugins
                .Where(p => p.State is PluginState.Pending or PluginState.Loaded && p.Manifest is not null)
                .Select(p => Path.Combine(p.Directory, p.Manifest!.AssemblyFile))
                .Where(File.Exists)
        ];
    }

    private async Task StartAsync()
    {
        try
        {
            await Task.Run(WriteProjectFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _console.Warning($"The scripts' IDE project could not be written: {ex.Message}", ConsoleSource.Script);
        }

        try
        {
            await _compilation.StartAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            if (_disposed)
                return;
            _console.Error($"Scripts could not be compiled: {ex.Message}", ex, ConsoleSource.Script);
            Post(() =>
            {
                IsCompiling = false;
                _idle.TrySetResult();
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private void OnStarted()
    {
        if (_idle.Task.IsCompleted)
            _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _compilations++;
        IsCompiling = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFinished(ScriptCompilationFinishedEventArgs e)
    {
        if (_disposed)
            return;
        var result = e.Result;
        LastResult = result;
        _compilations = Math.Max(0, _compilations - 1);
        IsCompiling = _compilations > 0;
        Report(result, e.LoadError);
        if (e.Assembly is { } assembly)
        {
            Assembly = assembly;
            AssemblyChanged?.Invoke(this, assembly);
            ReleaseUnused();
        }

        if (!IsCompiling)
            _idle.TrySetResult();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Report(ScriptCompilationResult result, Exception? loadError)
    {
        var shown = result.Diagnostics;
        var warnings = shown.Where(d => !d.IsError).Select(d => d.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var diagnostic in shown)
        {
            if (!diagnostic.IsError && _reportedWarnings.Contains(diagnostic.ToString()))
                continue;
            var location = diagnostic.RelativePath is { } path ? $"{Path.GetFileName(path)}({diagnostic.Line},{diagnostic.Column}): " : "";
            var severity = diagnostic.Severity switch
            {
                ScriptDiagnosticSeverity.Error => ConsoleSeverity.Error,
                ScriptDiagnosticSeverity.Warning => ConsoleSeverity.Warning,
                _ => ConsoleSeverity.Info
            };
            _console.Write(new ConsoleEntry(DateTimeOffset.Now, severity, ConsoleSource.Script,
                $"{location}{diagnostic.Message}", diagnostic.Id)
            {
                Details = diagnostic.ToString() + (diagnostic.HelpLink is { } link ? $"{Environment.NewLine}{link}" : ""),
                Target = diagnostic.FilePath is { } file ? new FileTarget(file, diagnostic.Line, diagnostic.Column) : null
            });
        }

        _reportedWarnings = warnings;
        var errors = shown.Count(d => d.IsError);
        if (result.Success)
        {
            _console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Info, ConsoleSource.Script,
                string.Create(CultureInfo.CurrentCulture, $"Compiled {result.SourceFiles} scripts in {result.Duration.TotalMilliseconds:N0} ms")));
        }
        else
        {
            _console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Error, ConsoleSource.Script,
                $"The scripts have {errors} {(errors == 1 ? "error" : "errors")}; play mode starts once they compile"));
        }

        if (loadError is not null)
            _console.Error($"The compiled scripts could not be loaded: {loadError.Message}", loadError, ConsoleSource.Script);
    }

    private void Release(ScriptAssembly assembly)
    {
        if (!_leases.TryGetValue(assembly, out var count))
            return;
        if (count > 1)
            _leases[assembly] = count - 1;
        else
            _leases.Remove(assembly);
        ReleaseUnused();
    }

    private void ReleaseUnused()
    {
        if (Assembly is { } latest && !_loaded.Contains(latest))
            _loaded.Add(latest);
        foreach (var assembly in _loaded.ToArray())
        {
            if (ReferenceEquals(assembly, Assembly) || _leases.ContainsKey(assembly))
                continue;
            _loaded.Remove(assembly);
            assembly.Unload();
        }
    }

    private void OnPluginsChanged(object? sender, EventArgs e)
    {
        try
        {
            WriteProjectFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _console.Warning($"The scripts' IDE project could not be updated: {ex.Message}", ConsoleSource.Script);
        }
    }

    private void Post(Action action)
    {
        if (_disposed)
            return;
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Post(action);
    }

    private sealed class Lease(ScriptService owner, ScriptAssembly assembly) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.Post(() => owner.Release(assembly));
        }
    }
}
