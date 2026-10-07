using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Viewport;

/// <summary>An icon drawn at an entity's position in the viewport, so entities without visuals, such as cameras, lights and sound sources,
/// can be seen and picked.</summary>
/// <param name="Icon">A 24×24 stroke icon geometry from <c>Talesmith.UI.Icons</c>.</param>
/// <param name="Color">The icon's color; null uses the theme's text color.</param>
public readonly record struct EntityIcon(Geometry Icon, Color? Color = null);

/// <summary>Chooses the viewport icon of entities that have no sprite or tile map.</summary>
/// <remarks>Register with <see cref="EntityIconServiceCollectionExtensions.AddEntityIconProvider{T}"/>. Providers are asked from the highest
/// <see cref="Priority"/> down and the first icon wins; the built-in provider, at priority 0, uses the icon of the entity's first component
/// whose <see cref="ComponentInfo.Icon"/> is set.</remarks>
public interface IEntityIconProvider
{
    int Priority => 0;

    EntityIcon? GetIcon(World world, Entity entity);
}

/// <summary>Uses the icon named by the component definitions of the entity's components, cameras first.</summary>
public sealed class ComponentIconProvider(IEditWorld editWorld) : IEntityIconProvider
{
    private readonly Dictionary<int, Geometry?> _byComponent = [];

    public EntityIcon? GetIcon(World world, Entity entity)
    {
        if (editWorld.Game?.Services.GetService<ComponentRegistry>() is not { } registry)
            return null;
        if (world.Has<Runtime.Components.Camera>(entity))
            return new EntityIcon(UI.Icons.Camera);
        Geometry? found = null;
        foreach (var id in world.GetComponentIds(entity))
        {
            if (!_byComponent.TryGetValue(id, out var icon))
            {
                var type = ComponentType.FromId(id).Type;
                icon = registry.TryGet(type, out var definition) && !definition.Info.Hidden && definition.Info.Icon is { } name && name != "move"
                    ? UI.Icons.Find(name)
                    : null;
                _byComponent[id] = icon;
            }

            found ??= icon;
        }

        return found is null ? null : new EntityIcon(found);
    }
}

public static class EntityIconServiceCollectionExtensions
{
    public static IServiceCollection AddEntityIconProvider<T>(this IServiceCollection services)
        where T : class, IEntityIconProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEntityIconProvider, T>();
        return services;
    }
}
