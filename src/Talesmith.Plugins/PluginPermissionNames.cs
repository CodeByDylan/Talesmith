namespace Talesmith.Plugins;

/// <summary>Names of <see cref="PluginPermissions"/> as written in manifests and shown to users.</summary>
public static class PluginPermissionNames
{
    private static readonly (PluginPermissions Permission, string Name, string DisplayName)[] Entries =
    [
        (PluginPermissions.FileSystem, "fileSystem", "File system access"),
        (PluginPermissions.Network, "network", "Network access"),
        (PluginPermissions.ProcessExecution, "processExecution", "Process execution"),
        (PluginPermissions.EditorUi, "editorUi", "Editor UI access"),
        (PluginPermissions.RuntimeScene, "runtimeScene", "Runtime scene access"),
        (PluginPermissions.AssetWrite, "assetWrite", "Asset write access"),
        (PluginPermissions.RenderBackend, "renderBackend", "Render backend access")
    ];

    /// <summary>Every manifest name, such as "fileSystem".</summary>
    public static IReadOnlyList<string> All { get; } = [.. Entries.Select(e => e.Name)];

    /// <summary>The single permissions contained in <paramref name="permissions"/>, in declaration order.</summary>
    public static IEnumerable<PluginPermissions> Split(PluginPermissions permissions) =>
        Entries.Where(e => permissions.HasFlag(e.Permission)).Select(e => e.Permission);

    /// <summary>The manifest name of a single permission, or a comma-separated list for several.</summary>
    public static string GetName(PluginPermissions permissions) =>
        string.Join(", ", Entries.Where(e => permissions.HasFlag(e.Permission)).Select(e => e.Name));

    /// <summary>A readable name such as "Network access", or a comma-separated list for several.</summary>
    public static string GetDisplayName(PluginPermissions permissions) =>
        string.Join(", ", Entries.Where(e => permissions.HasFlag(e.Permission)).Select(e => e.DisplayName));

    public static bool TryParse(string name, out PluginPermissions permission)
    {
        foreach (var entry in Entries)
        {
            if (entry.Name == name)
            {
                permission = entry.Permission;
                return true;
            }
        }

        permission = PluginPermissions.None;
        return false;
    }
}
