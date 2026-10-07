using System.Diagnostics.CodeAnalysis;

namespace Talesmith.Assets;

/// <summary>Provides the .meta information of assets, such as their import settings.</summary>
public interface IAssetMetaProvider
{
    bool TryGetMeta(string path, [NotNullWhen(true)] out AssetMeta? meta);
}

/// <summary>An asset or folder known to an <see cref="AssetCatalog"/>.</summary>
public sealed record AssetCatalogEntry(string Path, AssetMeta Meta)
{
    public AssetGuid Guid => Meta.Guid;
}

/// <summary>The default <see cref="IAssetCatalog"/>: guids, paths and metas of every asset, kept in memory.</summary>
/// <remarks>
/// <para>Games build it with <see cref="Load"/> from the build's <see cref="AssetIndex"/>, or from the .meta files when there is no
/// index. The editor's asset database keeps its own catalog up to date as files change.</para>
/// <para>Thread-safe. When two paths claim the same guid, the first one added keeps it. <see cref="Changed"/> is raised on the thread
/// that made the change, once per change or once per <see cref="BeginUpdate"/> block.</para>
/// </remarks>
public sealed class AssetCatalog : IAssetCatalog, IAssetMetaProvider
{
    private readonly Dictionary<string, AssetMeta> _byPath = new(AssetPath.Comparer);
    private readonly Dictionary<AssetGuid, string> _byGuid = new();
    private readonly Lock _lock = new();
    private int _updateDepth;
    private bool _changedDuringUpdate;

    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_lock)
                return _byPath.Count;
        }
    }

    /// <summary>A snapshot of every entry, ordered by path.</summary>
    public IReadOnlyList<AssetCatalogEntry> Entries
    {
        get
        {
            lock (_lock)
                return [.. _byPath.Select(pair => new AssetCatalogEntry(pair.Key, pair.Value)).OrderBy(entry => entry.Path, StringComparer.Ordinal)];
        }
    }

    /// <summary>Builds a catalog from the asset index of a build when the source has one, and from the .meta files otherwise.</summary>
    /// <remarks>Reads files synchronously; games call it once at startup.</remarks>
    /// <exception cref="AssetException">The index file is invalid.</exception>
    public static AssetCatalog Load(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var catalog = new AssetCatalog();
        if (source.Exists(AssetIndex.FileName))
        {
            using var stream = source.OpenRead(AssetIndex.FileName);
            catalog.AddRange(AssetIndex.Read(stream).Entries);
            return catalog;
        }

        var entries = new List<AssetCatalogEntry>();
        foreach (var metaPath in source.List(string.Empty, "*" + AssetMetaFile.Extension, recursive: true).Where(AssetMetaFile.IsMetaPath).Order(StringComparer.Ordinal))
        {
            try
            {
                using var stream = source.OpenRead(metaPath);
                entries.Add(new AssetCatalogEntry(AssetPath.Normalize(AssetMetaFile.GetAssetPath(metaPath)), AssetMetaFile.Read(stream)));
            }
            catch (AssetException)
            {
                // A damaged meta only hides its asset from guid lookups; the asset database reports and repairs it.
            }
        }

        catalog.AddRange(entries);
        return catalog;
    }

    public bool TryGetPath(AssetGuid guid, [NotNullWhen(true)] out string? path)
    {
        lock (_lock)
            return _byGuid.TryGetValue(guid, out path);
    }

    public bool TryGetGuid(string path, out AssetGuid guid)
    {
        var normalized = AssetPath.Normalize(path);
        lock (_lock)
        {
            guid = _byPath.TryGetValue(normalized, out var meta) ? meta.Guid : AssetGuid.Empty;
            return !guid.IsEmpty;
        }
    }

    public bool TryGetMeta(string path, [NotNullWhen(true)] out AssetMeta? meta)
    {
        var normalized = AssetPath.Normalize(path);
        lock (_lock)
            return _byPath.TryGetValue(normalized, out meta);
    }

    public bool TryGetMeta(AssetGuid guid, [NotNullWhen(true)] out AssetMeta? meta)
    {
        lock (_lock)
        {
            meta = null;
            return _byGuid.TryGetValue(guid, out var path) && _byPath.TryGetValue(path, out meta);
        }
    }

    /// <summary>Adds an entry or replaces the meta at a path.</summary>
    /// <returns>False when another path already has the guid, in which case nothing changes.</returns>
    public bool Set(string path, AssetMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        var normalized = AssetPath.Normalize(path);
        lock (_lock)
        {
            if (!SetLocked(normalized, meta))
                return false;
        }

        OnChanged();
        return true;
    }

    /// <summary>Removes the entry at a path, if any.</summary>
    public bool Remove(string path)
    {
        var normalized = AssetPath.Normalize(path);
        lock (_lock)
        {
            if (!_byPath.Remove(normalized, out var meta))
                return false;
            _byGuid.Remove(meta.Guid);
        }

        OnChanged();
        return true;
    }

    /// <summary>Moves an entry, and every entry inside it when it is a folder, to a new path.</summary>
    public bool Move(string oldPath, string newPath)
    {
        var from = AssetPath.Normalize(oldPath);
        var to = AssetPath.Normalize(newPath);
        lock (_lock)
        {
            var moved = _byPath.Where(pair => AssetPath.IsWithin(pair.Key, from)).ToList();
            if (moved.Count == 0)
                return false;

            foreach (var (path, _) in moved)
                _byPath.Remove(path);
            foreach (var (path, meta) in moved)
            {
                var target = to + path[from.Length..];
                _byPath[target] = meta;
                _byGuid[meta.Guid] = target;
            }
        }

        OnChanged();
        return true;
    }

    /// <summary>Replaces every entry.</summary>
    public void Reset(IEnumerable<AssetCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_lock)
        {
            _byPath.Clear();
            _byGuid.Clear();
            foreach (var entry in entries)
                SetLocked(AssetPath.Normalize(entry.Path), entry.Meta);
        }

        OnChanged();
    }

    /// <summary>Defers <see cref="Changed"/> until the returned scope is disposed, so a batch of edits raises it once.</summary>
    public IDisposable BeginUpdate()
    {
        lock (_lock)
            _updateDepth++;
        return new UpdateScope(this);
    }

    private void AddRange(IEnumerable<AssetCatalogEntry> entries)
    {
        lock (_lock)
        {
            foreach (var entry in entries)
                SetLocked(AssetPath.Normalize(entry.Path), entry.Meta);
        }
    }

    private bool SetLocked(string path, AssetMeta meta)
    {
        if (_byGuid.TryGetValue(meta.Guid, out var existing) && !AssetPath.Comparer.Equals(existing, path))
            return false;
        if (_byPath.TryGetValue(path, out var previous) && previous.Guid != meta.Guid)
            _byGuid.Remove(previous.Guid);

        _byPath[path] = meta;
        _byGuid[meta.Guid] = path;
        return true;
    }

    private void OnChanged()
    {
        lock (_lock)
        {
            if (_updateDepth > 0)
            {
                _changedDuringUpdate = true;
                return;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EndUpdate()
    {
        bool raise;
        lock (_lock)
        {
            raise = --_updateDepth == 0 && _changedDuringUpdate;
            if (raise)
                _changedDuringUpdate = false;
        }

        if (raise)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class UpdateScope(AssetCatalog catalog) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                catalog.EndUpdate();
        }
    }
}
