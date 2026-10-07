using System.Text.Json;
using Talesmith.Assets.Json;

namespace Talesmith.Assets;

/// <summary>The index of every asset that a build writes to the root of the shipped asset folder, replacing the .meta files.</summary>
/// <remarks>
/// <para>The file is <see cref="FileName"/>, a JSON document whose entries carry what the .meta files would:</para>
/// <code>
/// {
///   "version": 1,
///   "assets": [
///     { "path": "sprites/hero.png", "guid": "9a2c…", "importer": "texture", "importerVersion": 1, "settings": { … } },
///     { "path": "sprites", "guid": "41d0…", "folder": true }
///   ]
/// }
/// </code>
/// </remarks>
public sealed class AssetIndex
{
    public const string FileName = "assets.index.json";

    public const int CurrentVersion = 1;

    public AssetIndex(IEnumerable<AssetCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Entries = [.. entries.OrderBy(entry => entry.Path, StringComparer.Ordinal)];
    }

    public IReadOnlyList<AssetCatalogEntry> Entries { get; }

    /// <exception cref="AssetException">The stream is not a valid index.</exception>
    public static AssetIndex Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var document = AssetJson.Deserialize<IndexDocument>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), "The asset index");
        if (document.Version > CurrentVersion)
            throw new AssetException($"The asset index has version {document.Version}, but this version of Talesmith reads up to {CurrentVersion}.");

        var entries = new List<AssetCatalogEntry>();
        foreach (var asset in document.Assets ?? [])
        {
            if (string.IsNullOrWhiteSpace(asset.Path))
                throw new AssetException("An entry of the asset index has no path.");
            if (!AssetGuid.TryParse(asset.Guid, null, out var guid) || guid.IsEmpty)
                throw new AssetException($"The asset index entry '{asset.Path}' has no valid guid.");

            entries.Add(new AssetCatalogEntry(AssetPath.Normalize(asset.Path), new AssetMeta(guid)
            {
                IsFolder = asset.Folder ?? false,
                Importer = asset.Importer,
                ImporterVersion = asset.ImporterVersion ?? 0,
                Settings = asset.Settings is { ValueKind: JsonValueKind.Object } settings ? settings.Clone() : null
            }));
        }

        return new AssetIndex(entries);
    }

    public byte[] ToUtf8Bytes()
    {
        var document = new IndexDocument
        {
            Version = CurrentVersion,
            Assets =
            [
                .. Entries.Select(entry => new IndexEntry
                {
                    Path = entry.Path,
                    Guid = entry.Guid.ToString(),
                    Folder = entry.Meta.IsFolder ? true : null,
                    Importer = entry.Meta.Importer,
                    ImporterVersion = entry.Meta.Importer is null ? null : entry.Meta.ImporterVersion,
                    Settings = entry.Meta.Settings
                })
            ]
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, AssetJson.Options);
    }

    /// <summary>Writes the index into a folder, normally the root of a build's asset folder.</summary>
    public Task WriteAsync(string folder, CancellationToken cancellationToken = default) =>
        AtomicFile.WriteAllBytesAsync(Path.Combine(folder, FileName), ToUtf8Bytes(), cancellationToken);

    private sealed class IndexDocument
    {
        public int Version { get; set; }
        public List<IndexEntry>? Assets { get; set; }
    }

    private sealed class IndexEntry
    {
        public string? Path { get; set; }
        public string? Guid { get; set; }
        public bool? Folder { get; set; }
        public string? Importer { get; set; }
        public int? ImporterVersion { get; set; }
        public JsonElement? Settings { get; set; }
    }
}
