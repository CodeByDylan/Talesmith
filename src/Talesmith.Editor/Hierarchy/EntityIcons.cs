using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Projects;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;

namespace Talesmith.Editor.Hierarchy;

/// <summary>Chooses the icon that stands for an entity in the hierarchy and inspector: that of its primary component.</summary>
/// <remarks>Cameras win; otherwise the first component with an icon other than the transform's and tags'. Prefab instances without such a
/// component show a package.</remarks>
public sealed class EntityIcons(IProjectService project)
{
    private static readonly HashSet<string> Secondary = new(StringComparer.Ordinal) { "Transform", "Tags", "Name" };
    private readonly Dictionary<string, Geometry?> _byType = new(StringComparer.Ordinal);

    public Geometry Get(IReadOnlyList<ComponentDocument> components, bool isPrefabInstance = false)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Any(c => c.Type == "Camera"))
            return Icons.Camera;
        foreach (var component in components)
        {
            if (!Secondary.Contains(component.Type) && Of(component.Type) is { } icon)
                return icon;
        }

        return isPrefabInstance ? Icons.Package : Icons.Box;
    }

    /// <summary>The icon of a component type, or null when its definition names none.</summary>
    public Geometry? Of(string componentType)
    {
        if (_byType.TryGetValue(componentType, out var cached))
            return cached;
        if (Registry is not { } registry)
            return null;
        var icon = registry.Find(componentType)?.Info.Icon is { } name ? Icons.Find(name) : null;
        _byType[componentType] = icon;
        return icon;
    }

    private ComponentRegistry? Registry => project.EditSession?.Game.Services.GetService<ComponentRegistry>();
}
