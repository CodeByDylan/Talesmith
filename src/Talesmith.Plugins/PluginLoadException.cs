namespace Talesmith.Plugins;

/// <summary>Raised inside the loader when a single plugin cannot be loaded; it becomes a failed entry of the <see cref="PluginLoadReport"/>.</summary>
internal sealed class PluginLoadException(string message, Exception? innerException = null) : Exception(message, innerException);
