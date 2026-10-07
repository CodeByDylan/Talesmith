using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Projects;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Prefabs;

/// <summary>The project's prefab documents by guid, read from disk once and read again when their files change.</summary>
/// <remarks>Documents returned are shared; clone them before changing them. Use it on the UI thread.</remarks>
public sealed partial class PrefabLibrary : IDisposable
{
    private readonly IProjectService _project;
    private readonly ILogger<PrefabLibrary> _logger;
    private readonly Dictionary<AssetGuid, PrefabDocument?> _cache = [];
    private bool _disposed;
    private AssetDatabase? _database;

    public PrefabLibrary(IProjectService project, ILogger<PrefabLibrary> logger)
    {
        _project = project;
        _logger = logger;
        _project.StatusChanged += (_, _) => Hook();
        Hook();
    }

    /// <summary>Raised on the UI thread after a prefab file changed, moved or was deleted.</summary>
    public event EventHandler<PrefabChangedEventArgs>? Changed;

    /// <summary>The prefab with the guid, or null when it does not exist or cannot be read.</summary>
    public PrefabDocument? Get(AssetGuid guid)
    {
        if (guid.IsEmpty)
            return null;
        if (_cache.TryGetValue(guid, out var cached))
            return cached;
        var document = Read(guid);
        _cache[guid] = document;
        return document;
    }

    /// <summary>The prefab's asset path, or null when the catalog does not know it.</summary>
    public string? GetPath(AssetGuid guid) => _project.Catalog.TryGetPath(guid, out var path) ? path : null;

    /// <summary>The display name of a prefab: its file name without extension.</summary>
    public string GetName(AssetGuid guid) => GetPath(guid) is { } path ? Path.GetFileNameWithoutExtension(path) : "Missing prefab";

    /// <summary>Forgets a cached prefab and announces the change, such as after the editor wrote its file.</summary>
    public void Invalidate(AssetGuid guid)
    {
        _cache.Remove(guid);
        Changed?.Invoke(this, new PrefabChangedEventArgs(guid));
    }

    /// <summary>Stores a prefab the editor just wrote, so it is not read back from disk.</summary>
    public void Store(AssetGuid guid, PrefabDocument document)
    {
        _cache[guid] = document;
        Changed?.Invoke(this, new PrefabChangedEventArgs(guid));
    }

    public void Dispose()
    {
        _disposed = true;
        if (_database is not null)
            _database.AssetsChanged -= OnAssetsChanged;
    }

    private void Hook()
    {
        if (_database is not null || _project.Database is not { } database)
            return;
        _database = database;
        database.AssetsChanged += OnAssetsChanged;
    }

    private void OnAssetsChanged(object? sender, AssetChangesEventArgs e)
    {
        var changed = e.Changes.Where(c => c.Kind == AssetKind.Prefab || AssetPath.GetExtension(c.Path).Equals(".tprefab", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Guid).Distinct().ToList();
        if (changed.Count == 0)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;
            foreach (var guid in changed)
            {
                _cache.Remove(guid);
                Changed?.Invoke(this, new PrefabChangedEventArgs(guid));
            }
        });
    }

    private PrefabDocument? Read(AssetGuid guid)
    {
        if (GetPath(guid) is not { } path)
            return null;
        try
        {
            var serializer = _project.EditSession?.Game.Services.GetService<DocumentSerializer>() ?? DocumentSerializer.Default;
            using var stream = File.OpenRead(_project.Project.ToAbsolutePath(path));
            return serializer.ReadPrefab(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException)
        {
            LogReadFailed(_logger, ex, path);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The prefab {Path} could not be read")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string path);
}

public sealed class PrefabChangedEventArgs(AssetGuid prefab) : EventArgs
{
    public AssetGuid Prefab { get; } = prefab;
}
