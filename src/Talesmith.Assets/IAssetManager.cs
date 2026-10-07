namespace Talesmith.Assets;

/// <summary>Loads and caches assets by path or guid, counts references to them and reloads them when their files change.</summary>
/// <remarks>
/// <para>Each path is imported once and shared; concurrent loads of the same path share one import. Loading is asynchronous and runs
/// importers on background threads, so scenes can stream assets while the game keeps running. Paths are relative to the asset root
/// and use forward slashes, such as "maps/island.hexy".</para>
/// <para>Reference counting: every successful load adds a reference and <see cref="Release"/> removes one; the asset is unloaded when
/// the count reaches zero. Assets an importer loads while importing (such as a map's tileset images) are referenced by the asset that
/// loaded them and released with it. <see cref="TryGet{T}(string, out T)"/> adds no reference, and <see cref="Unload"/> unloads
/// regardless of the count. Code that never releases keeps assets cached for the whole game.</para>
/// </remarks>
public interface IAssetManager
{
    IAssetSource Source { get; }

    /// <summary>Resolves asset guids to paths; null when the game has no catalog.</summary>
    IAssetCatalog? Catalog { get; }

    /// <exception cref="AssetException">The asset does not exist, no importer handles it, or importing failed.</exception>
    Task<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default) where T : class;

    /// <exception cref="AssetException">No asset has the guid, no importer handles it, or importing failed.</exception>
    Task<T> LoadAsync<T>(AssetGuid guid, CancellationToken cancellationToken = default) where T : class;

    /// <summary>Loads an asset synchronously, blocking the caller; prefer <see cref="LoadAsync{T}(string, CancellationToken)"/> during gameplay.</summary>
    T Load<T>(string path) where T : class;

    /// <summary>Loads an asset by guid synchronously, blocking the caller.</summary>
    T Load<T>(AssetGuid guid) where T : class;

    /// <summary>Gets an asset that has already finished loading, without adding a reference.</summary>
    bool TryGet<T>(string path, out T asset) where T : class;

    /// <summary>Removes one reference to a loaded asset, unloading it at zero. Disposable assets are disposed.</summary>
    /// <returns>False when the instance did not come from this manager or was already unloaded.</returns>
    bool Release(object asset);

    /// <summary>Forgets a loaded asset regardless of its references so the next load imports it again. Disposable assets are disposed.</summary>
    void Unload(string path);

    /// <summary>Whether an asset at the path is loaded or loading, as any type.</summary>
    bool IsLoaded(string path);

    /// <summary>The number of references to the assets loaded from a path, for diagnostics.</summary>
    int GetReferenceCount(string path);

    /// <summary>Imports the loaded assets at a path again, and then the loaded assets that depend on them.</summary>
    /// <remarks>
    /// The new instance replaces the old one in the cache, keeping its references, and an <see cref="AssetReloaded"/> event is queued on
    /// the game's event bus for each replaced asset. When importing fails the old instance stays and the error is logged.
    /// </remarks>
    /// <returns>False when nothing was loaded from the path or importing failed.</returns>
    Task<bool> ReloadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Keeps assets cached after their file, or a folder containing them, moved from <paramref name="oldPath"/> to <paramref name="newPath"/>.</summary>
    void NotifyMoved(string oldPath, string newPath);

    /// <summary>Gets the path an asset instance was loaded from.</summary>
    bool TryGetPath(object asset, out string path);

    /// <summary>Gets the guid of the file an asset instance was loaded from, or <see cref="AssetGuid.Empty"/>.</summary>
    AssetGuid GetGuid(object asset);

    /// <summary>The number of loaded assets.</summary>
    int LoadedCount { get; }
}

/// <summary>Raised on the game thread after an asset was imported, the first time or again for a reload.</summary>
public readonly record struct AssetImported(string Path, AssetGuid Guid, Type AssetType, object Asset, bool IsReload, TimeSpan Duration);

/// <summary>Raised on the game thread after a loaded asset was imported again and replaced in the cache.</summary>
/// <remarks>Holders of <see cref="OldAsset"/> switch to <see cref="NewAsset"/>. Disposable old assets are disposed after every handler ran.</remarks>
public readonly record struct AssetReloaded(string Path, AssetGuid Guid, Type AssetType, object OldAsset, object NewAsset);
