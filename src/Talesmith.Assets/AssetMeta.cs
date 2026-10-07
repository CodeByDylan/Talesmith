using System.Text.Json;
using Talesmith.Assets.Json;

namespace Talesmith.Assets;

/// <summary>What the sidecar <c>.meta</c> file of an asset or folder records: its guid, importer, import settings and labels.</summary>
/// <remarks>
/// <para>The file is JSON next to the asset, named after it with ".meta" appended:</para>
/// <code>
/// {
///   "version": 1,
///   "guid": "9a2c4e0b7d1f4a3c8e6b5d4c3b2a1f0e",
///   "importer": "texture",
///   "importerVersion": 1,
///   "settings": { "filter": "nearest", "spriteMode": "multiple" },
///   "labels": [ "hero" ]
/// }
/// </code>
/// <para>Folders write <c>"folder": true</c> and no importer. Missing settings mean the importer's defaults.</para>
/// </remarks>
public sealed record AssetMeta
{
    /// <summary>The version of the .meta format this code writes.</summary>
    public const int CurrentVersion = 1;

    public AssetMeta(AssetGuid guid)
    {
        if (guid.IsEmpty)
            throw new ArgumentException("An asset needs a guid.", nameof(guid));
        Guid = guid;
    }

    public AssetGuid Guid { get; init; }

    /// <summary>Whether the meta describes a folder rather than a file.</summary>
    public bool IsFolder { get; init; }

    /// <summary>The id of the importer whose settings these are, such as "texture"; null for folders and files without an importer.</summary>
    public string? Importer { get; init; }

    /// <summary>The version of the importer's settings when they were written; newer importers re-import and upgrade them.</summary>
    public int ImporterVersion { get; init; }

    /// <summary>The importer's settings as written in the file; null means defaults. Read them with <see cref="GetSettings{T}"/>.</summary>
    public JsonElement? Settings { get; init; }

    /// <summary>User labels for searching and filtering, such as "ui" or "hero".</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    public static AssetMeta CreateFolder(AssetGuid guid) => new(guid) { IsFolder = true };

    /// <summary>Reads the settings as <typeparamref name="T"/>; missing settings give a default instance.</summary>
    /// <exception cref="AssetException">The settings do not match <typeparamref name="T"/>.</exception>
    public T GetSettings<T>() where T : class, new()
    {
        if (Settings is not { ValueKind: JsonValueKind.Object } settings)
            return new T();

        try
        {
            return settings.Deserialize<T>(AssetJson.Options) ?? new T();
        }
        catch (JsonException ex)
        {
            throw new AssetException($"The import settings of asset {Guid} are not valid {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>A copy with <paramref name="settings"/> stored, or the defaults when null.</summary>
    public AssetMeta WithSettings<T>(T? settings) where T : class =>
        this with { Settings = settings is null ? null : JsonSerializer.SerializeToElement(settings, AssetJson.Options) };

    /// <summary>Whether two metas would be written as the same file.</summary>
    public bool ContentEquals(AssetMeta? other) =>
        other is not null && AssetMetaFile.Serialize(this).AsSpan().SequenceEqual(AssetMetaFile.Serialize(other));
}
