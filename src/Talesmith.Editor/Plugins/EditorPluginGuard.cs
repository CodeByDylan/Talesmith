using System.Collections.Concurrent;
using System.Reflection;
using Talesmith.Editor.Console;
using Talesmith.Plugins;

namespace Talesmith.Editor.Plugins;

/// <summary>A failure of code a plugin contributed to the editor.</summary>
/// <param name="Action">What the editor asked the plugin to do, such as "add its commands".</param>
public sealed record EditorPluginFault(string PluginId, string Action, string Message, string Details);

/// <summary>Calls code that plugins contributed to the editor, so a plugin that throws is reported and skipped instead of breaking the editor.</summary>
/// <remarks>Code from the editor itself is called as is. A plugin's contribution that threw once is skipped from then on. Safe to use from any
/// thread.</remarks>
public sealed class EditorPluginGuard
{
    private readonly IConsole _console;
    private readonly Dictionary<Assembly, PluginInfo> _owners = [];
    private readonly ConcurrentDictionary<object, byte> _faulted = new(ReferenceEqualityComparer.Instance);
    private readonly Lock _gate = new();
    private readonly List<EditorPluginFault> _faults = [];

    public EditorPluginGuard(IConsole console, IEnumerable<PluginAssemblies> assemblies)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(assemblies);
        _console = console;
        foreach (var loaded in assemblies)
        {
            _owners[loaded.Assembly] = loaded.Plugin;
            if (loaded.EditorAssembly is { } editor)
                _owners[editor] = loaded.Plugin;
        }
    }

    /// <summary>Every failure so far, oldest first.</summary>
    public IReadOnlyList<EditorPluginFault> Faults
    {
        get
        {
            lock (_gate)
                return [.. _faults];
        }
    }

    /// <summary>Raised on the thread of the failure whenever a fault is recorded.</summary>
    public event EventHandler? Changed;

    /// <summary>The plugin whose assemblies define a contribution's type, or null for the editor's own code.</summary>
    public PluginInfo? FindPlugin(object contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        return FindPlugin(contribution as Type ?? contribution.GetType());
    }

    /// <summary>Whether a plugin's contribution threw before and is skipped.</summary>
    public bool IsFaulted(object contribution) => _faulted.ContainsKey(contribution);

    /// <summary>Calls a contribution; returns false when it belongs to a plugin and threw now or before.</summary>
    public bool Run(object contribution, string action, Action call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return Run(contribution, action, () =>
        {
            call();
            return true;
        }, false);
    }

    /// <summary>Calls a contribution; returns <paramref name="fallback"/> when it belongs to a plugin and threw now or before.</summary>
    public T Run<T>(object contribution, string action, Func<T> call, T fallback)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(call);
        if (IsFaulted(contribution))
            return fallback;
        try
        {
            return call();
        }
        catch (Exception ex) when (IsPluginFailure(ex) && Isolate(contribution, action, ex))
        {
            return fallback;
        }
    }

    /// <summary>Awaits a contribution; returns <paramref name="fallback"/> when it belongs to a plugin and threw now or before.</summary>
    public async Task<T> RunAsync<T>(object contribution, string action, Func<Task<T>> call, T fallback)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(call);
        if (IsFaulted(contribution))
            return fallback;
        try
        {
            return await call().ConfigureAwait(true);
        }
        catch (Exception ex) when (IsPluginFailure(ex) && Isolate(contribution, action, ex))
        {
            return fallback;
        }
    }

    /// <summary>Awaits a contribution; returns false when it belongs to a plugin and threw now or before.</summary>
    public Task<bool> RunAsync(object contribution, string action, Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return RunAsync(contribution, action, async () =>
        {
            await call().ConfigureAwait(true);
            return true;
        }, false);
    }

    /// <summary>Skips a contribution from now on and reports its failure once, when it belongs to a plugin.</summary>
    /// <returns>Whether the contribution belongs to a plugin; the editor's own failures are left to the caller.</returns>
    public bool Isolate(object contribution, string action, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        ArgumentNullException.ThrowIfNull(exception);
        if (FindPlugin(contribution) is not { } plugin)
            return false;
        if (_faulted.TryAdd(contribution, 0))
            Report(plugin, action, exception);
        return true;
    }

    /// <summary>Records and reports a failure of a plugin's editor code.</summary>
    public void Report(PluginInfo plugin, string action, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.GetBaseException().Message;
        lock (_gate)
            _faults.Add(new EditorPluginFault(plugin.Id, action, message, exception.ToString()));
        _console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Error, ConsoleSource.Plugin,
            $"The plugin {plugin.Id} failed to {action} and was skipped: {message}", plugin.Id)
        { Details = exception.ToString() });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PluginInfo? FindPlugin(Type type) => _owners.GetValueOrDefault(type.Assembly);

    private static bool IsPluginFailure(Exception exception) => exception is not (OutOfMemoryException or OperationCanceledException);
}
