using Talesmith.Systems;

namespace Talesmith.Plugins;

/// <summary>Says that registering services of certain types needs a permission; checked against every service a plugin registers.</summary>
/// <param name="Description">What such registrations add, for warnings, such as "systems".</param>
/// <param name="Matches">Whether a registered service type falls under the rule.</param>
public sealed record PluginRegistrationRule(PluginPermissions Permission, string Description, Func<Type, bool> Matches)
{
    /// <summary>Systems, scenes and scene listeners need <see cref="PluginPermissions.RuntimeScene"/>; renderers and render passes need
    /// <see cref="PluginPermissions.RenderBackend"/>; editor services need <see cref="PluginPermissions.EditorUi"/>.</summary>
    public static IReadOnlyList<PluginRegistrationRule> Defaults { get; } =
    [
        ForType(typeof(SystemDescriptor), PluginPermissions.RuntimeScene, "systems"),
        ForTypeName("Talesmith.Runtime.Scenes.SceneRegistration", PluginPermissions.RuntimeScene, "scenes"),
        ForTypeName("Talesmith.Runtime.Scenes.ISceneListener", PluginPermissions.RuntimeScene, "scene listeners"),
        ForTypeName("Talesmith.Rendering.IRenderer", PluginPermissions.RenderBackend, "a renderer"),
        ForTypeName("Talesmith.Rendering.IOffscreenRenderer", PluginPermissions.RenderBackend, "an offscreen renderer"),
        new(PluginPermissions.RenderBackend, "render passes",
            type => IsInNamespace(type, "Talesmith") && type.Name.EndsWith("RenderPass", StringComparison.Ordinal)),
        ForNamespace("Talesmith.Editor", PluginPermissions.EditorUi, "editor services"),
        ForNamespace("Talesmith.UI", PluginPermissions.EditorUi, "editor UI services")
    ];

    /// <summary>Matches registrations of <paramref name="serviceType"/> and of open or closed generic forms of it.</summary>
    public static PluginRegistrationRule ForType(Type serviceType, PluginPermissions permission, string description)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return new PluginRegistrationRule(permission, description,
            type => type == serviceType || (type.IsGenericType && type.GetGenericTypeDefinition() == serviceType));
    }

    /// <summary>Matches registrations by the service type's full name, for types the plugin system does not reference.</summary>
    public static PluginRegistrationRule ForTypeName(string fullName, PluginPermissions permission, string description) =>
        new(permission, description, type => (type.IsGenericType ? type.GetGenericTypeDefinition() : type).FullName == fullName);

    /// <summary>Matches registrations of any service type in the namespace or below it.</summary>
    public static PluginRegistrationRule ForNamespace(string ns, PluginPermissions permission, string description) =>
        new(permission, description, type => IsInNamespace(type, ns));

    private static bool IsInNamespace(Type type, string ns) =>
        type.Namespace is { } name && name.StartsWith(ns, StringComparison.Ordinal) && (name.Length == ns.Length || name[ns.Length] == '.');
}
