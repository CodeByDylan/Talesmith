using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Assets.Thumbnails;

/// <summary>Gives the editor thumbnails of assets, drawing them on background threads and keeping them in memory and on disk.</summary>
/// <remarks>The most recently requested thumbnails are drawn first, so the ones scrolled into view appear before those scrolled past.
/// Thread-safe; results complete on thread pool threads.</remarks>
public sealed partial class ThumbnailService : IDisposable
{
    /// <summary>The width and height thumbnails are drawn at, in pixels.</summary>
    public const int Size = 160;

    private const int MemoryCapacity = 500;

    private readonly IProjectService _project;
    private readonly IThumbnailRenderer[] _renderers;
    private readonly EditorPluginGuard _plugins;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly Lock _lock = new();
    private readonly List<Request> _queue = [];
    private readonly Dictionary<AssetGuid, Request> _pending = [];
    private readonly Dictionary<AssetGuid, (string Key, Bitmap? Bitmap)> _memory = [];
    private readonly LinkedList<AssetGuid> _recent = [];
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _workers;

    public ThumbnailService(IProjectService project, IEnumerable<IThumbnailRenderer> renderers, ILogger<ThumbnailService> logger, EditorPluginGuard plugins)
    {
        _plugins = plugins;
        _project = project;
        _renderers = [.. renderers.Reverse()];
        _logger = logger;
        Cache = new ThumbnailCache(project.Project.GetStatePath("thumbnails"));
        _workers = [.. Enumerable.Range(0, Math.Clamp(Environment.ProcessorCount / 2, 1, 3)).Select(_ => Task.Run(WorkAsync))];
    }

    public ThumbnailCache Cache { get; }

    /// <summary>Whether the asset has a thumbnail rather than an icon.</summary>
    public bool CanRender(AssetRecord asset) => !asset.IsFolder && Find(asset) is not null;

    /// <summary>The thumbnail if it is in memory and current, without drawing it.</summary>
    public bool TryGetCached(AssetRecord asset, out Bitmap? thumbnail)
    {
        var key = ThumbnailCache.Key(asset);
        lock (_lock)
        {
            if (_memory.TryGetValue(asset.Guid, out var entry) && entry.Key == key)
            {
                thumbnail = entry.Bitmap;
                return true;
            }
        }

        thumbnail = null;
        return false;
    }

    /// <summary>The asset's thumbnail, read from disk or drawn; null when it has none or could not be drawn.</summary>
    public Task<Bitmap?> GetAsync(AssetRecord asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (TryGetCached(asset, out var cached))
            return Task.FromResult(cached);
        if (!CanRender(asset))
            return Task.FromResult<Bitmap?>(null);
        lock (_lock)
        {
            if (_pending.TryGetValue(asset.Guid, out var pending) && pending.Asset.ImportStamp == asset.ImportStamp)
            {
                _queue.Remove(pending);
                _queue.Add(pending);
                return pending.Completion.Task;
            }

            var request = new Request(asset);
            _pending[asset.Guid] = request;
            _queue.Add(request);
            _signal.Release();
            return request.Completion.Task;
        }
    }

    /// <summary>Forgets an asset's thumbnail in memory, such as after it changed.</summary>
    public void Invalidate(AssetGuid guid)
    {
        lock (_lock)
            _memory.Remove(guid);
    }

    public void Dispose()
    {
        _stop.Cancel();
        lock (_lock)
        {
            foreach (var request in _queue)
                request.Completion.TrySetResult(null);
            _queue.Clear();
        }

        _signal.Release(_workers.Length);
    }

    private IThumbnailRenderer? Find(AssetRecord asset)
    {
        foreach (var renderer in _renderers)
        {
            if (_plugins.IsFaulted(renderer))
                continue;
            try
            {
                if (renderer.CanRender(asset))
                    return renderer;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && _plugins.Isolate(renderer, "draw thumbnails", ex))
            {
            }
        }

        return null;
    }

    private async Task WorkAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await _signal.WaitAsync().ConfigureAwait(false);
            Request? request;
            lock (_lock)
            {
                if (_queue.Count == 0)
                    continue;
                request = _queue[^1];
                _queue.RemoveAt(_queue.Count - 1);
            }

            Bitmap? bitmap = null;
            try
            {
                bitmap = Load(request.Asset);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(_logger, ex, request.Asset.Path);
            }

            lock (_lock)
            {
                if (_pending.TryGetValue(request.Asset.Guid, out var current) && ReferenceEquals(current, request))
                    _pending.Remove(request.Asset.Guid);
                Remember(request.Asset, bitmap);
            }

            request.Completion.TrySetResult(bitmap);
        }
    }

    private Bitmap? Load(AssetRecord asset)
    {
        var png = Cache.TryRead(asset);
        if (png is null)
        {
            if (Find(asset) is not { } renderer || _project.Database is not { } database)
                return null;
            var file = Path.Combine(database.RootFolder, asset.Path.Replace('/', Path.DirectorySeparatorChar));
            png = renderer.Render(file, asset, Size, _stop.Token);
            if (png is null)
                return null;
            Cache.Write(asset, png);
        }

        using var stream = new MemoryStream(png, writable: false);
        return new Bitmap(stream);
    }

    private void Remember(AssetRecord asset, Bitmap? bitmap)
    {
        _memory[asset.Guid] = (ThumbnailCache.Key(asset), bitmap);
        _recent.Remove(asset.Guid);
        _recent.AddFirst(asset.Guid);
        while (_recent.Count > MemoryCapacity)
        {
            _memory.Remove(_recent.Last!.Value);
            _recent.RemoveLast();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No thumbnail for {Path}")]
    private static partial void LogFailed(ILogger logger, Exception exception, string path);

    private sealed class Request(AssetRecord asset)
    {
        public AssetRecord Asset { get; } = asset;

        public TaskCompletionSource<Bitmap?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
