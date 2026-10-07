using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Packs;
using Talesmith.Scripting;

namespace Talesmith.Build.Content;

/// <summary>Decides which files of the asset folder go into the content pack and which ship as loose files or not at all.</summary>
/// <param name="PluginsFolder">The game's plugins folder, relative to the asset root.</param>
public sealed record ContentRules(string PluginsFolder = "plugins")
{
    /// <summary>Configuration read from disk before the asset system starts; shipped loose.</summary>
    public const string ConfigFolder = "config";

    /// <summary>Script sources; builds ship the compiled assembly instead.</summary>
    public const string ScriptsFolder = "scripts";

    /// <summary>Whether a file belongs in the content pack.</summary>
    public bool IsPacked(AssetRecord asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.IsFolder || asset.Kind == AssetKind.Script)
            return false;
        var path = asset.Path;
        return !AssetPath.IsWithin(path, ConfigFolder)
               && !AssetPath.IsWithin(path, ScriptsFolder)
               && !AssetPath.IsWithin(path, AssetPath.Normalize(PluginsFolder))
               && !AssetPath.Comparer.Equals(path, AssetIndex.FileName)
               && !AssetPath.Comparer.Equals(path, ContentPack.FileName)
               && !AssetPath.Comparer.Equals(path, ScriptingOptions.DefaultAssemblyPath);
    }
}

/// <summary>Collects the assets a game needs: everything reachable from its roots through the asset database's dependency graph.</summary>
/// <remarks>A folder root, or a dependency on a folder (as sprite atlases have), includes every file inside the folder.</remarks>
internal sealed class ContentCollector(AssetDatabase assets, ContentRules rules)
{
    public ContentManifest Collect(IEnumerable<ContentRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var graph = assets.GetDependencyGraph();
        var records = assets.Assets;
        var items = new Dictionary<AssetGuid, ContentItem>();
        var visited = new HashSet<AssetGuid>();
        var unresolved = new List<ContentRoot>();
        var queue = new Queue<(AssetRecord Asset, ContentReason Reason, string Via)>();

        foreach (var root in roots)
        {
            AssetRecord? record = null;
            var found = root.Path is { } path ? assets.TryGetAsset(path, out record) : assets.TryGetAsset(root.Guid, out record);
            if (found && record is not null)
                queue.Enqueue((record, root.Reason, root.Source));
            else
                unresolved.Add(root);
        }

        while (queue.TryDequeue(out var next))
        {
            var (asset, reason, via) = next;
            if (!visited.Add(asset.Guid))
                continue;
            if (asset.IsFolder)
            {
                foreach (var child in records.Where(r => !r.IsFolder && AssetPath.IsWithin(r.Path, asset.Path)))
                    queue.Enqueue((child, reason, via));
                continue;
            }

            if (!rules.IsPacked(asset))
                continue;
            items[asset.Guid] = new ContentItem(asset, reason, via);
            foreach (var dependency in graph.GetDependencies(asset.Guid))
            {
                if (assets.TryGetAsset(dependency, out var target))
                    queue.Enqueue((target, ContentReason.Dependency, asset.Path));
            }
        }

        var folders = new Dictionary<string, AssetRecord>(AssetPath.Comparer);
        foreach (var item in items.Values)
        {
            for (var folder = AssetPath.GetDirectory(item.Path); folder.Length > 0 && !folders.ContainsKey(folder); folder = AssetPath.GetDirectory(folder))
            {
                if (assets.TryGetAsset(folder, out var record))
                    folders[folder] = record;
            }
        }

        var missing = graph.MissingReferences.Where(m => items.ContainsKey(m.From)).ToArray();
        return new ContentManifest(
            [.. items.Values.OrderBy(i => i.Path, StringComparer.Ordinal)],
            [.. folders.Values.OrderBy(f => f.Path, StringComparer.Ordinal)],
            missing,
            unresolved);
    }
}
