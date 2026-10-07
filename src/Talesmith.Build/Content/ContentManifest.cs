using Talesmith.Assets;
using Talesmith.Assets.Database;

namespace Talesmith.Build.Content;

/// <summary>Why an asset ships with the game.</summary>
public enum ContentReason
{
    StartScene,
    BuildScene,

    /// <summary>Another shipped asset refers to it.</summary>
    Dependency,

    /// <summary>A script or plugin names its path in code.</summary>
    CodeReference,

    /// <summary>The build settings always include it, by path or label.</summary>
    AlwaysInclude,

    /// <summary>String tables are loaded by listing the asset folder, so they always ship.</summary>
    Localization,

    /// <summary>The loading screen shows it.</summary>
    LoadingScreen
}

/// <summary>Something the build starts collecting content from: an asset or folder path, or a guid.</summary>
/// <param name="Source">What named it, such as "config/game.json" or a plugin's id, for the report.</param>
public sealed record ContentRoot(string? Path, AssetGuid Guid, ContentReason Reason, string Source)
{
    public static ContentRoot ForPath(string path, ContentReason reason, string source) => new(AssetPath.Normalize(path), AssetGuid.Empty, reason, source);

    public static ContentRoot ForGuid(AssetGuid guid, ContentReason reason, string source) => new(null, guid, reason, source);
}

/// <summary>An asset that ships with the game.</summary>
/// <param name="Via">The asset or source that brought it in.</param>
public sealed record ContentItem(AssetRecord Asset, ContentReason Reason, string Via)
{
    public string Path => Asset.Path;
}

/// <summary>The assets a build ships, and what it found missing.</summary>
/// <param name="Folders">Folders containing shipped assets, which the asset index lists too.</param>
/// <param name="MissingReferences">References from shipped assets to assets that do not exist.</param>
/// <param name="UnresolvedRoots">Roots that name no asset, such as a scene listed in the build settings that was deleted.</param>
public sealed record ContentManifest(
    IReadOnlyList<ContentItem> Items,
    IReadOnlyList<AssetRecord> Folders,
    IReadOnlyList<MissingReference> MissingReferences,
    IReadOnlyList<ContentRoot> UnresolvedRoots)
{
    public long TotalSize => Items.Sum(i => i.Asset.Size);

    public bool Contains(string path) => Items.Any(i => AssetPath.Comparer.Equals(i.Path, path));
}
