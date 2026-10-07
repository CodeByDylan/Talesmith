namespace Talesmith.Assets.Database.Dependencies;

/// <summary>Finds the assets a file refers to, for the asset database's dependency graph.</summary>
/// <remarks>
/// Register extractors with dependency injection as <see cref="IAssetDependencyExtractor"/>; for each file the last registered extractor
/// that handles it is used. Extractors run on background threads and must not throw for malformed files; they return what they found.
/// </remarks>
public interface IAssetDependencyExtractor
{
    bool CanExtract(string path);

    Task<IReadOnlyList<AssetReference>> ExtractAsync(AssetDependencyContext context, CancellationToken cancellationToken);
}

/// <summary>The file an <see cref="IAssetDependencyExtractor"/> reads.</summary>
/// <param name="Path">The asset path relative to the asset root.</param>
/// <param name="FullPath">The file's location on disk.</param>
public sealed record AssetDependencyContext(string Path, string FullPath)
{
    public Stream OpenRead() => new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous);

    /// <summary>Resolves a path written inside the file, relative to its folder, to an asset path; null when it leaves the asset folder.</summary>
    public string? Resolve(string relativePath)
    {
        try
        {
            return AssetPath.Combine(AssetPath.GetDirectory(Path), relativePath);
        }
        catch (AssetException)
        {
            return null;
        }
    }
}
