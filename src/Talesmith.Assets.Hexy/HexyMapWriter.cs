using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Hexy;

/// <summary>What happened while writing a map that did not stop it from being written.</summary>
public sealed record HexyWriteResult(IReadOnlyList<string> Warnings);

/// <summary>Writes maps in Hexy's native .hexy format, so maps edited in Talesmith open in Hexy and the other way around.</summary>
/// <remarks>
/// Writes a version 2 package exactly as Hexy does: <c>map.json</c>, then every tileset image once under <c>assets/</c>, named after
/// the tileset and a hash of the image. Fields and sections Talesmith does not model are written back as they were read, and values
/// that did not change keep their stored form, so an unchanged map gives the same <c>map.json</c>. Layer roles and tile collision
/// shapes are stored as custom properties, which Hexy keeps. Hexy has no isometric grids, so neither does this format.
/// </remarks>
public static class HexyMapWriter
{
    private const string DocumentEntry = "map.json";
    private const string AssetFolder = "assets/";

    /// <summary>Writes a map as a .hexy package.</summary>
    /// <remarks>The map is captured before this method first yields, so it may change while the package is written.</remarks>
    public static async Task<HexyWriteResult> WriteAsync(TileMap map, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(stream);
        var snapshot = HexySnapshot.Capture(map);
        var warnings = new List<string>();
        var (images, entries) = await CollectImagesAsync(snapshot, warnings, cancellationToken).ConfigureAwait(false);

        var document = new MemoryStream();
        await Task.Run(() => HexyJsonWriter.Write(document, snapshot, images), cancellationToken).ConfigureAwait(false);

        var archive = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken).ConfigureAwait(false);
        await using (archive.ConfigureAwait(false))
        {
            await WriteEntryAsync(archive, DocumentEntry, CompressionLevel.Optimal, document.GetBuffer().AsMemory(0, (int)document.Length), cancellationToken)
                .ConfigureAwait(false);
            foreach (var (name, data) in entries)
                await WriteEntryAsync(archive, name, CompressionLevel.NoCompression, data, cancellationToken).ConfigureAwait(false);
        }

        return new HexyWriteResult(warnings);
    }

    /// <summary>Saves a map to a file with <see cref="AtomicFile"/>, so the file is never left half-written.</summary>
    /// <remarks>The tileset images of a map loaded from <paramref name="filePath"/> are read from it before it is replaced.</remarks>
    public static async Task<HexyWriteResult> SaveAsync(TileMap map, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        HexyWriteResult? result = null;
        await AtomicFile.WriteAsync(filePath, async (stream, token) => result = await WriteAsync(map, stream, token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);
        return result!;
    }

    private static async Task<(Dictionary<int, string> Images, List<(string Name, ReadOnlyMemory<byte> Data)> Entries)> CollectImagesAsync(HexySnapshot map,
        List<string> warnings, CancellationToken cancellationToken)
    {
        var images = new Dictionary<int, string>();
        var entries = new List<(string Name, ReadOnlyMemory<byte> Data)>();
        var entryByFile = new Dictionary<TilesetImageFile, string>(ReferenceEqualityComparer.Instance);
        var entryByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tileset in map.Tilesets)
        {
            if (tileset.ImageFile is not { } file)
            {
                if (tileset.HasTexture)
                    warnings.Add($"Tileset \"{tileset.Name}\" has an image but no image file, so it was saved as a color tileset.");
                continue;
            }

            if (entryByFile.TryGetValue(file, out var known))
            {
                images[tileset.Id] = known;
                continue;
            }

            byte[] data;
            try
            {
                data = await ReadAllAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or AssetException)
            {
                warnings.Add($"Tileset \"{tileset.Name}\" image {file.Name} could not be read ({ex.Message}), so it was not packaged. The map keeps a reference to it.");
                images[tileset.Id] = file.Name;
                continue;
            }

            var hash = Convert.ToHexStringLower(SHA256.HashData(data))[..12];
            if (!entryByHash.TryGetValue(hash, out var entry))
            {
                entry = $"{AssetFolder}{Slug(tileset.Name)}-{hash}{Path.GetExtension(file.Name).ToLowerInvariant()}";
                entryByHash[hash] = entry;
                entries.Add((entry, data));
            }

            entryByFile[file] = entry;
            images[tileset.Id] = entry;
        }

        return (images, entries);
    }

    private static async Task<byte[]> ReadAllAsync(TilesetImageFile file, CancellationToken cancellationToken)
    {
        var stream = file.Open();
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string name, CompressionLevel level, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, level);
        var target = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (target.ConfigureAwait(false))
            await target.WriteAsync(data, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hexy's file name for a tileset: lower-case ASCII letters and digits joined by dashes, at most 40 characters.</summary>
    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormKD))
        {
            if (char.IsAsciiLetterOrDigit(c))
                builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            else if (builder.Length > 0 && builder[^1] != '-' && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "tileset" : slug[..Math.Min(slug.Length, 40)];
    }
}
