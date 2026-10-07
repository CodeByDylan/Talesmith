using System.Text.Json;
using Talesmith.Assets.Json;

namespace Talesmith.Assets.Database;

/// <summary>What the database remembers about a file between sessions, so unchanged files are not hashed or parsed again.</summary>
internal sealed record CachedAsset(string Path, AssetGuid Guid, string? Hash, long Size, DateTime LastWriteTimeUtc, string? ImportedStamp, IReadOnlyList<AssetReference> References);

/// <summary>Reads and writes the database cache, a JSON file in the cache folder.</summary>
internal static class AssetDatabaseCache
{
    public const string FileName = "asset-database.json";
    private const int CurrentVersion = 1;
    private const string PathPrefix = "path:";

    public static async Task<Dictionary<string, CachedAsset>> LoadAsync(string? folder, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, CachedAsset>(AssetPath.Comparer);
        if (folder is null)
            return result;

        var file = Path.Combine(folder, FileName);
        AtomicFile.Recover(file);
        if (!File.Exists(file))
            return result;

        try
        {
            var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<CacheDocument>(bytes, AssetJson.Options);
            if (document?.Version != CurrentVersion)
                return result;

            foreach (var asset in document.Assets ?? [])
            {
                if (asset.Path is null || !AssetGuid.TryParse(asset.Guid, null, out var guid))
                    continue;
                var references = (asset.References ?? []).Select(ParseReference).Where(reference => reference is not null).Select(reference => reference!.Value).ToArray();
                result[asset.Path] = new CachedAsset(asset.Path, guid, asset.Hash, asset.Size, new DateTime(asset.LastWrite, DateTimeKind.Utc), asset.Imported, references);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or AssetException)
        {
            result.Clear();
        }

        return result;
    }

    public static async Task SaveAsync(string folder, IEnumerable<AssetRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var document = new CacheDocument
        {
            Version = CurrentVersion,
            Assets =
            [
                .. records.OrderBy(record => record.Path, StringComparer.Ordinal).Select(record => new CacheEntry
                {
                    Path = record.Path,
                    Guid = record.Guid.ToString(),
                    Hash = record.ContentHash,
                    Size = record.Size,
                    LastWrite = record.LastWriteTimeUtc.Ticks,
                    Imported = record.ImportedStamp,
                    References = record.References.Count == 0 ? null : [.. record.References.Select(FormatReference)]
                })
            ]
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, AssetJson.Options);
        await AtomicFile.WriteAllBytesAsync(Path.Combine(folder, FileName), bytes, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatReference(AssetReference reference) => reference.IsPath ? PathPrefix + reference.Path : reference.Guid.ToString();

    private static AssetReference? ParseReference(string text)
    {
        if (text.StartsWith(PathPrefix, StringComparison.Ordinal))
            return AssetReference.ToPath(text[PathPrefix.Length..]);
        return AssetGuid.TryParse(text, null, out var guid) ? AssetReference.ToGuid(guid) : null;
    }

    private sealed class CacheDocument
    {
        public int Version { get; set; }
        public List<CacheEntry>? Assets { get; set; }
    }

    private sealed class CacheEntry
    {
        public string? Path { get; set; }
        public string? Guid { get; set; }
        public string? Hash { get; set; }
        public long Size { get; set; }
        public long LastWrite { get; set; }
        public string? Imported { get; set; }
        public List<string>? References { get; set; }
    }
}
