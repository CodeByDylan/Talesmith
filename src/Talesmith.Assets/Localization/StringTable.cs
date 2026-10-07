using System.Diagnostics.CodeAnalysis;
using Talesmith.Assets.Json;

namespace Talesmith.Assets.Localization;

/// <summary>Translated strings by key and language, loaded from a <c>.tloc</c> file.</summary>
/// <remarks>
/// <code>
/// {
///   "version": 1,
///   "strings": {
///     "menu.start": { "en": "Start", "nl": "Beginnen" },
///     "hud.coins": { "en": "{0} coins", "nl": "{0} munten" }
///   }
/// }
/// </code>
/// Languages are IETF tags such as "en" or "pt-BR"; formatted strings use .NET composite format placeholders.
/// </remarks>
public sealed class StringTable
{
    private readonly Dictionary<string, Dictionary<string, string>> _strings;

    public StringTable(string path, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(strings);
        Path = path;
        _strings = strings.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToDictionary(text => text.Key, text => text.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);
        Languages = [.. _strings.Values.SelectMany(texts => texts.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public string Path { get; }

    /// <summary>Every language with at least one string.</summary>
    public IReadOnlyList<string> Languages { get; }

    public IEnumerable<string> Keys => _strings.Keys;

    /// <summary>Gets the text of a key in exactly this language.</summary>
    public bool TryGet(string key, string language, [NotNullWhen(true)] out string? text)
    {
        text = null;
        return _strings.TryGetValue(key, out var texts) && texts.TryGetValue(language, out text);
    }

    /// <exception cref="AssetException">The data is not a valid string table.</exception>
    public static StringTable Parse(string path, ReadOnlySpan<byte> json)
    {
        var document = AssetJson.Deserialize<StringTableDocument>(json, "The string table");
        if (document.Version > StringTableDocument.CurrentVersion)
            throw new AssetException($"The string table has version {document.Version}, but this version of Talesmith reads up to {StringTableDocument.CurrentVersion}.");
        return new StringTable(path, (document.Strings ?? []).ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)(pair.Value ?? []),
            StringComparer.Ordinal));
    }

    private sealed class StringTableDocument
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;

        public Dictionary<string, Dictionary<string, string>?>? Strings { get; set; }
    }
}

/// <summary>Imports <c>.tloc</c> files as <see cref="StringTable"/>s.</summary>
public sealed class StringTableImporter : AssetImporter<StringTable>
{
    public const string ImporterId = "localization";

    public override IReadOnlyList<string> Extensions { get; } = [".tloc"];

    public override string Id => ImporterId;

    public override async Task<StringTable> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var buffer = new MemoryStream();
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return StringTable.Parse(context.Path, buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}
