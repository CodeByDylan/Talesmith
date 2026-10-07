namespace Talesmith.Plugins;

/// <summary>A plugin that can be loaded, with the plugins it sees.</summary>
/// <param name="Dependencies">Ids of the required dependencies and the optional ones that are present and match, in manifest order.</param>
/// <param name="Warnings">Folder warnings and problems with optional dependencies.</param>
internal sealed record ResolvedPlugin(PluginCandidate Candidate, IReadOnlyList<string> Dependencies, IReadOnlyList<string> Warnings)
{
    public string Id => Candidate.Id;

    public PluginLoadEntry ToEntry(PluginState state) => new()
    {
        Directory = Candidate.Directory,
        Manifest = Candidate.Manifest,
        State = state,
        Warnings = Warnings,
        Info = Candidate.ToInfo()
    };
}

/// <summary>Which plugins load, in which order, and why the others do not.</summary>
/// <param name="LoadOrder">Loadable plugins, each after the plugins it depends on.</param>
/// <param name="Entries">Every plugin; loadable ones are <see cref="PluginState.Pending"/>.</param>
internal sealed record PluginResolution(IReadOnlyList<ResolvedPlugin> LoadOrder, IReadOnlyList<PluginLoadEntry> Entries);

/// <summary>Decides which installed plugins can load: switches, engine compatibility, dependencies with version ranges and cycles.</summary>
/// <remarks>
/// The load order is a topological order that always picks the smallest id among the plugins whose dependencies are placed, so it depends
/// only on the installed plugins and never on folder names or file system order.
/// </remarks>
internal static class PluginResolver
{
    public static PluginResolution Resolve(
        IReadOnlyList<PluginCandidate> candidates,
        IReadOnlyList<PluginLoadEntry> unreadable,
        PluginConfiguration configuration,
        IReadOnlySet<string> hostDisabled,
        Version engineVersion,
        int contractVersion)
    {
        var decided = new Dictionary<string, PluginLoadEntry>(StringComparer.Ordinal);
        var duplicates = new List<PluginLoadEntry>();
        var unique = new SortedDictionary<string, PluginCandidate>(StringComparer.Ordinal);
        foreach (var group in candidates.GroupBy(c => c.Id, StringComparer.Ordinal))
        {
            var copies = group.ToList();
            if (copies.Count == 1)
            {
                unique.Add(group.Key, copies[0]);
                continue;
            }

            var folders = string.Join(", ", copies.Select(c => Path.GetFileName(c.Directory)));
            duplicates.AddRange(copies.Select(c => Entry(c, PluginState.Failed,
                $"the id \"{c.Id}\" is used by more than one plugin folder ({folders}); remove or rename all but one.")));
        }

        var engine = PluginVersion.Normalize(engineVersion);
        foreach (var candidate in unique.Values)
        {
            if (Precheck(candidate, configuration, hostDisabled, engine, contractVersion) is { } entry)
                decided.Add(candidate.Id, entry);
        }

        var duplicateIds = duplicates.Select(d => d.Id!).ToHashSet(StringComparer.Ordinal);
        var order = Sort(unique, decided, duplicateIds);

        var entries = new List<PluginLoadEntry>(candidates.Count + unreadable.Count);
        entries.AddRange(order.Select(p => p.ToEntry(PluginState.Pending)));
        entries.AddRange(decided.Values.Concat(duplicates).OrderBy(e => e.Id, StringComparer.Ordinal).ThenBy(e => e.Directory, StringComparer.Ordinal));
        entries.AddRange(unreadable.OrderBy(e => e.Directory, StringComparer.Ordinal));
        return new PluginResolution(order, entries);
    }

    /// <summary>The message for a dependent of a plugin that ended in <paramref name="state"/>.</summary>
    public static string DescribeUnavailable(string id, PluginState state) => state switch
    {
        PluginState.Disabled => $"{id}, which is disabled",
        PluginState.Skipped => $"{id}, which is skipped",
        PluginState.Failed => $"{id}, which failed to load",
        _ => $"{id}, which is not loaded"
    };

    private static PluginLoadEntry? Precheck(
        PluginCandidate candidate,
        PluginConfiguration configuration,
        IReadOnlySet<string> hostDisabled,
        Version engine,
        int contractVersion)
    {
        var manifest = candidate.Manifest;
        if (hostDisabled.Contains(manifest.Id))
            return Entry(candidate, PluginState.Disabled, "disabled by the host.");

        switch (configuration.IsEnabled(manifest.Id))
        {
            case false:
                return Entry(candidate, PluginState.Disabled, "disabled in the game's plugin configuration.");
            case null when !manifest.Enabled:
                return Entry(candidate, PluginState.Disabled, "disabled in its manifest; enable it in the game's plugin configuration to load it.");
        }

        if (manifest.ContractVersion != contractVersion)
        {
            return Entry(candidate, PluginState.Skipped,
                $"it was built for engine contract version {manifest.ContractVersion}, but this engine uses version {contractVersion}; install a matching build of the plugin.");
        }

        if (manifest.MinEngineVersion is { } minimum && engine < PluginVersion.Normalize(minimum))
        {
            return Entry(candidate, PluginState.Skipped,
                $"it needs engine version {PluginVersion.Format(minimum)} or newer, but this engine is version {PluginVersion.Format(engine)}.");
        }

        return null;
    }

    private static List<ResolvedPlugin> Sort(
        SortedDictionary<string, PluginCandidate> unique,
        Dictionary<string, PluginLoadEntry> decided,
        HashSet<string> duplicateIds)
    {
        var nodes = unique.Keys.Where(id => !decided.ContainsKey(id)).ToHashSet(StringComparer.Ordinal);
        var waiting = new Dictionary<string, int>(StringComparer.Ordinal);
        var dependents = nodes.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var id in nodes)
        {
            var edges = unique[id].Manifest.Dependencies.Select(d => d.Id).Where(nodes.Contains).ToList();
            waiting[id] = edges.Count;
            foreach (var dependency in edges)
                dependents[dependency].Add(id);
        }

        var ready = new SortedSet<string>(nodes.Where(id => waiting[id] == 0), StringComparer.Ordinal);
        var resolved = new Dictionary<string, ResolvedPlugin>(StringComparer.Ordinal);
        var order = new List<ResolvedPlugin>();
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            nodes.Remove(id);

            var plugin = Evaluate(unique[id], resolved, decided, duplicateIds, unique, out var problem);
            if (plugin is not null)
            {
                resolved.Add(id, plugin);
                order.Add(plugin);
            }
            else
            {
                decided.Add(id, Entry(unique[id], PluginState.Skipped, problem!));
            }

            foreach (var dependent in dependents[id])
            {
                if (--waiting[dependent] == 0)
                    ready.Add(dependent);
            }
        }

        foreach (var id in nodes.Order(StringComparer.Ordinal))
            decided.Add(id, Entry(unique[id], PluginState.Skipped, DescribeCycle(id, nodes, unique)));
        return order;
    }

    private static ResolvedPlugin? Evaluate(
        PluginCandidate candidate,
        Dictionary<string, ResolvedPlugin> resolved,
        Dictionary<string, PluginLoadEntry> decided,
        HashSet<string> duplicateIds,
        SortedDictionary<string, PluginCandidate> unique,
        out string? problem)
    {
        var linked = new List<string>();
        var warnings = new List<string>(candidate.Warnings);
        foreach (var dependency in candidate.Manifest.Dependencies)
        {
            var range = dependency.Versions.IsAny ? "" : $" {dependency.Versions}";
            string? unavailable;
            if (resolved.TryGetValue(dependency.Id, out var present))
            {
                var version = present.Candidate.Manifest.Version;
                unavailable = dependency.Versions.IsSatisfiedBy(version)
                    ? null
                    : $"{dependency.Id}{range}, but version {PluginVersion.Format(version)} is installed";
            }
            else if (decided.TryGetValue(dependency.Id, out var entry))
            {
                unavailable = DescribeUnavailable(dependency.Id, entry.State);
            }
            else
            {
                unavailable = duplicateIds.Contains(dependency.Id) ? $"{dependency.Id}, which is installed more than once"
                    : unique.ContainsKey(dependency.Id) ? DescribeUnavailable(dependency.Id, PluginState.Skipped)
                    : $"{dependency.Id}{range}, which is not installed";
            }

            if (unavailable is null)
                linked.Add(dependency.Id);
            else if (!dependency.Optional)
            {
                problem = $"it requires {unavailable}.";
                return null;
            }
            else
            {
                warnings.Add($"it optionally uses {unavailable}; it loads without it.");
            }
        }

        problem = null;
        return new ResolvedPlugin(candidate, linked, warnings);
    }

    /// <summary>Describes the cycle <paramref name="id"/> is on, or the cycle it depends on.</summary>
    private static string DescribeCycle(string id, HashSet<string> remaining, SortedDictionary<string, PluginCandidate> unique)
    {
        if (FindPath(id, id, remaining, unique, []) is { } cycle)
            return $"its dependencies form a cycle: {id} -> {string.Join(" -> ", cycle)}.";

        var member = remaining.Order(StringComparer.Ordinal).First(other => FindPath(other, other, remaining, unique, []) is not null &&
                                                                            FindPath(id, other, remaining, unique, []) is not null);
        return $"it depends on {member}, whose dependencies form a cycle.";
    }

    /// <summary>A dependency path from <paramref name="from"/> to <paramref name="to"/> within <paramref name="remaining"/>, excluding the start.</summary>
    private static List<string>? FindPath(string from, string to, HashSet<string> remaining, SortedDictionary<string, PluginCandidate> unique, HashSet<string> visited)
    {
        foreach (var dependency in unique[from].Manifest.Dependencies.Select(d => d.Id).Where(remaining.Contains).Order(StringComparer.Ordinal))
        {
            if (dependency == to)
                return [dependency];
            if (!visited.Add(dependency))
                continue;
            if (FindPath(dependency, to, remaining, unique, visited) is { } path)
                return [dependency, .. path];
        }

        return null;
    }

    private static PluginLoadEntry Entry(PluginCandidate candidate, PluginState state, string reason) => new()
    {
        Directory = candidate.Directory,
        Manifest = candidate.Manifest,
        State = state,
        Reason = reason,
        Warnings = candidate.Warnings,
        Info = candidate.ToInfo()
    };
}
