namespace Talesmith.Plugins;

/// <summary>An installed plugin whose manifest was read successfully.</summary>
/// <param name="Warnings">Problems found in the folder that do not stop the plugin from loading.</param>
internal sealed record PluginCandidate(string Directory, PluginManifest Manifest, IReadOnlyList<string> Warnings)
{
    public PluginCandidate(string directory, PluginManifest manifest)
        : this(directory, manifest, [])
    {
    }

    public string Id => Manifest.Id;

    public string AssemblyPath => Path.Combine(Directory, Manifest.AssemblyFile);

    public string? EditorAssemblyPath => Manifest.EditorAssemblyFile is { } file ? Path.Combine(Directory, file) : null;

    public PluginInfo ToInfo() => new(Manifest.Id, Manifest.Name, Manifest.Version, Directory)
    {
        Permissions = Manifest.Permissions,
        AssetsDirectory = Manifest.AssetsFolder is { } assets ? Path.GetFullPath(Path.Combine(Directory, assets)) : null
    };
}
