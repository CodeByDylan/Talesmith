using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Talesmith.Plugins;

/// <summary>Adds installed plugins to dependency injection.</summary>
public static class PluginServiceCollectionExtensions
{
    /// <summary>Loads the plugins with <see cref="PluginLoader.Load"/>; the report is also registered as a singleton.</summary>
    /// <exception cref="InvalidDataException">The plugin configuration file is not valid.</exception>
    public static PluginLoadReport AddTalesmithPlugins(this IServiceCollection services, PluginLoadOptions options, ILogger logger) =>
        PluginLoader.Load(options, services, logger);

    /// <summary>Configures the plugins kept loaded by <paramref name="manager"/>; the report is also registered as a singleton.</summary>
    public static PluginLoadReport AddTalesmithPlugins(this IServiceCollection services, PluginManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.Configure(services);
    }
}
