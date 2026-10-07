using System.Text.Json;
using Talesmith.Assets.Json;

namespace Talesmith.Assets;

/// <summary>Reads and writes <c>.meta</c> files; see <see cref="AssetMeta"/> for the format.</summary>
public static class AssetMetaFile
{
    public const string Extension = ".meta";

    /// <summary>The path of the meta file of an asset or folder: the asset path with ".meta" appended.</summary>
    public static string GetMetaPath(string assetPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetPath);
        return assetPath + Extension;
    }

    public static bool IsMetaPath(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The path of the asset or folder a meta file belongs to.</summary>
    public static string GetAssetPath(string metaPath)
    {
        if (!IsMetaPath(metaPath))
            throw new ArgumentException($"'{metaPath}' is not a meta file.", nameof(metaPath));
        return metaPath[..^Extension.Length];
    }

    public static byte[] Serialize(AssetMeta meta)
    {
        ArgumentNullException.ThrowIfNull(meta);
        var document = new MetaDocument
        {
            Version = AssetMeta.CurrentVersion,
            Guid = meta.Guid.ToString(),
            Folder = meta.IsFolder ? true : null,
            Importer = meta.Importer,
            ImporterVersion = meta.Importer is null && meta.ImporterVersion == 0 ? null : meta.ImporterVersion,
            Settings = meta.Settings is { ValueKind: JsonValueKind.Object } settings ? settings : null,
            Labels = meta.Labels.Count == 0 ? null : [.. meta.Labels]
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, AssetJson.Options);
    }

    /// <exception cref="AssetException">The data is not a valid meta file.</exception>
    public static AssetMeta Parse(ReadOnlySpan<byte> json)
    {
        var document = AssetJson.Deserialize<MetaDocument>(json, "The meta file");
        if (document.Version > AssetMeta.CurrentVersion)
            throw new AssetException($"The meta file has version {document.Version}, but this version of Talesmith reads up to {AssetMeta.CurrentVersion}.");
        if (!AssetGuid.TryParse(document.Guid, null, out var guid) || guid.IsEmpty)
            throw new AssetException("The meta file has no valid guid.");

        return new AssetMeta(guid)
        {
            IsFolder = document.Folder ?? false,
            Importer = string.IsNullOrWhiteSpace(document.Importer) ? null : document.Importer,
            ImporterVersion = document.ImporterVersion ?? 0,
            Settings = document.Settings is { ValueKind: JsonValueKind.Object } settings ? settings.Clone() : null,
            Labels = document.Labels?.Where(label => !string.IsNullOrWhiteSpace(label)).ToArray() ?? []
        };
    }

    /// <exception cref="AssetException">The stream does not contain a valid meta file.</exception>
    public static AssetMeta Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>Reads the meta file at a file system path, or returns null when it does not exist.</summary>
    /// <exception cref="AssetException">The file is not a valid meta file.</exception>
    public static async Task<AssetMeta?> TryReadAsync(string metaFile, CancellationToken cancellationToken = default)
    {
        AtomicFile.Recover(metaFile);
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(metaFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        return Parse(bytes);
    }

    /// <summary>Writes a meta file at a file system path atomically.</summary>
    public static Task WriteAsync(string metaFile, AssetMeta meta, CancellationToken cancellationToken = default) =>
        AtomicFile.WriteAllBytesAsync(metaFile, Serialize(meta), cancellationToken);

    private sealed class MetaDocument
    {
        public int Version { get; set; }
        public string? Guid { get; set; }
        public bool? Folder { get; set; }
        public string? Importer { get; set; }
        public int? ImporterVersion { get; set; }
        public JsonElement? Settings { get; set; }
        public List<string>? Labels { get; set; }
    }
}
