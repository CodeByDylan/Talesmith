using System.IO.Compression;
using System.Text.Json;

namespace Talesmith.Assets.Database.Dependencies;

/// <summary>Finds the tileset images a Hexy map loads from the asset folder.</summary>
/// <remarks>Images stored inside a version 2 map package are part of the map and are not references.</remarks>
public sealed class HexyDependencyExtractor : IAssetDependencyExtractor
{
    private const string DocumentEntry = "map.json";

    public bool CanExtract(string path) => AssetPath.GetExtension(path) == ".hexy";

    public async Task<IReadOnlyList<AssetReference>> ExtractAsync(AssetDependencyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var buffer = new MemoryStream();
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;

        try
        {
            if (buffer.Length < 2 || buffer.GetBuffer()[0] != (byte)'P' || buffer.GetBuffer()[1] != (byte)'K')
                return Read(context, buffer, package: null);

            using var package = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (package.GetEntry(DocumentEntry) is not { } entry)
                return [];
            using var json = entry.Open();
            return Read(context, json, package);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return [];
        }
    }

    private static List<AssetReference> Read(AssetDependencyContext context, Stream json, ZipArchive? package)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var references = new List<AssetReference>();
        if (!document.RootElement.TryGetProperty("tilesets", out var tilesets) || tilesets.ValueKind != JsonValueKind.Array)
            return references;

        foreach (var tileset in tilesets.EnumerateArray())
        {
            if (tileset.ValueKind != JsonValueKind.Object || !tileset.TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.String || image.GetString() is not { Length: > 0 } path)
                continue;
            if (package?.GetEntry(path) is not null || Path.IsPathRooted(path))
                continue;
            if (context.Resolve(path) is { } resolved && !references.Contains(AssetReference.ToPath(resolved)))
                references.Add(AssetReference.ToPath(resolved));
        }

        return references;
    }
}
