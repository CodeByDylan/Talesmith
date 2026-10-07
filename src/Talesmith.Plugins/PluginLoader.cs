using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Talesmith.Plugins;

/// <summary>Discovers, validates, orders and configures the installed plugins of a game.</summary>
/// <remarks>
/// Each plugin folder holds a <c>plugin.json</c>, the entry assembly and its <c>.deps.json</c> (build plugins with
/// <c>EnableDynamicLoading</c> and reference Talesmith with <c>Private=false</c>). Plugins are configured dependencies first; a plugin
/// that fails, and every plugin that requires it, is reported without affecting the others. Hosts that rebuild games and unload plugins,
/// such as the editor, use <see cref="PluginManager"/> instead.
/// </remarks>
public static class PluginLoader
{
    /// <summary>Loads every enabled plugin, lets it register into <paramref name="services"/>, and registers the resulting report,
    /// <see cref="IPluginPermissions"/> and each plugin's <see cref="IPluginSettings"/> keyed by plugin id.</summary>
    /// <exception cref="InvalidDataException">The plugin configuration file is not valid.</exception>
    public static PluginLoadReport Load(PluginLoadOptions options, IServiceCollection services, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);

        var store = options.ConfigurationFile is { } path ? PluginConfigurationStore.Open(path) : PluginConfigurationStore.InMemory();
        var generation = PluginGeneration.Load(Resolve(options, store.Saved), options, logger);
        var report = generation.Configure(services, id => new PluginSettings(id, store));
        Log(report, logger);
        return report;
    }

    /// <summary>Reads the installed plugins and decides which load, without loading anything.</summary>
    public static PluginLoadReport Scan(PluginLoadOptions options, PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        return new PluginLoadReport(Resolve(options, configuration).Entries);
    }

    internal static PluginResolution Resolve(PluginLoadOptions options, PluginConfiguration configuration)
    {
        var unreadable = new List<PluginLoadEntry>();
        var candidates = PluginDiscovery.Discover(options.PluginsDirectory, unreadable);
        return PluginResolver.Resolve(candidates, unreadable, configuration, options.Disabled, EngineInfo.Version, EngineInfo.ContractVersion);
    }

    internal static void Log(PluginLoadReport report, ILogger logger)
    {
        foreach (var plugin in report.Plugins)
        {
            var name = plugin.Id ?? plugin.Directory;
            switch (plugin.State)
            {
                case PluginState.Loaded:
                    PluginLog.Loaded(logger, name, plugin.Manifest!.Version, plugin.Directory);
                    break;
                case PluginState.Disabled:
                    PluginLog.Disabled(logger, name, plugin.Reason ?? "");
                    break;
                case PluginState.Skipped:
                    PluginLog.Skipped(logger, name, plugin.Reason ?? "");
                    break;
                case PluginState.Failed:
                    PluginLog.Failed(logger, null, name, plugin.ErrorDetails is null ? plugin.Reason ?? "" : $"{plugin.Reason}{Environment.NewLine}{plugin.ErrorDetails}");
                    break;
            }

            foreach (var warning in plugin.Warnings)
                PluginLog.Warning(logger, name, warning);
        }

        PluginLog.Summary(logger, report.Loaded.Count, report.Failed.Count, report.Skipped.Count, report.Disabled.Count);
    }
}
