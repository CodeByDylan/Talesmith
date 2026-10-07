using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Plugins;

/// <summary>The entry point of a plugin: registers its services, systems, importers, render passes and UI with the engine.</summary>
/// <remarks>
/// A plugin assembly contains exactly one public, parameterless class implementing this interface. A new instance is created each time a
/// game is built, before the service provider exists, so <see cref="Configure"/> can only register services. Use hosted startup services
/// or systems for work that needs other services.
/// </remarks>
public interface IPlugin
{
    void Configure(IPluginBuilder builder);
}

/// <summary>What a plugin can use while it registers itself.</summary>
public interface IPluginBuilder
{
    /// <summary>The engine's services; register systems with <c>AddSystem</c> and anything else the usual way.</summary>
    IServiceCollection Services { get; }

    /// <summary>The plugin's manifest data.</summary>
    PluginInfo Plugin { get; }

    /// <summary>The plugin's own settings, stored with the game's plugin configuration.</summary>
    IPluginSettings Settings { get; }
}

/// <summary>Identifies a loaded plugin.</summary>
/// <param name="Id">A unique, stable id such as "talesmith.cutscenes".</param>
/// <param name="Directory">The folder the plugin was loaded from, for plugin-specific files.</param>
public sealed record PluginInfo(string Id, string Name, Version Version, string Directory)
{
    /// <summary>The permissions the plugin declared in its manifest.</summary>
    public PluginPermissions Permissions { get; init; }

    /// <summary>The full path of the plugin's assets folder, or null when it ships none.</summary>
    public string? AssetsDirectory { get; init; }
}
