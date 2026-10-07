namespace Talesmith.Assets;

/// <summary>Where asset files come from, addressed by paths relative to the asset root with forward slashes.</summary>
public interface IAssetSource
{
    /// <summary>A description of the source for messages, such as its folder.</summary>
    string Description { get; }

    bool Exists(string path);

    /// <exception cref="FileNotFoundException">The asset does not exist.</exception>
    Stream OpenRead(string path);

    /// <summary>Lists asset paths in a folder, optionally recursively, matching a pattern such as "*.hexy".</summary>
    IEnumerable<string> List(string folder, string pattern = "*", bool recursive = false);

    /// <summary>Gets the file system path of an asset, or null when the source is not a folder on disk.</summary>
    string? GetFullPath(string path);
}
