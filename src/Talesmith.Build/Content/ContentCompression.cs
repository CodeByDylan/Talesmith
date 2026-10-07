using Talesmith.Assets;
using Talesmith.Assets.Packs;

namespace Talesmith.Build.Content;

/// <summary>Chooses how each asset is stored in the content pack.</summary>
public static class ContentCompression
{
    /// <summary>Formats that are compressed already, so compressing them again costs time and saves nothing.</summary>
    public static IReadOnlySet<string> PrecompressedExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".ogg", ".mp3", ".flac", ".zip", ".gz", ".br", ".woff2"
    };

    public static PackCompression For(string path, bool enabled) =>
        enabled && !PrecompressedExtensions.Contains(AssetPath.GetExtension(path)) ? PackCompression.Brotli : PackCompression.None;
}
