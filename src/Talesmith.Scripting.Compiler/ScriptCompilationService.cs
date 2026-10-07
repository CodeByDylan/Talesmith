using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Talesmith.Scripting.Compiler;

/// <summary>Raised when a compilation starts.</summary>
/// <param name="changes">The files whose changes triggered it; empty for compilations that were requested.</param>
public sealed class ScriptCompilationStartedEventArgs(IReadOnlyList<string> changes) : EventArgs
{
    public IReadOnlyList<string> Changes { get; } = changes;
}

/// <summary>Raised when a compilation finished, successfully or not.</summary>
public sealed class ScriptCompilationFinishedEventArgs(ScriptCompilationResult result, ScriptAssembly? assembly, Exception? loadError) : EventArgs
{
    /// <summary>The diagnostics, timing and, when it succeeded, the compiled assembly.</summary>
    public ScriptCompilationResult Result { get; } = result;

    /// <summary>The newly loaded scripts when the compilation succeeded and <see cref="ScriptCompilationService.LoadAssemblies"/> is on.</summary>
    public ScriptAssembly? Assembly { get; } = assembly;

    /// <summary>Why the compiled assembly could not be loaded, which is rare and indicates a broken reference.</summary>
    public Exception? LoadError { get; } = loadError;
}

/// <summary>Keeps a project's scripts compiled: watches the scripts folder, compiles shortly after changes stop, and reports each compilation.</summary>
/// <remarks>
/// <para>
/// Compilations run on the thread pool, one at a time; a change during a compilation schedules another one. Events are raised on thread
/// pool threads, so UI code must dispatch to its own thread. Each successful compilation is loaded into its own collectible load context
/// when <see cref="LoadAssemblies"/> is on.
/// </para>
/// <para>
/// The editor hands <see cref="Assembly"/> to new play sessions and to <see cref="ScriptReloader.Reload"/> for running ones. Once no game
/// uses an older assembly any more, call its <see cref="ScriptAssembly.Unload"/> to release it.
/// </para>
/// </remarks>
public sealed class ScriptCompilationService : IDisposable
{
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly HashSet<string> _changes = new(StringComparer.Ordinal);
    private readonly Timer _debounce;
    private FileSystemWatcher? _watcher;
    private Task _running = Task.CompletedTask;
    private bool _disposed;

    public ScriptCompilationService(ScriptCompilerOptions options, ILogger<ScriptCompilationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Compiler = new ScriptCompiler(options);
        _logger = logger ?? NullLogger<ScriptCompilationService>.Instance;
        _debounce = new Timer(_ => _ = CompileChangesAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public ScriptCompiler Compiler { get; }

    public ScriptCompilerOptions Options => Compiler.Options;

    /// <summary>How long after the last change compilation starts, so saving several files compiles once.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Whether successful compilations are loaded as <see cref="ScriptAssembly"/>s.</summary>
    public bool LoadAssemblies { get; set; } = true;

    /// <summary>The result of the latest compilation, or null before the first.</summary>
    public ScriptCompilationResult? LastResult { get; private set; }

    /// <summary>The scripts of the latest successful compilation, or null before one succeeded.</summary>
    public ScriptAssembly? Assembly { get; private set; }

    public bool IsCompiling { get; private set; }

    public event EventHandler<ScriptCompilationStartedEventArgs>? CompilationStarted;

    public event EventHandler<ScriptCompilationFinishedEventArgs>? CompilationFinished;

    /// <summary>Starts watching the scripts folder, warms the compiler up and compiles once, all off the calling thread.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Watch();
        return Task.Run(async () =>
        {
            await Compiler.WarmUpAsync(cancellationToken).ConfigureAwait(false);
            await CompileAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>Compiles after <see cref="Debounce"/>, unless more changes arrive first.</summary>
    public void RequestCompile()
    {
        if (!_disposed)
            _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Compiles now, after a compilation in progress finishes.</summary>
    public Task<ScriptCompilationResult> CompileAsync(CancellationToken cancellationToken = default) => RunAsync([], cancellationToken);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher?.Dispose();
        _debounce.Dispose();
        try
        {
            _running.Wait(TimeSpan.FromSeconds(10));
        }
        catch (AggregateException)
        {
        }

        Compiler.Dispose();
    }

    private void Watch()
    {
        var watched = Options.SourceRoot;
        while (!Directory.Exists(watched) && Path.GetDirectoryName(watched) is { } parent)
            watched = parent;
        var watcher = new FileSystemWatcher(watched, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += (_, e) =>
        {
            Record(e.OldFullPath);
            Record(e.FullPath);
        };
        watcher.Error += (_, _) => RequestCompile();
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Record(e.FullPath);

    private void Record(string path)
    {
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !path.StartsWith(Options.SourceRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || ScriptCompiler.IsExcluded(Options.SourceRoot, path))
            return;
        lock (_lock)
            _changes.Add(path);
        RequestCompile();
    }

    private async Task CompileChangesAsync()
    {
        string[] changes;
        lock (_lock)
        {
            changes = [.. _changes];
            _changes.Clear();
        }

        try
        {
            await RunAsync(changes, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.CompilationCrashed(ex);
        }
    }

    private Task<ScriptCompilationResult> RunAsync(IReadOnlyList<string> changes, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_lock)
        {
            var previous = _running;
            var next = Task.Run(async () =>
            {
                await previous.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).ConfigureAwait(false);
                return await CompileCoreAsync(changes, cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
            _running = next;
            return next;
        }
    }

    private async Task<ScriptCompilationResult> CompileCoreAsync(IReadOnlyList<string> changes, CancellationToken cancellationToken)
    {
        IsCompiling = true;
        try
        {
            CompilationStarted?.Invoke(this, new ScriptCompilationStartedEventArgs(changes));
            var result = await Compiler.CompileAsync(cancellationToken).ConfigureAwait(false);
            LastResult = result;
            ScriptAssembly? assembly = null;
            Exception? loadError = null;
            if (result.Success && LoadAssemblies)
            {
                try
                {
                    assembly = result.Load();
                    Assembly = assembly;
                }
                catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException)
                {
                    loadError = ex;
                }
            }

            if (_logger.IsEnabled(LogLevel.Information))
                _logger.Compiled(result.SourceFiles, result.Duration.TotalMilliseconds, result.Errors.Count(), result.Warnings.Count());
            CompilationFinished?.Invoke(this, new ScriptCompilationFinishedEventArgs(result, assembly, loadError));
            return result;
        }
        finally
        {
            IsCompiling = false;
        }
    }

}

internal static partial class CompilerLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Compiling scripts failed unexpectedly")]
    public static partial void CompilationCrashed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Compiled {Files} scripts in {Milliseconds:0} ms: {Errors} errors, {Warnings} warnings")]
    public static partial void Compiled(this ILogger logger, int files, double milliseconds, int errors, int warnings);
}
