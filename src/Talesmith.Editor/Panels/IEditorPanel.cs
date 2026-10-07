using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Editor.Panels;

/// <summary>Where a panel goes in the default layout.</summary>
public enum DockLocation
{
    Left,

    /// <summary>The document area with the scene viewport.</summary>
    Center,

    Right,

    Bottom
}

/// <summary>A panel of the dock workspace, such as the hierarchy or the console.</summary>
/// <remarks>
/// Register panels with <see cref="PanelServiceCollectionExtensions.AddEditorPanel{T}"/>. A panel is created through dependency injection the
/// first time it is shown, and its content is created once and kept while the panel is moved, hidden behind another tab or closed.
/// </remarks>
public interface IEditorPanel
{
    /// <summary>Creates the panel's view; called once.</summary>
    Control CreateContent();

    /// <summary>Creates small controls shown at the right of the tab strip while the panel is active, such as icon buttons; called once.</summary>
    Control? CreateHeaderActions() => null;
}

/// <summary>How a panel is listed and placed: its id in layouts, title, icon and place in the default layout.</summary>
/// <param name="Id">A stable id such as "console"; layouts refer to panels by it. See <see cref="PanelIds"/> for the built-in ones.</param>
public sealed record EditorPanelInfo(string Id, string Title, Geometry? Icon, DockLocation Location)
{
    /// <summary>Whether the tab has a close button.</summary>
    public bool CanClose { get; init; } = true;

    /// <summary>The position among the panels of the same location; lower comes first.</summary>
    public int Order { get; init; }

    /// <summary>A shortcut that shows the panel, such as "Ctrl+Shift+C", or null.</summary>
    public string? Shortcut { get; init; }

    /// <summary>Whether the default layout includes the panel; otherwise it opens from the Window menu.</summary>
    public bool OpenByDefault { get; init; } = true;
}

/// <summary>A panel type with its info, as registered.</summary>
public sealed record EditorPanelRegistration(EditorPanelInfo Info, Type PanelType)
{
    /// <summary>The key of a keyed service registration of the panel, or null for a plain one.</summary>
    public object? ServiceKey { get; init; }
}

/// <summary>The ids of the built-in panels.</summary>
public static class PanelIds
{
    public const string Hierarchy = "hierarchy";
    public const string Scene = "scene";
    public const string Game = "game";
    public const string Inspector = "inspector";
    public const string Assets = "assets";
    public const string Console = "console";
    public const string History = "history";
    public const string TileMap = "tilemap";
    public const string Particles = "particles";
    public const string Lighting = "lighting";
    public const string Plugins = "plugins";
}

public static class PanelServiceCollectionExtensions
{
    /// <summary>Registers a panel. A later registration with the same id replaces an earlier one, so plugins can replace built-in panels.</summary>
    public static IServiceCollection AddEditorPanel<T>(this IServiceCollection services, EditorPanelInfo info)
        where T : class, IEditorPanel
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(info);
        services.AddSingleton<T>();
        services.AddSingleton(new EditorPanelRegistration(info, typeof(T)));
        return services;
    }
}
