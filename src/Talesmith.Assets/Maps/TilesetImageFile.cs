namespace Talesmith.Assets.Maps;

/// <summary>The encoded image file of a tileset, which map writers store with the map.</summary>
/// <remarks>The bytes are read only when a map is saved, so loaded maps do not keep encoded images in memory.</remarks>
/// <param name="name">The image's path as the map stores it, such as <c>assets/terrain-6dac1961dc0d.png</c>; its extension names the format.</param>
/// <param name="open">Opens the encoded image for reading.</param>
/// <param name="source">The artwork the image was imported from, relative to the map's folder, which Hexy reloads artwork from; null when unknown.</param>
public sealed class TilesetImageFile(string name, Func<Stream> open, string? source = null)
{
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public string? Source { get; } = source;

    /// <summary>Creates a file read from disk; <paramref name="name"/> defaults to the file's name.</summary>
    public static TilesetImageFile FromFile(string path, string? name = null, string? source = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        return new TilesetImageFile(name ?? Path.GetFileName(fullPath),
            () => new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous), source);
    }

    /// <summary>Creates a file held in memory.</summary>
    public static TilesetImageFile FromBytes(string name, ReadOnlyMemory<byte> data, string? source = null) =>
        new(name, () => new MemoryStream(data.ToArray(), writable: false), source);

    /// <exception cref="IOException">The image is no longer available.</exception>
    public Stream Open() => open();

    public override string ToString() => Name;
}
