using System.Text.Json;

namespace Talesmith.Assets.Database.Dependencies;

/// <summary>Finds asset references in JSON documents: every string that is a guid, and paths in chosen properties.</summary>
/// <remarks>
/// Guids that the document defines itself, as the value of an <c>"id"</c> property (such as entity ids in scenes), are not references,
/// and neither are guids in the <paramref name="ignoredProperties"/>. Strings in <paramref name="pathProperties"/> that are not guids
/// are paths relative to the document.
/// </remarks>
/// <param name="extensions">The lower-case extensions handled, such as ".tscene".</param>
/// <param name="pathProperties">Properties whose string values, or array elements, may be paths.</param>
/// <param name="ignoredProperties">Properties whose values are never asset references, such as ids of entities in other documents.</param>
public sealed class JsonDependencyExtractor(
    IReadOnlyCollection<string> extensions,
    IReadOnlyCollection<string>? pathProperties = null,
    IReadOnlyCollection<string>? ignoredProperties = null) : IAssetDependencyExtractor
{
    private static readonly JsonReaderOptions ReaderOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly HashSet<string> _extensions = new(extensions, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pathProperties = new(pathProperties ?? [], StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ignoredProperties = new(ignoredProperties ?? ["parent", "entity", "entityId"], StringComparer.OrdinalIgnoreCase);

    public bool CanExtract(string path) => _extensions.Contains(AssetPath.GetExtension(path));

    public async Task<IReadOnlyList<AssetReference>> ExtractAsync(AssetDependencyContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        byte[] json;
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            json = buffer.ToArray();
        }

        return Extract(context, json);
    }

    private List<AssetReference> Extract(AssetDependencyContext context, ReadOnlySpan<byte> json)
    {
        var defined = new HashSet<AssetGuid>();
        var guids = new List<AssetGuid>();
        var paths = new List<string>();
        var containers = new Stack<(string? Name, bool IsArray)>();
        string? property = null;
        var reader = new Utf8JsonReader(json, ReaderOptions);
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        property = reader.GetString();
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        containers.Push((property, reader.TokenType == JsonTokenType.StartArray));
                        property = null;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        containers.TryPop(out _);
                        property = null;
                        break;
                    case JsonTokenType.String:
                        var name = property ?? (containers.TryPeek(out var container) && container.IsArray ? container.Name : null);
                        Classify(context, name, reader.GetString()!, defined, guids, paths);
                        property = null;
                        break;
                    default:
                        property = null;
                        break;
                }
            }
        }
        catch (JsonException)
        {
            // A malformed document keeps the references found before the error.
        }

        var references = new List<AssetReference>();
        var seen = new HashSet<AssetReference>();
        foreach (var guid in guids)
        {
            var reference = AssetReference.ToGuid(guid);
            if (!defined.Contains(guid) && seen.Add(reference))
                references.Add(reference);
        }

        foreach (var path in paths)
        {
            var reference = AssetReference.ToPath(path);
            if (seen.Add(reference))
                references.Add(reference);
        }

        return references;
    }

    private void Classify(AssetDependencyContext context, string? property, string value, HashSet<AssetGuid> defined, List<AssetGuid> guids, List<string> paths)
    {
        if (property is not null && _ignoredProperties.Contains(property))
            return;

        if (IsGuid(value, out var guid))
        {
            if (string.Equals(property, "id", StringComparison.OrdinalIgnoreCase))
                defined.Add(guid);
            else
                guids.Add(guid);
            return;
        }

        if (property is not null && _pathProperties.Contains(property) && value.Length > 0 && context.Resolve(value) is { } path)
            paths.Add(path);
    }

    private static bool IsGuid(string value, out AssetGuid guid)
    {
        if ((value.Length == 32 && Guid.TryParseExact(value, "N", out var parsed)) || (value.Length == 36 && Guid.TryParseExact(value, "D", out parsed)))
        {
            guid = new AssetGuid(parsed);
            return parsed != Guid.Empty;
        }

        guid = AssetGuid.Empty;
        return false;
    }
}
