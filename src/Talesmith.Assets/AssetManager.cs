using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Talesmith.Events;

namespace Talesmith.Assets;

/// <summary>The default <see cref="IAssetManager"/>: imports assets on the thread pool with the registered importers and caches them.</summary>
/// <remarks>
/// <para>When several importers handle the same extension and asset type, the one registered last wins. A failed load is not cached, so
/// loading the path again retries. Unloading an asset that is still importing forgets it without disposing it.</para>
/// <para>Importers receive the asset's .meta information from the <see cref="IAssetMetaProvider"/>, when one is registered. Import and
/// reload events are queued on the <see cref="IEventBus"/>, when one is registered, so they arrive on the game thread.</para>
/// </remarks>
public sealed partial class AssetManager : IAssetManager, IDisposable
{
    private readonly Dictionary<string, IAssetImporter[]> _importersByExtension;
    private readonly Dictionary<AssetKey, Entry> _entries = new();
    private readonly ConditionalWeakTable<object, Entry> _entryByAsset = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly ILogger<AssetManager> _logger;
    private readonly IAssetMetaProvider? _metas;
    private readonly IEventBus? _events;
    private readonly IDisposable? _reloadSubscription;

    public AssetManager(
        IAssetSource source,
        IEnumerable<IAssetImporter> importers,
        ILogger<AssetManager> logger,
        IAssetCatalog? catalog = null,
        IAssetMetaProvider? metas = null,
        IEventBus? events = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(importers);
        ArgumentNullException.ThrowIfNull(logger);
        Source = source;
        Catalog = catalog;
        _logger = logger;
        _metas = metas ?? catalog as IAssetMetaProvider;
        _events = events;
        _importersByExtension = importers
            .SelectMany(importer => importer.Extensions.Select(extension => (Extension: extension.ToLowerInvariant(), Importer: importer)))
            .GroupBy(pair => pair.Extension, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Importer).Reverse().ToArray(), StringComparer.Ordinal);
        _reloadSubscription = _events?.Subscribe<AssetReloaded>(DisposeReplaced, priority: int.MinValue);
    }

    public IAssetSource Source { get; }

    public IAssetCatalog? Catalog { get; }

    public int LoadedCount
    {
        get
        {
            lock (_lock)
                return _entries.Values.Count(entry => entry.Task.IsCompletedSuccessfully);
        }
    }

    public async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
    {
        var normalized = AssetPath.Normalize(path);
        var importer = FindImporter(normalized, typeof(T));
        var key = new AssetKey(normalized, importer.AssetType);
        var task = Acquire(key, importer);
        object asset;
        try
        {
            asset = await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReleaseKey(key);
            throw;
        }

        if (asset is T typed)
            return typed;

        ReleaseKey(key);
        throw new AssetException($"The asset '{normalized}' was imported as {asset.GetType().Name}, not {typeof(T).Name}.");
    }

    public Task<T> LoadAsync<T>(AssetGuid guid, CancellationToken cancellationToken = default) where T : class =>
        LoadAsync<T>(ResolveGuid(guid), cancellationToken);

    public T Load<T>(string path) where T : class =>
        Task.Run(() => LoadAsync<T>(path)).GetAwaiter().GetResult();

    public T Load<T>(AssetGuid guid) where T : class => Load<T>(ResolveGuid(guid));

    public bool TryGet<T>(string path, out T asset) where T : class
    {
        asset = null!;
        var normalized = AssetPath.Normalize(path);
        if (TryFindImporter(normalized, typeof(T)) is not { } importer)
            return false;

        Entry? entry;
        lock (_lock)
            _entries.TryGetValue(new AssetKey(normalized, importer.AssetType), out entry);
        if (entry?.Task is not { IsCompletedSuccessfully: true } task || task.Result is not T loaded)
            return false;

        asset = loaded;
        return true;
    }

    public bool Release(object asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        AssetKey key;
        lock (_lock)
        {
            if (!_entryByAsset.TryGetValue(asset, out var entry) || !_entries.TryGetValue(entry.Key, out var current) || current != entry)
                return false;
            key = entry.Key;
        }

        return ReleaseKey(key);
    }

    public void Unload(string path)
    {
        var normalized = AssetPath.Normalize(path);
        List<Entry> removed;
        lock (_lock)
        {
            removed = [.. _entries.Values.Where(entry => string.Equals(entry.Key.Path, normalized, StringComparison.Ordinal))];
            foreach (var entry in removed)
                _entries.Remove(entry.Key);
        }

        foreach (var entry in removed)
            Retire(entry);
    }

    public bool IsLoaded(string path)
    {
        var normalized = AssetPath.Normalize(path);
        lock (_lock)
            return _entries.Keys.Any(key => string.Equals(key.Path, normalized, StringComparison.Ordinal));
    }

    public int GetReferenceCount(string path)
    {
        var normalized = AssetPath.Normalize(path);
        lock (_lock)
            return _entries.Values.Where(entry => string.Equals(entry.Key.Path, normalized, StringComparison.Ordinal)).Sum(entry => entry.References);
    }

    public async Task<bool> ReloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = AssetPath.Normalize(path);
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Entry> targets;
            lock (_lock)
                targets = [.. _entries.Values.Where(entry => string.Equals(entry.Key.Path, normalized, StringComparison.Ordinal))];
            if (targets.Count == 0)
                return false;

            var reloaded = new HashSet<AssetKey>();
            var succeeded = true;
            var pending = new Queue<Entry>(targets);
            while (pending.TryDequeue(out var entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!reloaded.Add(entry.Key))
                    continue;

                if (!await ReloadEntryAsync(entry).ConfigureAwait(false))
                {
                    if (targets.Contains(entry))
                        succeeded = false;
                    continue;
                }

                foreach (var dependent in FindDependents(entry.Key))
                    pending.Enqueue(dependent);
            }

            return succeeded;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void NotifyMoved(string oldPath, string newPath)
    {
        var from = AssetPath.Normalize(oldPath);
        var to = AssetPath.Normalize(newPath);
        if (from.Length == 0 || string.Equals(from, to, StringComparison.Ordinal))
            return;

        lock (_lock)
        {
            var moved = _entries.Values.Where(entry => AssetPath.IsWithin(entry.Key.Path, from)).ToList();
            if (moved.Count == 0)
                return;

            var renamed = new Dictionary<AssetKey, AssetKey>();
            foreach (var entry in moved)
            {
                _entries.Remove(entry.Key);
                var key = entry.Key with { Path = to + entry.Key.Path[from.Length..] };
                renamed[entry.Key] = key;
                entry.Key = key;
            }

            foreach (var entry in moved)
                _entries[entry.Key] = entry;
            foreach (var entry in _entries.Values)
            {
                for (var i = 0; i < entry.Dependencies.Length; i++)
                {
                    if (renamed.TryGetValue(entry.Dependencies[i], out var key))
                        entry.Dependencies[i] = key;
                }
            }
        }
    }

    public bool TryGetPath(object asset, out string path)
    {
        ArgumentNullException.ThrowIfNull(asset);
        lock (_lock)
        {
            path = _entryByAsset.TryGetValue(asset, out var entry) ? entry.Key.Path : string.Empty;
            return path.Length > 0;
        }
    }

    public AssetGuid GetGuid(object asset) =>
        TryGetPath(asset, out var path) && Catalog is not null && Catalog.TryGetGuid(path, out var guid) ? guid : AssetGuid.Empty;

    /// <summary>Stops listening to reload events; loaded assets stay as they are.</summary>
    public void Dispose()
    {
        _reloadSubscription?.Dispose();
        _reloadGate.Dispose();
    }

    private string ResolveGuid(AssetGuid guid)
    {
        if (Catalog is null)
            throw new AssetException($"The asset {guid} cannot be loaded by guid because the game has no asset catalog.");
        if (!Catalog.TryGetPath(guid, out var path))
            throw new AssetException($"No asset has the guid {guid}.");
        return path;
    }

    private Task<object> Acquire(AssetKey key, IAssetImporter importer)
    {
        Entry entry;
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.References++;
                return existing.Task;
            }

            entry = new Entry(key) { References = 1 };
            _entries.Add(key, entry);
            entry.Task = Task.Run(() => ImportEntryAsync(entry, importer));
        }

        entry.Task.ContinueWith(
            (failed, state) => Forget((Entry)state!, failed),
            entry,
            CancellationToken.None,
            TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return entry.Task;
    }

    private async Task<object> ImportEntryAsync(Entry entry, IAssetImporter importer)
    {
        var (asset, dependencies) = await ImportAsync(entry.Key.Path, importer, isReload: false).ConfigureAwait(false);
        bool current;
        lock (_lock)
        {
            current = _entries.TryGetValue(entry.Key, out var registered) && registered == entry;
            if (current)
            {
                entry.Dependencies = dependencies;
                _entryByAsset.AddOrUpdate(asset, entry);
            }
        }

        if (!current)
            ReleaseKeys(dependencies);
        return asset;
    }

    private async Task<bool> ReloadEntryAsync(Entry entry)
    {
        if (entry.Task is not { IsCompletedSuccessfully: true } loaded || TryFindImporter(entry.Key.Path, entry.Key.AssetType) is not { } importer)
            return false;

        object asset;
        AssetKey[] dependencies;
        try
        {
            (asset, dependencies) = await ImportAsync(entry.Key.Path, importer, isReload: true).ConfigureAwait(false);
        }
        catch (AssetException)
        {
            return false;
        }

        var old = loaded.Result;
        AssetKey[] oldDependencies;
        lock (_lock)
        {
            if (!_entries.TryGetValue(entry.Key, out var registered) || registered != entry)
            {
                oldDependencies = dependencies;
                old = null;
            }
            else
            {
                oldDependencies = entry.Dependencies;
                entry.Dependencies = dependencies;
                entry.Task = Task.FromResult(asset);
                _entryByAsset.AddOrUpdate(asset, entry);
            }
        }

        ReleaseKeys(oldDependencies);
        if (old is null)
            return false;

        LogReloaded(entry.Key.Path, entry.Key.AssetType.Name);
        var reloaded = new AssetReloaded(entry.Key.Path, GuidOf(entry.Key.Path), entry.Key.AssetType, old, asset);
        if (_events is null)
            DisposeAsset(old);
        else
            _events.Enqueue(reloaded);
        return true;
    }

    private async Task<(object Asset, AssetKey[] Dependencies)> ImportAsync(string path, IAssetImporter importer, bool isReload)
    {
        if (!Source.Exists(path))
            throw new AssetException($"The asset '{path}' does not exist in {Source.Description}.");

        var stopwatch = Stopwatch.StartNew();
        var scope = new ImportScope(this);
        object? asset;
        try
        {
            var meta = _metas is not null && _metas.TryGetMeta(path, out var found) ? found : null;
            var context = new AssetImportContext(path, Source, scope, _logger) { Meta = meta, Catalog = Catalog };
            asset = await importer.ImportAsync(context, CancellationToken.None).ConfigureAwait(false);
            if (asset is null)
                throw new AssetException($"{importer.GetType().Name} returned no asset for '{path}'.");
        }
        catch (Exception ex)
        {
            ReleaseKeys(scope.Complete());
            LogImportFailed(path, ex);
            throw ex as AssetException ?? new AssetException($"Importing '{path}' with {importer.GetType().Name} failed: {ex.Message}", ex);
        }

        var elapsed = stopwatch.Elapsed;
        LogImported(path, importer.AssetType.Name, elapsed.TotalMilliseconds);
        _events?.Enqueue(new AssetImported(path, GuidOf(path), importer.AssetType, asset, isReload, elapsed));
        return (asset, scope.Complete());
    }

    private List<Entry> FindDependents(AssetKey key)
    {
        lock (_lock)
            return [.. _entries.Values.Where(entry => entry.Dependencies.Contains(key))];
    }

    private void Forget(Entry entry, Task<object> failed)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(entry.Key, out var current) && current == entry && ReferenceEquals(entry.Task, failed))
                _entries.Remove(entry.Key);
        }
    }

    private bool ReleaseKey(AssetKey key)
    {
        Entry? retired = null;
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return false;
            if (--entry.References <= 0)
            {
                _entries.Remove(key);
                retired = entry;
            }
        }

        if (retired is not null)
            Retire(retired);
        return true;
    }

    private void ReleaseKeys(AssetKey[] keys)
    {
        foreach (var key in keys)
            ReleaseKey(key);
    }

    private void Retire(Entry entry)
    {
        AssetKey[] dependencies;
        lock (_lock)
        {
            dependencies = entry.Dependencies;
            entry.Dependencies = [];
        }

        if (entry.Task.IsCompletedSuccessfully)
            DisposeAsset(entry.Task.Result);
        ReleaseKeys(dependencies);
    }

    private AssetGuid GuidOf(string path) => Catalog is not null && Catalog.TryGetGuid(path, out var guid) ? guid : AssetGuid.Empty;

    private IAssetImporter FindImporter(string path, Type assetType)
    {
        if (TryFindImporter(path, assetType) is { } importer)
            return importer;

        var extension = AssetPath.GetExtension(path);
        if (!_importersByExtension.TryGetValue(extension, out var candidates))
        {
            throw new AssetException(extension.Length == 0
                ? $"The asset '{path}' has no file extension, so no importer can load it."
                : $"No importer is registered for '{extension}' files, so '{path}' cannot be loaded.");
        }

        var available = string.Join(", ", candidates.Select(c => c.AssetType.Name).Distinct());
        throw new AssetException($"No importer loads '{extension}' files as {assetType.Name}, so '{path}' cannot be loaded. '{extension}' files load as: {available}.");
    }

    private IAssetImporter? TryFindImporter(string path, Type assetType)
    {
        if (!_importersByExtension.TryGetValue(AssetPath.GetExtension(path), out var candidates))
            return null;
        foreach (var importer in candidates)
        {
            if (assetType.IsAssignableFrom(importer.AssetType))
                return importer;
        }

        return null;
    }

    private static void DisposeReplaced(ref AssetReloaded e) => DisposeAsset(e.OldAsset);

    private static void DisposeAsset(object asset)
    {
        switch (asset)
        {
            case IDisposable disposable:
                disposable.Dispose();
                break;
            case IAsyncDisposable asyncDisposable:
                Task.Run(() => asyncDisposable.DisposeAsync().AsTask()).GetAwaiter().GetResult();
                break;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Imported {Path} as {AssetType} in {Milliseconds:0.0} ms")]
    private partial void LogImported(string path, string assetType, double milliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reloaded {Path} as {AssetType}")]
    private partial void LogReloaded(string path, string assetType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Importing {Path} failed")]
    private partial void LogImportFailed(string path, Exception exception);

    private readonly record struct AssetKey(string Path, Type AssetType);

    private sealed class Entry(AssetKey key)
    {
        public AssetKey Key { get; set; } = key;

        public Task<object> Task { get; set; } = null!;

        public int References { get; set; }

        /// <summary>The assets the importer loaded through its context; each holds one reference released with this entry.</summary>
        public AssetKey[] Dependencies { get; set; } = [];
    }

    /// <summary>The <see cref="IAssetManager"/> an importer sees: loads go through the manager and are recorded as dependencies.</summary>
    private sealed class ImportScope(AssetManager owner) : IAssetManager
    {
        private readonly List<AssetKey> _dependencies = [];
        private readonly Lock _lock = new();
        private bool _completed;

        public IAssetSource Source => owner.Source;

        public IAssetCatalog? Catalog => owner.Catalog;

        public int LoadedCount => owner.LoadedCount;

        public async Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class
        {
            var asset = await owner.LoadAsync<T>(path, cancellationToken).ConfigureAwait(false);
            Record(new AssetKey(AssetPath.Normalize(path), owner.FindImporter(AssetPath.Normalize(path), typeof(T)).AssetType));
            return asset;
        }

        public Task<T> LoadAsync<T>(AssetGuid guid, CancellationToken cancellationToken = default) where T : class =>
            LoadAsync<T>(owner.ResolveGuid(guid), cancellationToken);

        public T Load<T>(string path) where T : class => Task.Run(() => LoadAsync<T>(path)).GetAwaiter().GetResult();

        public T Load<T>(AssetGuid guid) where T : class => Load<T>(owner.ResolveGuid(guid));

        public bool TryGet<T>(string path, out T asset) where T : class => owner.TryGet(path, out asset);

        public bool Release(object asset) => owner.Release(asset);

        public void Unload(string path) => owner.Unload(path);

        public bool IsLoaded(string path) => owner.IsLoaded(path);

        public int GetReferenceCount(string path) => owner.GetReferenceCount(path);

        public Task<bool> ReloadAsync(string path, CancellationToken cancellationToken = default) => owner.ReloadAsync(path, cancellationToken);

        public void NotifyMoved(string oldPath, string newPath) => owner.NotifyMoved(oldPath, newPath);

        public bool TryGetPath(object asset, out string path) => owner.TryGetPath(asset, out path);

        public AssetGuid GetGuid(object asset) => owner.GetGuid(asset);

        public AssetKey[] Complete()
        {
            lock (_lock)
            {
                _completed = true;
                return [.. _dependencies];
            }
        }

        private void Record(AssetKey key)
        {
            lock (_lock)
            {
                if (!_completed)
                {
                    _dependencies.Add(key);
                    return;
                }
            }

            owner.ReleaseKey(key);
        }
    }
}
