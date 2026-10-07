using System.Globalization;
using System.Text;

namespace Talesmith.Plugins;

/// <summary>What happened to an installed plugin.</summary>
public enum PluginState
{
    /// <summary>Not loaded yet; it will load when the next game session starts.</summary>
    Pending,

    /// <summary>Loaded and configured.</summary>
    Loaded,

    /// <summary>Switched off by the user, its manifest or the host.</summary>
    Disabled,

    /// <summary>Not loaded because a requirement is not met: a dependency, the engine version or the contract version.</summary>
    Skipped,

    /// <summary>Broken: an invalid manifest, a duplicate id, or an assembly or <see cref="IPlugin.Configure"/> that failed.</summary>
    Failed
}

/// <summary>One installed plugin in a <see cref="PluginLoadReport"/>.</summary>
/// <remarks>Entries hold no references to plugin code, so keeping a report never prevents a plugin from unloading.</remarks>
public sealed record PluginLoadEntry
{
    /// <summary>The plugin's folder.</summary>
    public required string Directory { get; init; }

    /// <summary>The manifest, or null when it could not be read.</summary>
    public PluginManifest? Manifest { get; init; }

    public required PluginState State { get; init; }

    /// <summary>Why the plugin is not loaded, written for the user; null for loaded and pending plugins.</summary>
    public string? Reason { get; init; }

    /// <summary>Problems that did not stop the plugin, such as undeclared permissions or a missing optional dependency.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Permissions the plugin appears to use without declaring them.</summary>
    public PluginPermissions UndeclaredPermissions { get; init; }

    /// <summary>The full exception text when loading threw, for diagnostics.</summary>
    public string? ErrorDetails { get; init; }

    /// <summary>The plugin as plugins see themselves; set when the manifest was read.</summary>
    public PluginInfo? Info { get; init; }

    /// <summary>Whether the editor part was loaded alongside the plugin.</summary>
    public bool EditorAssemblyLoaded { get; init; }

    public string? Id => Manifest?.Id;

    /// <summary>The manifest name, or the folder name when the manifest could not be read.</summary>
    public string DisplayName => Manifest?.Name ?? Path.GetFileName(Directory);

    /// <summary>The permissions declared in the manifest.</summary>
    public PluginPermissions Permissions => Manifest?.Permissions ?? PluginPermissions.None;
}

/// <summary>The state of every installed plugin; registered as a singleton so the host and editor can show it.</summary>
public sealed class PluginLoadReport
{
    public PluginLoadReport(IReadOnlyList<PluginLoadEntry> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        Plugins = plugins;
        Loaded = [.. plugins.Where(p => p.State == PluginState.Loaded)];
        Failed = [.. plugins.Where(p => p.State == PluginState.Failed)];
        Disabled = [.. plugins.Where(p => p.State == PluginState.Disabled)];
        Skipped = [.. plugins.Where(p => p.State == PluginState.Skipped)];
    }

    public static PluginLoadReport Empty { get; } = new([]);

    /// <summary>Every plugin: loadable ones in load order (dependencies first), then the others by id, then unreadable folders.</summary>
    public IReadOnlyList<PluginLoadEntry> Plugins { get; }

    /// <summary>Configured plugins, in the order they were configured.</summary>
    public IReadOnlyList<PluginLoadEntry> Loaded { get; }

    public IReadOnlyList<PluginLoadEntry> Failed { get; }

    public IReadOnlyList<PluginLoadEntry> Disabled { get; }

    public IReadOnlyList<PluginLoadEntry> Skipped { get; }

    public bool HasFailures => Failed.Count > 0;

    public bool HasWarnings => Plugins.Any(p => p.Warnings.Count > 0);

    public PluginLoadEntry? Find(string id) => Plugins.FirstOrDefault(p => p.Id == id);

    /// <summary>A multi-line, human-readable overview with one line per plugin and one per warning.</summary>
    public string Summary
    {
        get
        {
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture,
                $"Plugins: {Loaded.Count} loaded, {Failed.Count} failed, {Skipped.Count} skipped, {Disabled.Count} disabled");
            foreach (var plugin in Plugins)
            {
                var name = plugin.Manifest is { } manifest ? $"{manifest.Id} {PluginVersion.Format(manifest.Version)}" : Path.GetFileName(plugin.Directory);
                var state = plugin.State.ToString().ToLowerInvariant();
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"  {state,-9} {name}");
                if (plugin.Reason is not null)
                    text.Append(CultureInfo.InvariantCulture, $": {plugin.Reason}");
                foreach (var warning in plugin.Warnings)
                    text.AppendLine().Append(CultureInfo.InvariantCulture, $"            warning: {warning}");
            }

            return text.ToString();
        }
    }

    public override string ToString() => Summary;
}
