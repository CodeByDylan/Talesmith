namespace Talesmith.Plugins;

/// <summary>The contents of a plugin's <c>plugin.json</c>.</summary>
public sealed record PluginManifest
{
    /// <summary>The manifest file name every plugin folder contains.</summary>
    public const string FileName = "plugin.json";

    /// <summary>A unique, stable id such as "talesmith.cutscenes".</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required Version Version { get; init; }

    public string Description { get; init; } = "";

    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>The file name of the entry assembly inside the plugin folder.</summary>
    public required string AssemblyFile { get; init; }

    /// <summary>The file name of the editor-only assembly, which games never load, or null.</summary>
    public string? EditorAssemblyFile { get; init; }

    /// <summary>The <see cref="EngineInfo.ContractVersion"/> the plugin was built against.</summary>
    public required int ContractVersion { get; init; }

    /// <summary>The oldest <see cref="EngineInfo.Version"/> the plugin runs on, or null for any.</summary>
    public Version? MinEngineVersion { get; init; }

    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];

    public PluginPermissions Permissions { get; init; }

    /// <summary>Ids of the extension points the plugin contributes to, such as "systems" or "editor.panels"; see <see cref="PluginExtensionPoints"/>.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>The plugin's assets folder, relative to the plugin folder, or null.</summary>
    public string? AssetsFolder { get; init; }

    /// <summary>The plugin's icon file, relative to the plugin folder, or null.</summary>
    public string? IconFile { get; init; }

    public Uri? Homepage { get; init; }

    /// <summary>The license, ideally as an SPDX expression such as "MIT".</summary>
    public string? License { get; init; }

    /// <summary>False keeps the plugin installed but unloaded unless the game's plugin configuration enables it.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Reads and validates a manifest file.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON or breaks a manifest rule; the message lists every problem.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static PluginManifest Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return PluginManifestParser.Parse(File.ReadAllBytes(path), path);
    }

    /// <summary>Parses and validates manifest JSON; <paramref name="source"/> names it in error messages.</summary>
    /// <exception cref="InvalidDataException">The JSON is not valid or breaks a manifest rule; the message lists every problem.</exception>
    public static PluginManifest Parse(string json, string source = FileName)
    {
        ArgumentNullException.ThrowIfNull(json);
        return PluginManifestParser.Parse(System.Text.Encoding.UTF8.GetBytes(json), source);
    }
}

/// <summary>Another plugin this plugin uses; required dependencies are configured first and must be loaded.</summary>
/// <param name="Versions">The acceptable versions of the dependency.</param>
/// <param name="Optional">When true the plugin also loads without the dependency, but is configured after it when it is present.</param>
public sealed record PluginDependency(string Id, VersionRange Versions, bool Optional = false);
