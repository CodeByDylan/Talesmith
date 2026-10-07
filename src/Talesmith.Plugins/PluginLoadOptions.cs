namespace Talesmith.Plugins;

/// <summary>Where plugins are installed and how they are loaded.</summary>
public sealed record PluginLoadOptions
{
    /// <summary>The folder holding one sub-folder per plugin, normally <c>assets/plugins</c>; a missing folder means no plugins.</summary>
    public required string PluginsDirectory { get; init; }

    /// <summary>The game's <c>config/plugins.json</c> with plugin switches and settings; null keeps both in memory only.</summary>
    public string? ConfigurationFile { get; init; }

    /// <summary>Ids of plugins the host switches off regardless of the configuration, for example from the command line.</summary>
    public IReadOnlySet<string> Disabled { get; init; } = PluginConfiguration.Default.Disabled;

    /// <summary>Loads plugins into collectible contexts so they can be unloaded; <see cref="PluginManager"/> always does.</summary>
    public bool Collectible { get; init; }

    /// <summary>Reads plugin assemblies into memory so their files can be rebuilt while the plugin is loaded.</summary>
    /// <remarks><see cref="System.Reflection.Assembly.Location"/> is empty for such assemblies; use <see cref="PluginInfo.Directory"/>.</remarks>
    public bool LoadInMemory { get; init; }

    /// <summary>Also loads each plugin's <c>editorAssembly</c> into the plugin's context. Only the editor sets this; games never load editor code.</summary>
    public bool LoadEditorAssemblies { get; init; }

    /// <summary>Extra assembly names (or name prefixes) to share with the host besides <see cref="PluginLoadContext.DefaultSharedAssemblies"/>.</summary>
    public IReadOnlyCollection<string> SharedAssemblies { get; init; } = [];

    /// <summary>Whether undeclared permissions are only reported or also refused.</summary>
    public PluginPermissionPolicy PermissionPolicy { get; init; } = PluginPermissionPolicy.Warn;

    /// <summary>Service registrations that need a permission; hosts and engine modules can add their own.</summary>
    public IReadOnlyList<PluginRegistrationRule> RegistrationRules { get; init; } = PluginRegistrationRule.Defaults;
}

/// <summary>What happens when a plugin uses a permission it did not declare.</summary>
public enum PluginPermissionPolicy
{
    /// <summary>The use is reported in the load report and <see cref="IPluginPermissions.Violations"/>, and allowed.</summary>
    Warn,

    /// <summary>Plugins whose code or registrations need undeclared permissions fail to load, and permission checks refuse.</summary>
    Enforce
}
