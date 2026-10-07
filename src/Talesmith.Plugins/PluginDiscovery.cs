namespace Talesmith.Plugins;

/// <summary>Finds plugin folders and reads their manifests.</summary>
internal static class PluginDiscovery
{
    /// <summary>Reads every plugin folder in name order; folders whose manifest is missing or invalid become failed entries.</summary>
    public static List<PluginCandidate> Discover(string pluginsDirectory, List<PluginLoadEntry> failed)
    {
        var candidates = new List<PluginCandidate>();
        var root = Path.GetFullPath(pluginsDirectory);
        if (!Directory.Exists(root))
            return candidates;

        var folders = Directory.GetDirectories(root);
        Array.Sort(folders, StringComparer.Ordinal);
        foreach (var folder in folders)
        {
            var manifestPath = Path.Combine(folder, PluginManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                failed.Add(Failure(folder, $"the folder has no {PluginManifest.FileName}."));
                continue;
            }

            try
            {
                var manifest = PluginManifest.Load(manifestPath);
                candidates.Add(new PluginCandidate(folder, manifest, Inspect(folder, manifest)));
            }
            catch (InvalidDataException ex)
            {
                failed.Add(Failure(folder, ex.Message));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(Failure(folder, $"its {PluginManifest.FileName} could not be read: {ex.Message}"));
            }
        }

        return candidates;
    }

    /// <summary>Checks the manifest against the folder's contents and its own declarations.</summary>
    public static List<string> Inspect(string folder, PluginManifest manifest)
    {
        var warnings = new List<string>();
        if (manifest.EditorAssemblyFile is not null && !manifest.Permissions.HasFlag(PluginPermissions.EditorUi))
            warnings.Add($"it ships an editor assembly ({manifest.EditorAssemblyFile}) but does not declare the \"editorUi\" permission.");
        if (manifest.AssetsFolder is { } assets && !Directory.Exists(Path.Combine(folder, assets)))
            warnings.Add($"its assets folder \"{assets}\" does not exist.");
        if (manifest.IconFile is { } icon && !File.Exists(Path.Combine(folder, icon)))
            warnings.Add($"its icon \"{icon}\" does not exist.");
        return warnings;
    }

    private static PluginLoadEntry Failure(string folder, string reason) => new() { Directory = folder, State = PluginState.Failed, Reason = reason };
}
