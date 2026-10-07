namespace Talesmith.Plugins;

/// <summary>Lets engine services and plugins check whether a plugin declared a permission before doing something on its behalf.</summary>
/// <remarks>
/// Checks record a <see cref="PluginPermissionViolation"/> when the plugin did not declare the permission. Whether the operation is then
/// refused depends on the host's policy; by default violations are only reported.
/// </remarks>
public interface IPluginPermissions
{
    /// <summary>Violations recorded so far, each operation once per plugin and permission.</summary>
    IReadOnlyList<PluginPermissionViolation> Violations { get; }

    /// <summary>Raised the first time a plugin uses an undeclared permission for an operation.</summary>
    event EventHandler<PluginPermissionViolation>? ViolationRecorded;

    /// <summary>The permissions the plugin declared, or <see cref="PluginPermissions.None"/> for an unknown plugin.</summary>
    PluginPermissions GetDeclared(string pluginId);

    /// <summary>The id of the plugin whose code defines <paramref name="type"/>, or null for engine and host types.</summary>
    string? FindPlugin(Type type);

    /// <summary>Checks that the plugin declared every permission in <paramref name="permission"/>.</summary>
    /// <param name="operation">What is being done, for the report, such as "register render pass Bloom".</param>
    /// <returns>Whether the operation may go ahead.</returns>
    bool Check(string pluginId, PluginPermissions permission, string operation);

    /// <summary>Checks on behalf of the plugin that defines <paramref name="caller"/>; engine and host types always pass.</summary>
    bool Check(Type caller, PluginPermissions permission, string operation);
}

/// <summary>A plugin used a permission it did not declare.</summary>
/// <param name="Missing">The permissions that were needed but not declared.</param>
/// <param name="Denied">Whether the operation was refused rather than only reported.</param>
public sealed record PluginPermissionViolation(string PluginId, PluginPermissions Missing, string Operation, bool Denied);
