namespace Talesmith.Assets.Database;

/// <summary>Where an <see cref="AssetDatabase"/> works and keeps its state.</summary>
/// <param name="AssetRoot">The asset folder of the project.</param>
public sealed record AssetDatabaseOptions(string AssetRoot)
{
    /// <summary>The name of the project folder that holds editor state, which builds and version control ignore.</summary>
    public const string StateFolderName = ".talesmith";

    /// <summary>Where hashes, import state and dependencies are kept between sessions; null keeps nothing.</summary>
    public string? CacheFolder { get; init; }

    /// <summary>Where deleted assets are moved; null deletes them permanently.</summary>
    public string? TrashFolder { get; init; }

    /// <summary>The scripts folder, relative to the asset folder; <c>bin</c> and <c>obj</c> folders inside it hold build output, not assets.</summary>
    public string? ScriptsFolder { get; init; } = "scripts";

    /// <summary>The game's plugins folder, relative to the asset folder; plugins are code with their own files, not assets, so it is left
    /// out with everything inside. Null scans it like any other folder.</summary>
    public string? PluginsFolder { get; init; } = "plugins";

    /// <summary>How long the file system must be quiet before watched changes are processed.</summary>
    public TimeSpan WatchDebounce { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How many files are read and hashed at the same time during scans.</summary>
    public int MaxParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>Options for a project folder: assets in <c>assets/</c>, the cache in <c>.talesmith/cache/</c> and deleted assets in <c>.talesmith/trash/</c>.</summary>
    public static AssetDatabaseOptions ForProject(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var state = Path.Combine(projectFolder, StateFolderName);
        return new AssetDatabaseOptions(Path.Combine(projectFolder, "assets"))
        {
            CacheFolder = Path.Combine(state, "cache"),
            TrashFolder = Path.Combine(state, "trash")
        };
    }
}

/// <summary>A reference from one asset to another, by guid or, for formats that store them, by path.</summary>
/// <param name="Path">The referenced asset path when the reference is a path; null for guid references.</param>
public readonly record struct AssetReference(AssetGuid Guid, string? Path)
{
    public static AssetReference ToGuid(AssetGuid guid) => new(guid, null);

    public static AssetReference ToPath(string path) => new(AssetGuid.Empty, AssetPath.Normalize(path));

    public bool IsPath => Path is not null;

    public override string ToString() => Path ?? Guid.ToString();
}

/// <summary>What the asset database knows about one asset or folder.</summary>
public sealed record AssetRecord
{
    public required AssetGuid Guid { get; init; }

    /// <summary>The path relative to the asset root.</summary>
    public required string Path { get; init; }

    public required AssetKind Kind { get; init; }

    public required AssetMeta Meta { get; init; }

    public bool IsFolder => Meta.IsFolder;

    public string Name => AssetPath.GetFileName(Path);

    /// <summary>The SHA-256 of the file's contents in hex; null for folders.</summary>
    public string? ContentHash { get; init; }

    public long Size { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    /// <summary>The assets this one refers to, as written in the file.</summary>
    public IReadOnlyList<AssetReference> References { get; init; } = [];

    /// <summary>Identifies what an import of the asset would produce: its contents, importer, importer version and settings.</summary>
    public string? ImportStamp { get; init; }

    /// <summary>The <see cref="ImportStamp"/> of the last import reported with <see cref="AssetDatabase.MarkImportedAsync"/>.</summary>
    public string? ImportedStamp { get; init; }

    /// <summary>Whether the asset changed since it was last imported, or was never imported.</summary>
    public bool NeedsImport => !IsFolder && Meta.Importer is not null && ImportStamp != ImportedStamp;
}

public enum AssetChangeKind
{
    Added,
    Changed,
    Moved,
    Deleted
}

/// <summary>What changed about an asset that still has the same path.</summary>
[Flags]
public enum AssetChangeReasons
{
    None = 0,
    Content = 1,
    Settings = 2,
    ImporterVersion = 4
}

/// <summary>One change to the assets of a project.</summary>
/// <param name="Change">Whether the asset was added, changed, moved or deleted.</param>
/// <param name="Path">The current path, or the last path for deleted assets.</param>
public sealed record AssetChange(AssetChangeKind Change, AssetGuid Guid, string Path, AssetKind Kind)
{
    /// <summary>For moves, the previous path.</summary>
    public string? OldPath { get; init; }

    /// <summary>For changes, what changed.</summary>
    public AssetChangeReasons Reasons { get; init; }

    /// <summary>For assets added as copies of another asset's .meta file, the guid they had before they were given a new one.</summary>
    public AssetGuid ReplacedGuid { get; init; }

    public override string ToString() => Change switch
    {
        AssetChangeKind.Moved => $"Moved {OldPath} to {Path}",
        AssetChangeKind.Changed => $"Changed {Path} ({Reasons})",
        _ => $"{Change} {Path}"
    };
}

/// <summary>A batch of asset changes, in the order they were found.</summary>
public sealed class AssetChangesEventArgs(IReadOnlyList<AssetChange> changes) : EventArgs
{
    public IReadOnlyList<AssetChange> Changes { get; } = changes;
}

/// <summary>How far a scan has come.</summary>
/// <param name="CurrentPath">The path being read, or null at the start and end.</param>
public readonly record struct AssetScanProgress(int Processed, int Total, string? CurrentPath)
{
    public double Fraction => Total == 0 ? 1 : Processed / (double)Total;
}

/// <summary>An asset whose .meta file had the guid of another asset, typically a copy, and was given a new guid.</summary>
public sealed record DuplicateGuidRepair(AssetGuid Guid, string KeptPath, string RepairedPath, AssetGuid NewGuid);

/// <summary>A .meta file that could not be read and was replaced; the original is kept next to it as a hidden ".invalid" file.</summary>
public sealed record InvalidMeta(string Path, string Error);

/// <summary>A reference to an asset that does not exist.</summary>
public sealed record MissingReference(AssetGuid From, string FromPath, AssetReference Reference);

/// <summary>The outcome of a full scan.</summary>
public sealed record AssetScanResult(int AssetCount, int MetasCreated, IReadOnlyList<DuplicateGuidRepair> Repairs, IReadOnlyList<InvalidMeta> InvalidMetas, TimeSpan Duration);

/// <summary>Problems in a project's assets.</summary>
/// <param name="OrphanedMetas">.meta files whose asset no longer exists.</param>
/// <param name="UnreferencedAssets">
/// Assets no other asset refers to. Scenes, scripts, plugin manifests, string tables, folders and other files count as used; code may still
/// load what is listed here by path.
/// </param>
/// <param name="DuplicateGuids">Guid conflicts repaired since the database was created.</param>
public sealed record AssetReport(
    IReadOnlyList<MissingReference> MissingReferences,
    IReadOnlyList<string> OrphanedMetas,
    IReadOnlyList<AssetRecord> UnreferencedAssets,
    IReadOnlyList<DuplicateGuidRepair> DuplicateGuids,
    IReadOnlyList<InvalidMeta> InvalidMetas)
{
    public bool IsClean => MissingReferences.Count == 0 && OrphanedMetas.Count == 0 && InvalidMetas.Count == 0;
}
