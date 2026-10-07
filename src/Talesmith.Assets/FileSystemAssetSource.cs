namespace Talesmith.Assets;

/// <summary>Reads assets from a folder on disk.</summary>
/// <remarks>Paths can never reach files outside the folder. Whether names are case-sensitive follows the operating system.</remarks>
public sealed class FileSystemAssetSource : IAssetSource
{
    private readonly string _rootWithSeparator;

    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public FileSystemAssetSource(string rootFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);
        RootFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootFolder));
        if (!Directory.Exists(RootFolder))
            throw new DirectoryNotFoundException($"The asset folder '{RootFolder}' does not exist.");
        _rootWithSeparator = Path.EndsInDirectorySeparator(RootFolder) ? RootFolder : RootFolder + Path.DirectorySeparatorChar;
    }

    /// <summary>The absolute path of the asset folder.</summary>
    public string RootFolder { get; }

    public string Description => RootFolder;

    public bool Exists(string path) => File.Exists(ToFullPath(path));

    public Stream OpenRead(string path)
    {
        var fullPath = ToFullPath(path);
        try
        {
            return new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, useAsync: true);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new FileNotFoundException($"The asset '{path}' does not exist in {RootFolder}.", fullPath, ex);
        }
    }

    public IEnumerable<string> List(string folder, string pattern = "*", bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        if (pattern.AsSpan().ContainsAny('/', '\\'))
            throw new ArgumentException("The pattern must match file names only, without folders.", nameof(pattern));

        var directory = ToFullPath(folder);
        if (!Directory.Exists(directory))
            return [];

        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, MatchCasing = MatchCasing.PlatformDefault };
        return Directory.EnumerateFiles(directory, pattern, options)
            .Select(file => Path.GetRelativePath(RootFolder, file).Replace('\\', '/'));
    }

    public string? GetFullPath(string path) => ToFullPath(path);

    private string ToFullPath(string path)
    {
        var normalized = AssetPath.Normalize(path);
        if (normalized.Length == 0)
            return RootFolder;

        var fullPath = Path.GetFullPath(Path.Combine(RootFolder, normalized));
        if (!fullPath.StartsWith(_rootWithSeparator, AssetPath.Comparison))
            throw new AssetException($"The path '{path}' leaves the asset folder.");
        return fullPath;
    }
}
