using Microsoft.Extensions.Logging;

namespace Talesmith.Assets.Database;

public sealed partial class AssetDatabase
{
    private static readonly TimeSpan CacheSaveDelay = TimeSpan.FromSeconds(1);

    private readonly Lock _watchLock = new();
    private readonly HashSet<string> _dirty = new(AssetPath.Comparer);
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private Timer? _saveTimer;
    private Task _processing = Task.CompletedTask;
    private bool _rescanRequested;

    /// <summary>Whether file system changes are being watched.</summary>
    public bool IsWatching
    {
        get
        {
            lock (_watchLock)
                return _watcher is not null;
        }
    }

    /// <summary>Starts watching the asset folder; changes are reported through <see cref="AssetsChanged"/> once the folder is quiet.</summary>
    /// <remarks>When the system drops change notifications, the whole folder is scanned again.</remarks>
    public void StartWatching()
    {
        EnsureScanned();
        lock (_watchLock)
        {
            if (_watcher is not null)
                return;

            _debounce = new Timer(_ => ProcessDirty(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(RootFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            _watcher.Created += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Error += OnWatcherError;
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Stops watching and waits for changes already being processed.</summary>
    public async Task StopWatchingAsync()
    {
        Task processing;
        lock (_watchLock)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounce?.Dispose();
            _debounce = null;
            _dirty.Clear();
            _rescanRequested = false;
            processing = _processing;
        }

        await processing.ConfigureAwait(false);
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e) => MarkDirty(e.FullPath);

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        MarkDirty(e.OldFullPath);
        MarkDirty(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        LogWatcherError(e.GetException());
        lock (_watchLock)
        {
            _rescanRequested = true;
            _debounce?.Change(_options.WatchDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void MarkDirty(string fullPath)
    {
        if (ToRelativeOrNull(fullPath) is not { Length: > 0 } path)
            return;
        if (AssetMetaFile.IsMetaPath(path) && AssetPath.GetFileName(path)[0] != '.')
            path = AssetMetaFile.GetAssetPath(path);
        if (IsIgnoredPath(path))
            return;

        lock (_watchLock)
        {
            if (_debounce is null)
                return;
            _dirty.Add(path);
            _debounce.Change(_options.WatchDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void ProcessDirty()
    {
        lock (_watchLock)
        {
            if (_debounce is null || (_dirty.Count == 0 && !_rescanRequested))
                return;

            HashSet<string>? dirty = _rescanRequested ? null : new HashSet<string>(_dirty, AssetPath.Comparer);
            _dirty.Clear();
            _rescanRequested = false;
            _processing = _processing.ContinueWith(_ => ReconcileWatchedAsync(dirty), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task ReconcileWatchedAsync(HashSet<string>? dirty)
    {
        try
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            Reconciliation reconciliation;
            try
            {
                reconciliation = await ReconcileAsync(dirty, null, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            ScheduleCacheSave();
            Raise(reconciliation.Changes);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            LogWatchFailed(ex);
            lock (_watchLock)
            {
                if (dirty is not null)
                    _dirty.UnionWith(dirty);
                _debounce?.Change(_options.WatchDebounce * 4, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void ScheduleCacheSave()
    {
        if (_options.CacheFolder is null)
            return;
        lock (_watchLock)
        {
            _saveTimer ??= new Timer(_ => _ = FlushCacheAsync(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(CacheSaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Writes the cache now instead of waiting for the delayed save.</summary>
    public async Task FlushCacheAsync()
    {
        if (_options.CacheFolder is not { } folder)
            return;

        AssetRecord[] records;
        lock (_stateLock)
        {
            if (!_scanned)
                return;
            records = [.. _records.Values];
        }

        try
        {
            await AssetDatabaseCache.SaveAsync(folder, records, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCacheSaveFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The asset folder watcher lost changes, so the folder will be scanned again")]
    private partial void LogWatcherError(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing asset changes failed; they will be retried")]
    private partial void LogWatchFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The asset database cache could not be saved")]
    private partial void LogCacheSaveFailed(Exception exception);
}
