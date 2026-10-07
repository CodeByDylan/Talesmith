namespace Talesmith.Assets;

/// <summary>Normalizes asset paths: forward slashes, no leading slash, and "." and ".." segments resolved.</summary>
public static class AssetPath
{
    /// <summary>Compares asset paths the way the file system does: ignoring case on Windows, exactly elsewhere.</summary>
    public static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The <see cref="StringComparison"/> matching <see cref="Comparer"/>.</summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Normalize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new AssetException($"The path '{path}' leaves the asset folder.");
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    public static string Combine(string folder, string path) => Normalize(folder.Length == 0 ? path : $"{folder}/{path}");

    public static string GetDirectory(string path)
    {
        var normalized = Normalize(path);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    public static string GetExtension(string path) => Path.GetExtension(path).ToLowerInvariant();

    /// <summary>The last segment of a path, such as "hero.png" for "sprites/hero.png".</summary>
    public static string GetFileName(string path)
    {
        var normalized = Normalize(path);
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    /// <summary>The last segment of a path without its extension, such as "hero" for "sprites/hero.png".</summary>
    public static string GetFileNameWithoutExtension(string path) => Path.GetFileNameWithoutExtension(GetFileName(path));

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it; both must be normalized.</summary>
    public static bool IsWithin(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Length == 0)
            return true;
        return path.StartsWith(folder, Comparison) && (path.Length == folder.Length || path[folder.Length] == '/');
    }
}
