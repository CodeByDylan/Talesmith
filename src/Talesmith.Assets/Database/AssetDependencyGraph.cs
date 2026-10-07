using Talesmith.Assets.Database.Dependencies;

namespace Talesmith.Assets.Database;

/// <summary>Which assets refer to which, resolved against the assets that exist when the graph was built.</summary>
/// <remarks>An immutable snapshot; <see cref="AssetDatabase.GetDependencyGraph"/> returns a new one after assets change.</remarks>
public sealed class AssetDependencyGraph
{
    private readonly Dictionary<AssetGuid, AssetGuid[]> _dependencies = new();
    private readonly Dictionary<AssetGuid, List<AssetGuid>> _dependents = new();

    internal AssetDependencyGraph(IReadOnlyCollection<AssetRecord> records, Func<string, AssetGuid> resolvePath, Func<AssetGuid, bool> exists)
    {
        var missing = new List<MissingReference>();
        foreach (var record in records)
        {
            var targets = new List<AssetGuid>();
            foreach (var reference in record.References)
            {
                var target = reference.IsPath ? resolvePath(reference.Path!) : reference.Guid;
                if (target.IsEmpty || !exists(target))
                {
                    missing.Add(new MissingReference(record.Guid, record.Path, reference));
                    continue;
                }

                if (target == record.Guid || targets.Contains(target))
                    continue;
                targets.Add(target);
                if (!_dependents.TryGetValue(target, out var dependents))
                    _dependents[target] = dependents = [];
                dependents.Add(record.Guid);
            }

            if (targets.Count > 0)
                _dependencies[record.Guid] = [.. targets];
        }

        MissingReferences = missing;
    }

    /// <summary>References to assets that do not exist.</summary>
    public IReadOnlyList<MissingReference> MissingReferences { get; }

    /// <summary>The assets <paramref name="guid"/> refers to directly.</summary>
    public IReadOnlyList<AssetGuid> GetDependencies(AssetGuid guid) => _dependencies.GetValueOrDefault(guid, []);

    /// <summary>The assets that refer to <paramref name="guid"/>, directly or, when <paramref name="transitive"/>, through other assets.</summary>
    /// <remarks>Transitive results are in breadth-first order, nearest first, without duplicates; cycles are followed once.</remarks>
    public IReadOnlyList<AssetGuid> GetDependents(AssetGuid guid, bool transitive = false)
    {
        if (!transitive)
            return _dependents.TryGetValue(guid, out var direct) ? direct : [];

        var result = new List<AssetGuid>();
        var seen = new HashSet<AssetGuid> { guid };
        var pending = new Queue<AssetGuid>();
        pending.Enqueue(guid);
        while (pending.TryDequeue(out var current))
        {
            if (!_dependents.TryGetValue(current, out var dependents))
                continue;
            foreach (var dependent in dependents)
            {
                if (!seen.Add(dependent))
                    continue;
                result.Add(dependent);
                pending.Enqueue(dependent);
            }
        }

        return result;
    }

    public bool IsReferenced(AssetGuid guid) => _dependents.ContainsKey(guid);
}

/// <summary>The dependency extractors for the engine's own file formats.</summary>
public static class DefaultDependencyExtractors
{
    public static IReadOnlyList<IAssetDependencyExtractor> Create() =>
    [
        new JsonDependencyExtractor([".tscene", ".tprefab", ".tparticles"]),
        new JsonDependencyExtractor([".tmaterial"], pathProperties: ["shader"]),
        new JsonDependencyExtractor([".tatlas"], pathProperties: ["sources"]),
        new JsonDependencyExtractor([".tshader"], pathProperties: ["skSlFile", "spirVFile"]),
        new HexyDependencyExtractor()
    ];
}
