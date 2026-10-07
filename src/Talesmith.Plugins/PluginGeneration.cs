using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Talesmith.Systems;

namespace Talesmith.Plugins;

/// <summary>A loaded plugin's assemblies: the editor looks for its editor plugin classes in <paramref name="EditorAssembly"/>.</summary>
/// <param name="EditorAssembly">The editor part, loaded only when <see cref="PluginLoadOptions.LoadEditorAssemblies"/> is set.</param>
public sealed record PluginAssemblies(PluginInfo Plugin, Assembly Assembly, Assembly? EditorAssembly);

/// <summary>The plugin assemblies loaded together from one resolution; every game built from it shares their types.</summary>
internal sealed class PluginGeneration
{
    private sealed record ActivePlugin(
        ResolvedPlugin Resolved,
        PluginLoadContext Context,
        Assembly Assembly,
        Assembly? EditorAssembly,
        ConstructorInfo Constructor,
        IReadOnlyList<Assembly> Visible,
        IReadOnlyList<string> Warnings,
        PluginPermissions Undeclared)
    {
        public string Id => Resolved.Id;

        public PluginInfo Info => Resolved.Candidate.ToInfo();
    }

    private readonly PluginLoadOptions _options;
    private readonly List<PluginLoadEntry> _entries;
    private readonly Dictionary<string, ActivePlugin> _active;

    private PluginGeneration(PluginLoadOptions options, List<PluginLoadEntry> entries, Dictionary<string, ActivePlugin> active, PluginPermissionService permissions)
    {
        _options = options;
        _entries = entries;
        _active = active;
        Permissions = permissions;
    }

    public PluginPermissionService Permissions { get; }

    /// <summary>The state of every plugin after loading assemblies; plugins ready to configure are <see cref="PluginState.Pending"/>.</summary>
    public IReadOnlyList<PluginLoadEntry> Entries => _entries;

    public IReadOnlyList<PluginAssemblies> Assemblies =>
        [.. _active.Values.Select(p => new PluginAssemblies(p.Info, p.Assembly, p.EditorAssembly))];

    public static PluginGeneration Load(PluginResolution resolution, PluginLoadOptions options, ILogger logger)
    {
        var entries = new List<PluginLoadEntry>(resolution.Entries);
        var outcome = new Dictionary<string, PluginState>(StringComparer.Ordinal);
        var active = new Dictionary<string, ActivePlugin>(StringComparer.Ordinal);
        foreach (var resolved in resolution.LoadOrder)
        {
            var index = entries.FindIndex(e => e.State == PluginState.Pending && e.Id == resolved.Id);
            var warnings = new List<string>(resolved.Warnings);
            var visible = new List<Assembly>();
            string? problem = null;
            foreach (var id in resolved.Dependencies)
            {
                if (active.TryGetValue(id, out var dependency))
                {
                    visible.AddRange(dependency.Visible);
                    continue;
                }

                var unavailable = PluginResolver.DescribeUnavailable(id, outcome.GetValueOrDefault(id, PluginState.Failed));
                if (IsOptional(resolved, id))
                {
                    warnings.Add($"it optionally uses {unavailable}; it loads without it.");
                    continue;
                }

                problem = $"it requires {unavailable}.";
                break;
            }

            if (problem is not null)
            {
                entries[index] = entries[index] with { State = PluginState.Skipped, Reason = problem, Warnings = warnings };
                outcome[resolved.Id] = PluginState.Skipped;
                continue;
            }

            try
            {
                var plugin = LoadPlugin(resolved, visible.Distinct().ToList(), warnings, options);
                active.Add(plugin.Id, plugin);
                entries[index] = entries[index] with
                {
                    Warnings = plugin.Warnings,
                    UndeclaredPermissions = plugin.Undeclared,
                    EditorAssemblyLoaded = plugin.EditorAssembly is not null
                };
                outcome[resolved.Id] = PluginState.Pending;
            }
            catch (Exception ex)
            {
                var reason = ex is PluginLoadException ? ex.Message : $"loading failed unexpectedly with {ex.GetType().Name}: {ex.Message}";
                entries[index] = entries[index] with
                {
                    State = PluginState.Failed,
                    Reason = reason,
                    Warnings = warnings,
                    ErrorDetails = (ex is PluginLoadException ? ex.InnerException : ex)?.ToString()
                };
                outcome[resolved.Id] = PluginState.Failed;
            }
        }

        var declared = entries.Where(e => e.Manifest is not null).DistinctBy(e => e.Id).ToDictionary(e => e.Id!, e => e.Permissions);
        return new PluginGeneration(options, entries, active, new PluginPermissionService(declared, options.PermissionPolicy, logger));
    }

    /// <summary>Lets every loaded plugin register into <paramref name="services"/>, dependencies first, and registers the report,
    /// <see cref="IPluginPermissions"/> and each plugin's <see cref="IPluginSettings"/> keyed by plugin id.</summary>
    public PluginLoadReport Configure(IServiceCollection services, Func<string, IPluginSettings> settings)
    {
        var entries = new List<PluginLoadEntry>(_entries.Count);
        var outcome = new Dictionary<string, PluginState>(StringComparer.Ordinal);
        foreach (var entry in _entries)
        {
            var configured = entry.State == PluginState.Pending ? Configure(_active[entry.Id!], entry, outcome, services, settings) : entry;
            entries.Add(configured);
            if (configured.Id is { } id)
                outcome.TryAdd(id, configured.State);
        }

        var report = new PluginLoadReport(entries);
        services.AddSingleton(report);
        services.TryAddSingleton<IPluginPermissions>(Permissions);
        foreach (var plugin in report.Loaded)
            services.AddKeyedSingleton(plugin.Id, settings(plugin.Id!));
        return report;
    }

    /// <summary>Asks every plugin context to unload; the code is released once nothing references it.</summary>
    public PluginUnloadResult Unload()
    {
        var contexts = _active.Values.Select(p => (p.Id, new WeakReference(p.Context))).ToList();
        if (_options.Collectible)
        {
            foreach (var plugin in _active.Values)
                plugin.Context.Unload();
        }

        _active.Clear();
        return new PluginUnloadResult(contexts, _options.Collectible);
    }

    private PluginLoadEntry Configure(
        ActivePlugin plugin,
        PluginLoadEntry entry,
        Dictionary<string, PluginState> outcome,
        IServiceCollection services,
        Func<string, IPluginSettings> settings)
    {
        var warnings = new List<string>(entry.Warnings);
        foreach (var id in plugin.Resolved.Dependencies.Where(_active.ContainsKey))
        {
            var state = outcome.GetValueOrDefault(id, PluginState.Failed);
            if (state == PluginState.Loaded)
                continue;
            var unavailable = PluginResolver.DescribeUnavailable(id, state);
            if (!IsOptional(plugin.Resolved, id))
                return entry with { State = PluginState.Skipped, Reason = $"it requires {unavailable}.", Warnings = warnings };
            warnings.Add($"it optionally uses {unavailable}; it is configured without it.");
        }

        var undeclared = entry.UndeclaredPermissions;
        try
        {
            PluginActivator.Configure(plugin.Constructor, plugin.Info, settings(plugin.Id), services, added =>
            {
                var missing = CheckRegistrations(added, entry.Permissions, warnings);
                undeclared |= missing;
                return _options.PermissionPolicy == PluginPermissionPolicy.Enforce && missing != PluginPermissions.None
                    ? $"it registers services that need undeclared permissions: {PluginPermissionNames.GetName(missing)}."
                    : null;
            });
            return entry with { State = PluginState.Loaded, Warnings = warnings, UndeclaredPermissions = undeclared };
        }
        catch (Exception ex)
        {
            var reason = ex is PluginLoadException ? ex.Message : $"configuring failed unexpectedly with {ex.GetType().Name}: {ex.Message}";
            return entry with
            {
                State = PluginState.Failed,
                Reason = reason,
                Warnings = warnings,
                UndeclaredPermissions = undeclared,
                ErrorDetails = (ex is PluginLoadException ? ex.InnerException : ex)?.ToString()
            };
        }
    }

    private PluginPermissions CheckRegistrations(IReadOnlyList<ServiceDescriptor> added, PluginPermissions declared, List<string> warnings)
    {
        var missing = PluginPermissions.None;
        foreach (var rules in _options.RegistrationRules.Where(r => (declared & r.Permission) != r.Permission).GroupBy(r => r.Permission & ~declared))
        {
            var found = rules
                .Select(rule => (rule.Description, Services: added.Where(d => rule.Matches(d.ServiceType)).Select(Describe).Distinct().ToList()))
                .Where(match => match.Services.Count > 0)
                .Select(match => $"{match.Description} ({List(match.Services)})")
                .ToList();
            if (found.Count == 0)
                continue;
            missing |= rules.Key;
            warnings.Add($"it registers {string.Join(" and ", found)} without declaring the \"{PluginPermissionNames.GetName(rules.Key)}\" permission.");
        }

        return missing;
    }

    private static ActivePlugin LoadPlugin(ResolvedPlugin resolved, List<Assembly> visible, List<string> warnings, PluginLoadOptions options)
    {
        var candidate = resolved.Candidate;
        var manifest = candidate.Manifest;
        if (!File.Exists(candidate.AssemblyPath))
            throw new PluginLoadException($"its assembly {manifest.AssemblyFile} was not found in {candidate.Directory}.");

        var findings = PluginCodeInspector.Inspect(candidate.AssemblyPath);
        var undeclared = ApplyFindings(findings, manifest.Permissions, PluginPermissions.None, "its assembly", warnings);
        if (findings.EditorReferences.Count > 0)
            warnings.Add($"its assembly references {List(findings.EditorReferences)}, which games do not ship; move editor code into an editorAssembly.");

        if (options.PermissionPolicy == PluginPermissionPolicy.Enforce && undeclared != PluginPermissions.None)
            throw new PluginLoadException($"its code uses undeclared permissions: {PluginPermissionNames.GetName(undeclared)}.");

        var context = PluginActivator.CreateContext(manifest.Id, candidate.AssemblyPath, visible, options);
        try
        {
            var assembly = PluginActivator.LoadAssembly(candidate.AssemblyPath, context.LoadPluginAssembly);
            var constructor = PluginActivator.FindPlugin(assembly);
            var editor = options.LoadEditorAssemblies ? LoadEditor(candidate, context, warnings, ref undeclared) : null;
            Assembly[] own = editor is null ? [assembly] : [assembly, editor];
            return new ActivePlugin(resolved, context, assembly, editor, constructor, [.. own, .. visible], warnings, undeclared);
        }
        catch
        {
            if (context.IsCollectible)
                context.Unload();
            throw;
        }
    }

    private static Assembly? LoadEditor(PluginCandidate candidate, PluginLoadContext context, List<string> warnings, ref PluginPermissions undeclared)
    {
        if (candidate.EditorAssemblyPath is not { } path)
            return null;
        if (!File.Exists(path))
        {
            warnings.Add($"its editor assembly {candidate.Manifest.EditorAssemblyFile} was not found, so its editor features are unavailable.");
            return null;
        }

        undeclared |= ApplyFindings(PluginCodeInspector.Inspect(path), candidate.Manifest.Permissions, PluginPermissions.EditorUi, "its editor assembly", warnings);
        try
        {
            return PluginActivator.LoadAssembly(path, context.LoadEditorAssembly);
        }
        catch (PluginLoadException ex)
        {
            warnings.Add($"its editor features are unavailable because {ex.Message}");
            return null;
        }
    }

    /// <summary>Adds a warning per undeclared permission the code uses, except those in <paramref name="reportedElsewhere"/>.</summary>
    private static PluginPermissions ApplyFindings(
        PluginCodeFindings findings,
        PluginPermissions declared,
        PluginPermissions reportedElsewhere,
        string where,
        List<string> warnings)
    {
        var undeclared = PluginPermissions.None;
        foreach (var permission in PluginPermissionNames.Split(PluginPermissions.FileSystem | PluginPermissions.Network | PluginPermissions.ProcessExecution |
                                                               PluginPermissions.EditorUi | PluginPermissions.RenderBackend))
        {
            if (declared.HasFlag(permission) || !findings.Evidence.TryGetValue(permission, out var types))
                continue;
            undeclared |= permission;
            if (!reportedElsewhere.HasFlag(permission))
                warnings.Add($"{where} uses {List(types)} without declaring the \"{PluginPermissionNames.GetName(permission)}\" permission.");
        }

        return undeclared;
    }

    private static bool IsOptional(ResolvedPlugin plugin, string dependencyId) =>
        plugin.Candidate.Manifest.Dependencies.Any(d => d.Id == dependencyId && d.Optional);

    private static string Describe(ServiceDescriptor descriptor)
    {
        var instance = descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance : descriptor.ImplementationInstance;
        if (instance is SystemDescriptor system)
            return system.Type.Name;
        var type = descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
        return (type ?? instance?.GetType() ?? descriptor.ServiceType).Name;
    }

    private static string List(IReadOnlyList<string> items) =>
        items.Count <= 3 ? string.Join(", ", items) : $"{string.Join(", ", items.Take(3))} and {items.Count - 3} more";
}
