using Microsoft.Extensions.Logging;

namespace Talesmith.Assets;

/// <summary>Turns a file into a loaded asset of a specific type.</summary>
/// <remarks>
/// Register importers with dependency injection as <see cref="IAssetImporter"/>; the asset manager picks one by extension and asset type.
/// Importers that read the same files with the same settings, such as sound clips and music tracks, share an <see cref="Id"/>.
/// </remarks>
public interface IAssetImporter
{
    /// <summary>Lower-case extensions including the dot, such as ".png".</summary>
    IReadOnlyList<string> Extensions { get; }

    Type AssetType { get; }

    /// <summary>The importer id written to .meta files, such as "texture".</summary>
    string Id => AssetType.Name.ToLowerInvariant();

    /// <summary>Increases when the importer's output or settings change, so assets imported by older versions are imported again.</summary>
    int Version => 1;

    /// <summary>The type of the settings stored in .meta files, or null when the importer has none.</summary>
    Type? SettingsType => null;

    /// <summary>Imports an asset; called on a background thread.</summary>
    /// <exception cref="AssetException">The file is invalid.</exception>
    Task<object> ImportAsync(AssetImportContext context, CancellationToken cancellationToken);
}

/// <summary>A strongly typed base for importers.</summary>
public abstract class AssetImporter<T> : IAssetImporter where T : class
{
    public abstract IReadOnlyList<string> Extensions { get; }

    public Type AssetType => typeof(T);

    public virtual string Id => typeof(T).Name.ToLowerInvariant();

    public virtual int Version => 1;

    public virtual Type? SettingsType => null;

    async Task<object> IAssetImporter.ImportAsync(AssetImportContext context, CancellationToken cancellationToken) =>
        await ImportAsync(context, cancellationToken).ConfigureAwait(false);

    public abstract Task<T> ImportAsync(AssetImportContext context, CancellationToken cancellationToken);
}

/// <summary>A strongly typed base for importers with settings stored in .meta files.</summary>
public abstract class AssetImporter<T, TSettings> : AssetImporter<T> where T : class where TSettings : class, new()
{
    public sealed override Type SettingsType => typeof(TSettings);
}

/// <summary>What an importer can use while importing one asset.</summary>
/// <param name="Path">The asset's path relative to the asset root.</param>
/// <param name="Assets">Loads other assets this one depends on, such as a tileset's image; they stay loaded while this asset is.</param>
public sealed record AssetImportContext(string Path, IAssetSource Source, IAssetManager Assets, ILogger Logger)
{
    /// <summary>The asset's guid, or <see cref="AssetGuid.Empty"/> when it has no .meta file.</summary>
    public AssetGuid Guid => Meta?.Guid ?? AssetGuid.Empty;

    /// <summary>The asset's .meta information, or null when it has none.</summary>
    public AssetMeta? Meta { get; init; }

    /// <summary>Resolves guids written inside the asset; null when the game has no catalog.</summary>
    public IAssetCatalog? Catalog { get; init; }

    public Stream OpenRead() => Source.OpenRead(Path);

    /// <summary>Resolves a path written inside this asset, relative to this asset's folder, to an asset path.</summary>
    public string Resolve(string relativePath) => AssetPath.Combine(AssetPath.GetDirectory(Path), relativePath);

    /// <summary>The import settings from the asset's .meta file, or defaults when it has none.</summary>
    /// <exception cref="AssetException">The settings do not match <typeparamref name="TSettings"/>.</exception>
    public TSettings GetSettings<TSettings>() where TSettings : class, new() => Meta?.GetSettings<TSettings>() ?? new TSettings();

    /// <summary>Resolves an asset guid written inside this asset to its path.</summary>
    /// <exception cref="AssetException">No asset has the guid.</exception>
    public string ResolveGuid(AssetGuid guid)
    {
        if (Catalog is null)
            throw new AssetException($"'{Path}' refers to asset {guid}, but the game has no asset catalog to find it.");
        if (!Catalog.TryGetPath(guid, out var path))
            throw new AssetException($"'{Path}' refers to asset {guid}, which does not exist.");
        return path;
    }
}

/// <summary>An asset could not be loaded.</summary>
public sealed class AssetException(string message, Exception? innerException = null) : Exception(message, innerException);
