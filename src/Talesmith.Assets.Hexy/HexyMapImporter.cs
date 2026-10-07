using System.IO.Compression;
using System.Text.Json;
using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Hexy;

/// <summary>Imports maps saved by the Hexy editor as <see cref="TileMap"/>s.</summary>
/// <remarks>
/// Reads version 2 packages, whose tileset images are decoded straight from the package, and version 1 JSON files, whose images are
/// loaded through the asset manager relative to the map. Tile chunks stay compressed until the game needs them. Missing images are
/// reported as warnings and their tiles fall back to their colors.
/// </remarks>
public sealed class HexyMapImporter : AssetImporter<TileMap>
{
    private const string DocumentEntry = "map.json";

    public override IReadOnlyList<string> Extensions { get; } = [".hexy"];

    public override async Task<TileMap> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stream = await OpenSeekableAsync(context, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            if (!await IsPackageAsync(stream, cancellationToken).ConfigureAwait(false))
                return await ReadAsync(context, stream, package: null, cancellationToken).ConfigureAwait(false);

            ZipArchive package;
            try
            {
                package = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: null, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                throw new AssetException($"The map package is damaged: {ex.Message}", ex);
            }

            await using (package.ConfigureAwait(false))
            {
                var entry = package.GetEntry(DocumentEntry) ?? throw new AssetException($"The map package has no {DocumentEntry}.");
                var json = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using (json.ConfigureAwait(false))
                    return await ReadAsync(context, json, package, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<TileMap> ReadAsync(AssetImportContext context, Stream json, ZipArchive? package, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await json.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var newLine = NewLineOf(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
        buffer.Position = 0;
        MapDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync(buffer, HexyJsonContext.Default.MapDocument, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new AssetException($"The file is not a valid Hexy map: {ex.Message}", ex);
        }

        HexyMapReader.Validate(document);
        var tilesets = document.Tilesets ?? [];
        var loader = new TilesetImages(context, package);
        var images = new TilesetImage?[tilesets.Count];
        for (var i = 0; i < tilesets.Count; i++)
        {
            if (tilesets[i].Image is not { Length: > 0 } image)
                continue;
            var source = tilesets[i].Source is { Length: > 0 } stored ? stored : document.Version == 1 ? image : null;
            images[i] = await loader.LoadAsync(tilesets[i].Name, image, source, cancellationToken).ConfigureAwait(false);
        }

        return new HexyMapReader(context).Read(document, images, newLine);
    }

    /// <summary>The line ending of a JSON document, or null when it has no line breaks.</summary>
    /// <remarks>JSON strings cannot hold a raw line break, so the first one is whitespace between tokens.</remarks>
    private static string? NewLineOf(ReadOnlySpan<byte> json) =>
        json.IndexOf((byte)'\n') switch
        {
            < 0 => null,
            > 0 and var index when json[index - 1] == '\r' => "\r\n",
            _ => "\n"
        };

    private static async Task<Stream> OpenSeekableAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        var stream = context.OpenRead();
        if (stream.CanSeek)
            return stream;

        await using (stream.ConfigureAwait(false))
        {
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
    }

    private static async Task<bool> IsPackageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var start = stream.Position;
        var header = new byte[4];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        stream.Position = start;
        return read == header.Length && header is [(byte)'P', (byte)'K', 3, 4];
    }
}
