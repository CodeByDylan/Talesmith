using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Talesmith.Assets.Database;

/// <summary>Keeps an <see cref="IAssetManager"/> in step with an <see cref="AssetDatabase"/>: reloads loaded assets whose files or settings changed and follows moves.</summary>
/// <remarks>
/// <para>A changed asset that is loaded is reloaded, and the manager reloads the loaded assets that imported it. A changed file that is not
/// loaded itself, such as an SkSL file of a .tshader, reloads the loaded assets that refer to it according to the dependency graph.
/// Deleted assets stay loaded, so a running game keeps working. Successful reloads are marked as imported in the database.</para>
/// <para>Batches are processed one at a time in the order they arrive. Dispose to disconnect.</para>
/// </remarks>
public sealed partial class AssetHotReload : IDisposable
{
    private readonly AssetDatabase _database;
    private volatile IAssetManager _assets;
    private readonly ILogger<AssetHotReload> _logger;
    private readonly Lock _lock = new();
    private ImmutableArray<Func<AssetChange, bool>> _filters = [];
    private Task _pending = Task.CompletedTask;
    private bool _disposed;

    public AssetHotReload(AssetDatabase database, IAssetManager assets, ILogger<AssetHotReload> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);
        _database = database;
        _assets = assets;
        _logger = logger;
        _database.AssetsChanged += OnAssetsChanged;
    }

    /// <summary>The manager kept in step; set it when the game it belongs to is replaced by another.</summary>
    public IAssetManager Assets
    {
        get => _assets;
        set => _assets = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Completes when every batch received so far has been processed.</summary>
    public Task Idle
    {
        get
        {
            lock (_lock)
                return _pending;
        }
    }

    /// <summary>Skips reloading changed assets for which <paramref name="shouldReload"/> returns false, such as files the editor wrote from
    /// the instance it holds; dispose the result to remove the filter.</summary>
    /// <remarks>Filters run on a background thread.</remarks>
    public IDisposable AddFilter(Func<AssetChange, bool> shouldReload)
    {
        ArgumentNullException.ThrowIfNull(shouldReload);
        ImmutableInterlocked.Update(ref _filters, filters => filters.Add(shouldReload));
        return new Filter(this, shouldReload);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _database.AssetsChanged -= OnAssetsChanged;
    }

    private void OnAssetsChanged(object? sender, AssetChangesEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _pending = _pending.ContinueWith(_ => ProcessAsync(e.Changes), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ProcessAsync(IReadOnlyList<AssetChange> changes)
    {
        var reload = new List<AssetChange>();
        foreach (var change in changes)
        {
            switch (change.Change)
            {
                case AssetChangeKind.Moved:
                    _assets.NotifyMoved(change.OldPath!, change.Path);
                    if (change.Reasons != AssetChangeReasons.None)
                        reload.Add(change);
                    break;
                case AssetChangeKind.Changed:
                case AssetChangeKind.Added:
                    if (ShouldReload(change))
                        reload.Add(change);
                    else
                        await _database.MarkImportedAsync(change.Guid).ConfigureAwait(false);
                    break;
            }
        }

        var visited = new HashSet<AssetGuid>();
        foreach (var change in reload)
            await ReloadNearestLoadedAsync(change.Guid, change.Path, visited).ConfigureAwait(false);
    }

    private bool ShouldReload(AssetChange change)
    {
        foreach (var filter in _filters)
        {
            if (!filter(change))
                return false;
        }

        return true;
    }

    /// <summary>Reloads the asset if it is loaded, which also reloads what imported it; otherwise looks for loaded assets referring to it.</summary>
    private async Task ReloadNearestLoadedAsync(AssetGuid guid, string path, HashSet<AssetGuid> visited)
    {
        if (!visited.Add(guid))
            return;

        if (_assets.IsLoaded(path))
        {
            await ReloadAsync(path, guid).ConfigureAwait(false);
            return;
        }

        foreach (var dependent in _database.GetDependents(guid))
        {
            if (_database.TryGetAsset(dependent, out var record))
                await ReloadNearestLoadedAsync(record.Guid, record.Path, visited).ConfigureAwait(false);
        }
    }

    private async Task ReloadAsync(string path, AssetGuid guid)
    {
        try
        {
            if (await _assets.ReloadAsync(path).ConfigureAwait(false))
                await _database.MarkImportedAsync(guid).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogReloadFailed(path, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Reloading {Path} failed")]
    private partial void LogReloadFailed(string path, Exception exception);

    private sealed class Filter(AssetHotReload owner, Func<AssetChange, bool> filter) : IDisposable
    {
        public void Dispose() => ImmutableInterlocked.Update(ref owner._filters, filters => filters.Remove(filter));
    }
}
