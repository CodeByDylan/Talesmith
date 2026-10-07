namespace Talesmith.Plugins;

/// <summary>A plugin's settings: JSON values by key, stored with the game's plugin configuration.</summary>
/// <remarks>Values are serialized with System.Text.Json using camelCase names. Changes are kept in memory until <see cref="Save"/>.</remarks>
public interface IPluginSettings
{
    string PluginId { get; }

    IReadOnlyCollection<string> Keys { get; }

    bool Contains(string key);

    /// <summary>Reads a value; returns false when the key is missing or its value cannot be read as <typeparamref name="T"/>.</summary>
    bool TryGet<T>(string key, out T value);

    /// <summary>Reads a value, or returns <paramref name="fallback"/> when it is missing or cannot be read as <typeparamref name="T"/>.</summary>
    T Get<T>(string key, T fallback);

    void Set<T>(string key, T value);

    bool Remove(string key);

    /// <summary>Writes the settings to the game's plugin configuration; does nothing for settings that are not backed by a file.</summary>
    /// <exception cref="IOException">The configuration file cannot be written.</exception>
    void Save();
}
