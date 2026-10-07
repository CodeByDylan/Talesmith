using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Talesmith.Plugins;

/// <summary>Tracks plugins that were asked to unload until the runtime has released their code.</summary>
/// <remarks>
/// A plugin's code is released only once nothing references it: dispose every game built with it and drop references to its services and
/// types, then call <see cref="WaitForUnload"/>.
/// </remarks>
public sealed class PluginUnloadResult
{
    private readonly (string Id, WeakReference Context)[] _contexts;

    internal PluginUnloadResult(IEnumerable<(string Id, WeakReference Context)> contexts, bool collectible)
    {
        _contexts = [.. contexts];
        IsCollectible = collectible;
    }

    public static PluginUnloadResult None { get; } = new([], collectible: true);

    /// <summary>The plugins that were asked to unload.</summary>
    public IReadOnlyList<string> PluginIds => [.. _contexts.Select(c => c.Id)];

    /// <summary>False when the plugins were loaded into contexts that can never unload.</summary>
    public bool IsCollectible { get; }

    /// <summary>The plugins whose code is still in memory.</summary>
    public IReadOnlyList<string> StillLoaded => [.. _contexts.Where(c => !IsCollectible || c.Context.IsAlive).Select(c => c.Id)];

    public bool IsComplete => StillLoaded.Count == 0;

    /// <summary>Runs the garbage collector until every plugin is released or <paramref name="timeout"/> passes.</summary>
    /// <returns>Whether every plugin was released.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool WaitForUnload(TimeSpan timeout)
    {
        if (!IsCollectible)
            return _contexts.Length == 0;

        if (IsComplete)
            return true;

        RuntimeCaches.Clear();
        var clock = Stopwatch.StartNew();
        do
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            if (IsComplete)
                return true;
            Thread.Sleep(10);
        }
        while (clock.Elapsed < timeout);

        return IsComplete;
    }
}
