namespace Talesmith.Plugins;

/// <summary>Raised by the host on a game's first frame for each plugin loaded and configured into the game.</summary>
public readonly record struct PluginLoaded(PluginInfo Plugin);

/// <summary>Raised by the host for each of a game's plugins when its session ends, before the game shuts down and the plugin may be unloaded.</summary>
public readonly record struct PluginUnloaded(PluginInfo Plugin);
