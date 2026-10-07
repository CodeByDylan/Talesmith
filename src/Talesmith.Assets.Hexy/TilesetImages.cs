using System.IO.Compression;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;

namespace Talesmith.Assets.Hexy;

/// <summary>A tileset image: the decoded texture, null when it could not be loaded, and the file a writer stores again.</summary>
internal readonly record struct TilesetImage(TextureAsset? Texture, TilesetImageFile File);

/// <summary>Loads tileset images from a map package, or relative to the map file when they are not packaged.</summary>
internal sealed class TilesetImages(AssetImportContext context, ZipArchive? package)
{
    private readonly Dictionary<string, TextureAsset> _packaged = new(StringComparer.Ordinal);

    /// <summary>Loads an image; a missing or invalid image logs a warning and yields no texture, but keeps its file reference.</summary>
    public async Task<TilesetImage> LoadAsync(string tilesetName, string image, string? source, CancellationToken cancellationToken)
    {
        if (package?.GetEntry(image) is { } entry)
        {
            var file = new TilesetImageFile(image, PackagedOpener(context.Source, context.Path, entry.FullName, entry.Crc32, entry.Length), source);
            return new TilesetImage(await DecodePackagedAsync(tilesetName, entry, cancellationToken).ConfigureAwait(false), file);
        }

        if (Path.IsPathRooted(image) || !TryResolve(image, out var path))
            return new TilesetImage(Missing(tilesetName, $"{image}, which is outside the asset folder"), new TilesetImageFile(image, () => throw Unavailable(image), source));

        var assetSource = context.Source;
        var imageFile = new TilesetImageFile(image, () => assetSource.OpenRead(path), source);
        if (!assetSource.Exists(path))
            return new TilesetImage(Missing(tilesetName, path), imageFile);

        try
        {
            return new TilesetImage(await context.Assets.LoadAsync<TextureAsset>(path, cancellationToken).ConfigureAwait(false), imageFile);
        }
        catch (AssetException ex)
        {
            return new TilesetImage(Missing(tilesetName, $"{path} ({ex.Message})"), imageFile);
        }
    }

    /// <summary>Reopens the package when the image is needed again; if the entry was renamed by a later save, finds it by checksum.</summary>
    private static Func<Stream> PackagedOpener(IAssetSource source, string mapPath, string entryName, uint crc, long length) => () =>
    {
        using var archive = new ZipArchive(source.OpenRead(mapPath), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) is { } named && named.Crc32 == crc && named.Length == length
            ? named
            : archive.Entries.FirstOrDefault(e => e.Crc32 == crc && e.Length == length) ?? throw Unavailable($"{entryName} in {mapPath}");
        var buffer = new MemoryStream((int)Math.Min(length, int.MaxValue));
        using (var stream = entry.Open())
            stream.CopyTo(buffer);
        buffer.Position = 0;
        return buffer;
    };

    private bool TryResolve(string image, out string path)
    {
        try
        {
            path = context.Resolve(image);
            return true;
        }
        catch (AssetException)
        {
            path = string.Empty;
            return false;
        }
    }

    private static FileNotFoundException Unavailable(string image) => new($"The tileset image {image} is no longer available.", image);

    private async Task<TextureAsset?> DecodePackagedAsync(string tilesetName, ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (_packaged.TryGetValue(entry.FullName, out var shared))
            return shared;

        var stream = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                var texture = new TextureAsset($"{context.Path}#{entry.FullName}", TextureDecoder.Decode(stream));
                _packaged.Add(entry.FullName, texture);
                return texture;
            }
            catch (AssetException ex)
            {
                return Missing(tilesetName, $"{entry.FullName} in the package ({ex.Message})");
            }
        }
    }

    private TextureAsset? Missing(string tilesetName, string location)
    {
        HexyLog.Warning(context.Logger, context.Path, $"The image of tileset \"{tilesetName}\" could not be loaded from {location}; its tiles are drawn in their colors.");
        return null;
    }
}
