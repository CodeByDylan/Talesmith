using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Plugins;

internal sealed class PluginBuilder(IServiceCollection services, PluginInfo plugin, IPluginSettings settings) : IPluginBuilder
{
    public IServiceCollection Services { get; } = services;

    public PluginInfo Plugin { get; } = plugin;

    public IPluginSettings Settings { get; } = settings;
}
