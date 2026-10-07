using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Database.Dependencies;

namespace Talesmith.Assets.Database;

/// <summary>The editor's and build's view of a project's assets: their guids, .meta files, content hashes, dependencies and problems.</summary>
/// <remarks>
/// <para><b>Lifecycle.</b> Call <see cref="ScanAsync"/> once; it creates missing .meta files, gives copied assets new guids, upgrades
/// importer versions and fills <see cref="Catalog"/>. Then <see cref="StartWatching"/> keeps the database current as files change on
/// disk; changes are debounced and reported as one <see cref="AssetsChanged"/> batch, where a delete and a create of a file with the
/// same .meta guid (or, without a .meta file, the same contents) are paired into a move.</para>
/// <para><b>Operations.</b> <see cref="CreateFolderAsync"/>, <see cref="MoveAsync"/>, <see cref="RenameAsync"/>,
/// <see cref="DuplicateAsync"/>, <see cref="DeleteAsync"/> and <see cref="UpdateMetaAsync"/> change files together with their .meta
/// files. Deleted assets go to <see cref="AssetDatabaseOptions.TrashFolder"/>, in a folder per deletion named after its time, so they
/// can be restored by hand; without a trash folder they are deleted permanently. .meta files are written atomically.</para>
/// <para><b>Threading.</b> Every method is thread-safe. Scans and operations run one at a time on the thread pool and never block
/// the caller. Reads return snapshots. <see cref="AssetsChanged"/> and <see cref="ScanProgressChanged"/> are raised on thread pool
/// threads, after the database has been updated; editors marshal them to their UI thread.</para>
/// <para><b>Incremental imports.</b> Each record has an <see cref="AssetRecord.ImportStamp"/> derived from its contents, importer, importer
/// version and settings; <see cref="AssetRecord.NeedsImport"/> compares it with the stamp passed to <see cref="MarkImportedAsync"/>.
/// Hashes, stamps and references are cached in <see cref="AssetDatabaseOptions.CacheFolder"/>, so unchanged files are not read again.</para>
/// </remarks>
public sealed partial class AssetDatabase : IAsyncDisposable, IDisposable
{
    private static readonly HashSet<string> IgnoredNames = new(["Thumbs.db", "desktop.ini"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<AssetKind> RootKinds =
    [
        AssetKind.Scene, AssetKind.Script, AssetKind.PluginManifest, AssetKind.Localization, AssetKind.Folder, AssetKind.Other
    ];

    private readonly AssetDatabaseOptions _options;
    private readonly ILogger<AssetDatabase> _logger;
    private readonly IAssetImporter[] _importers;
    private readonly IAssetDependencyExtractor[] _extractors;
    private readonly string[] _excludedFolders;
    private readonly string? _scriptsFolder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _stateLock = new();
    private readonly Dictionary<AssetGuid, AssetRecord> _records = new();
    private readonly Dictionary<string, AssetGuid> _byPath = new(AssetPath.Comparer);
    private readonly HashSet<string> _orphanedMetas = new(AssetPath.Comparer);
    private readonly Dictionary<string, InvalidMeta> _invalidMetas = new(AssetPath.Comparer);
    private readonly List<DuplicateGuidRepair> _repairs = [];
    private Dictionary<string, CachedAsset> _cache = new(AssetPath.Comparer);
    private AssetDependencyGraph? _graph;
    private bool _scanned;
    private bool _disposed;

    /// <exception cref="DirectoryNotFoundException">The asset folder does not exist.</exception>
    public AssetDatabase(
        AssetDatabaseOptions options,
        IEnumerable<IAssetImporter> importers,
        ILogger<AssetDatabase> logger,
        IEnumerable<IAssetDependencyExtractor>? extractors = null,
        AssetKindRegistry? kinds = null,
        AssetCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(importers);
        ArgumentNullException.ThrowIfNull(logger);
        RootFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.AssetRoot));
        if (!Directory.Exists(RootFolder))
            throw new DirectoryNotFoundException($"The asset folder '{RootFolder}' does not exist.");

        _options = options;
        _logger = logger;
        _importers = [.. importers];
        _extractors = [.. extractors ?? DefaultDependencyExtractors.Create()];
        Kinds = kinds ?? AssetKindRegistry.Default;
        Catalog = catalog ?? new AssetCatalog();
        var plugins = options.PluginsFolder is null ? null : Path.Combine(RootFolder, options.PluginsFolder);
        _excludedFolders = [.. new[] { options.CacheFolder, options.TrashFolder, plugins }.OfType<string>().Select(ToRelativeOrNull).OfType<string>().Where(path => path.Length > 0)];
        _scriptsFolder = options.ScriptsFolder is null ? null : ToRelativeOrNull(Path.Combine(RootFolder, options.ScriptsFolder));
    }

    /// <summary>Raised after assets were added, changed, moved or deleted, with every change found together.</summary>
    public event EventHandler<AssetChangesEventArgs>? AssetsChanged;

    /// <summary>Raised while a scan reads files.</summary>
    public event EventHandler<AssetScanProgress>? ScanProgressChanged;

    /// <summary>The absolute path of the asset folder.</summary>
    public string RootFolder { get; }

    /// <summary>The guids, paths and metas of every asset, kept current; games run from the editor can share it.</summary>
    public AssetCatalog Catalog { get; }

    public AssetKindRegistry Kinds { get; }

    /// <summary>Whether the first scan has completed.</summary>
    public bool IsScanned
    {
        get
        {
            lock (_stateLock)
                return _scanned;
        }
    }

    /// <summary>A snapshot of every asset and folder, ordered by path.</summary>
    public IReadOnlyList<AssetRecord> Assets
    {
        get
        {
            lock (_stateLock)
                return [.. _records.Values.OrderBy(record => record.Path, StringComparer.Ordinal)];
        }
    }

    public bool TryGetAsset(AssetGuid guid, [NotNullWhen(true)] out AssetRecord? record)
    {
        lock (_stateLock)
            return _records.TryGetValue(guid, out record);
    }

    public bool TryGetAsset(string path, [NotNullWhen(true)] out AssetRecord? record)
    {
        var normalized = AssetPath.Normalize(path);
        lock (_stateLock)
        {
            record = null;
            return _byPath.TryGetValue(normalized, out var guid) && _records.TryGetValue(guid, out record);
        }
    }

    /// <summary>The assets and folders directly inside a folder; "" is the asset root.</summary>
    public IReadOnlyList<AssetRecord> GetChildren(string folder)
    {
        var normalized = AssetPath.Normalize(folder);
        lock (_stateLock)
        {
            return
            [
                .. _records.Values
                    .Where(record => AssetPath.Comparer.Equals(AssetPath.GetDirectory(record.Path), normalized) && record.Path.Length > 0)
                    .OrderBy(record => record.IsFolder ? 0 : 1)
                    .ThenBy(record => record.Path, StringComparer.OrdinalIgnoreCase)
            ];
        }
    }

    /// <summary>The current dependency graph.</summary>
    public AssetDependencyGraph GetDependencyGraph()
    {
        lock (_stateLock)
            return _graph ??= new AssetDependencyGraph([.. _records.Values], ResolvePathLocked, _records.ContainsKey);
    }

    /// <summary>The assets <paramref name="guid"/> refers to directly.</summary>
    public IReadOnlyList<AssetGuid> GetDependencies(AssetGuid guid) => GetDependencyGraph().GetDependencies(guid);

    /// <summary>The assets that refer to <paramref name="guid"/>, directly or, when <paramref name="transitive"/>, through other assets.</summary>
    public IReadOnlyList<AssetGuid> GetDependents(AssetGuid guid, bool transitive = false) => GetDependencyGraph().GetDependents(guid, transitive);

    /// <summary>Missing references, orphaned .meta files, unreferenced assets, repaired guid conflicts and unreadable .meta files.</summary>
    public AssetReport GetReport()
    {
        var graph = GetDependencyGraph();
        lock (_stateLock)
        {
            var unreferenced = _records.Values
                .Where(record => !RootKinds.Contains(record.Kind) && !graph.IsReferenced(record.Guid))
                .OrderBy(record => record.Path, StringComparer.Ordinal)
                .ToArray();
            return new AssetReport(
                graph.MissingReferences,
                [.. _orphanedMetas.Select(AssetMetaFile.GetMetaPath).Order(StringComparer.Ordinal)],
                unreferenced,
                [.. _repairs],
                [.. _invalidMetas.Values.OrderBy(meta => meta.Path, StringComparer.Ordinal)]);
        }
    }

    /// <summary>An index of every asset for a build, which games load instead of the .meta files.</summary>
    public AssetIndex CreateIndex() => new(Catalog.Entries);

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        await StopWatchingAsync().ConfigureAwait(false);
        await FlushCacheAsync().ConfigureAwait(false);
        _saveTimer?.Dispose();
        _gate.Dispose();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private AssetGuid ResolvePathLocked(string path) => _byPath.GetValueOrDefault(path);

    private IAssetImporter? FindImporter(string path)
    {
        var extension = AssetPath.GetExtension(path);
        for (var i = _importers.Length - 1; i >= 0; i--)
        {
            if (_importers[i].Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return _importers[i];
        }

        return null;
    }

    private IAssetDependencyExtractor? FindExtractor(string path)
    {
        for (var i = _extractors.Length - 1; i >= 0; i--)
        {
            if (_extractors[i].CanExtract(path))
                return _extractors[i];
        }

        return null;
    }

    private string ToFullPath(string path) =>
        path.Length == 0 ? RootFolder : Path.Combine(RootFolder, path.Replace('/', Path.DirectorySeparatorChar));

    private string? ToRelativeOrNull(string fullPath)
    {
        var relative = Path.GetRelativePath(RootFolder, Path.GetFullPath(fullPath));
        if (relative == ".")
            return string.Empty;
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
            return null;
        return relative.Replace('\\', '/');
    }

    private static bool IsIgnoredName(string name) =>
        name.Length == 0 || name[0] == '.' || name[^1] == '~' || IgnoredNames.Contains(name) || AssetMetaFile.IsMetaPath(name);

    /// <summary>Whether a path is not an asset: hidden, temporary, a .meta file, the build index, script build output, or inside the cache, trash or
    /// plugins folder.</summary>
    private bool IsIgnoredPath(string path)
    {
        if (path.Length == 0)
            return true;
        if (AssetPath.Comparer.Equals(path, AssetIndex.FileName))
            return true;
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            if (IsIgnoredName(segment))
                return true;
        }

        if (_scriptsFolder is not null && AssetPath.IsWithin(path, _scriptsFolder))
        {
            for (var i = _scriptsFolder.Length == 0 ? 0 : _scriptsFolder.Count(c => c == '/') + 1; i < segments.Length; i++)
            {
                if (segments[i] is "bin" or "obj")
                    return true;
            }
        }

        foreach (var excluded in _excludedFolders)
        {
            if (AssetPath.IsWithin(path, excluded))
                return true;
        }

        return false;
    }

    private void Raise(List<AssetChange> changes)
    {
        if (changes.Count == 0)
            return;

        try
        {
            AssetsChanged?.Invoke(this, new AssetChangesEventArgs(changes));
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex);
        }
    }

    private void ReportProgress(AssetScanProgress progress, IProgress<AssetScanProgress>? listener)
    {
        listener?.Report(progress);
        try
        {
            ScanProgressChanged?.Invoke(this, progress);
        }
        catch (Exception ex)
        {
            LogHandlerFailed(ex);
        }
    }

    private void EnsureScanned()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsScanned)
            throw new InvalidOperationException("Scan the asset database before changing assets.");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "An asset database event handler failed")]
    private partial void LogHandlerFailed(Exception exception);
}
