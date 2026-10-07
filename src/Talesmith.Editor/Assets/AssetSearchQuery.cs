using System.Globalization;
using Talesmith.Assets;
using Talesmith.Assets.Database;

namespace Talesmith.Editor.Assets;

/// <summary>A parsed search of the Assets panel, such as <c>hero kind:texture tag:enemy</c>.</summary>
/// <remarks>
/// Words match names; <c>kind:</c> (or <c>t:</c>) matches kind ids and names, <c>tag:</c> (or <c>label:</c>, <c>l:</c>) matches labels,
/// <c>ext:</c> matches extensions, <c>guid:</c> or a bare 32-digit guid matches guids by prefix, and <c>is:favorite</c> keeps favorites.
/// Every term must match.
/// </remarks>
public sealed record AssetSearchQuery
{
    public static AssetSearchQuery Empty { get; } = new();

    public IReadOnlyList<string> Words { get; init; } = [];

    public IReadOnlyList<string> Kinds { get; init; } = [];

    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Hex digits a guid starts with, or null.</summary>
    public string? Guid { get; init; }

    public bool FavoritesOnly { get; init; }

    public bool IsEmpty => Words.Count == 0 && Kinds.Count == 0 && Labels.Count == 0 && Extensions.Count == 0 && Guid is null && !FavoritesOnly;

    public static AssetSearchQuery Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Empty;
        List<string> words = [], kinds = [], labels = [], extensions = [];
        string? guid = null;
        var favorites = false;
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            var key = colon > 0 ? token[..colon].ToLowerInvariant() : "";
            var value = colon > 0 ? token[(colon + 1)..] : token;
            if (colon > 0 && value.Length == 0)
                continue;
            switch (key)
            {
                case "kind" or "type" or "t":
                    kinds.Add(value);
                    break;
                case "tag" or "label" or "l":
                    labels.Add(value);
                    break;
                case "ext":
                    extensions.Add(value.TrimStart('.'));
                    break;
                case "guid" or "id":
                    guid = NormalizeGuid(value);
                    break;
                case "is" when value.StartsWith("fav", StringComparison.OrdinalIgnoreCase):
                    favorites = true;
                    break;
                default:
                    if (colon < 0 && token.Length == 32 && token.All(char.IsAsciiHexDigit))
                        guid = token.ToLowerInvariant();
                    else
                        words.Add(token);
                    break;
            }
        }

        return new AssetSearchQuery { Words = words, Kinds = kinds, Labels = labels, Extensions = extensions, Guid = guid, FavoritesOnly = favorites };
    }

    public bool Matches(AssetRecord record, bool isFavorite = false)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (FavoritesOnly && !isFavorite)
            return false;
        if (Guid is { } guid && !record.Guid.ToString().StartsWith(guid, StringComparison.Ordinal))
            return false;
        var name = record.Name;
        foreach (var word in Words)
        {
            if (!name.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        foreach (var kind in Kinds)
        {
            if (!MatchesKind(record.Kind, kind))
                return false;
        }

        foreach (var label in Labels)
        {
            if (!record.Meta.Labels.Any(l => l.StartsWith(label, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (Extensions.Count > 0)
        {
            var extension = AssetPath.GetExtension(record.Path).TrimStart('.');
            if (!Extensions.Any(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static bool MatchesKind(AssetKind kind, string term) =>
        kind.Id.StartsWith(term, StringComparison.OrdinalIgnoreCase)
        || kind.DisplayName.Replace(" ", "", StringComparison.Ordinal).StartsWith(term, StringComparison.OrdinalIgnoreCase)
        || kind.DisplayName.Split(' ').Any(part => part.StartsWith(term, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeGuid(string value) =>
        new([.. value.Where(char.IsAsciiHexDigit).Select(c => char.ToLower(c, CultureInfo.InvariantCulture))]);
}
