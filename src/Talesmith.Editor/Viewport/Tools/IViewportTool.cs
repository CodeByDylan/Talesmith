using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>An interactive tool of the scene viewport, such as selecting, moving entities or painting tiles.</summary>
/// <remarks>
/// <para>Register tools with <see cref="ViewportToolServiceCollectionExtensions.AddViewportTool{T}"/>. They appear in the viewport's tool
/// rail, grouped by <see cref="Group"/>, get a command <c>tool.&lt;id&gt;</c> with their <see cref="Shortcut"/>, and show their
/// <see cref="CreateOptionsView"/> in the toolbar while active.</para>
/// <para>The active tool receives the viewport's pointer and key input, except for navigation the viewport handles first: the middle and
/// right buttons and Space+drag pan, and the wheel zooms. Set <see cref="ViewportPointerEventArgs.Handled"/> to keep later handling from
/// running. <see cref="Render"/> draws over the scene in screen coordinates; convert with <see cref="ViewportToolContext.Camera"/>.</para>
/// </remarks>
public interface IViewportTool
{
    /// <summary>A stable identifier such as "select" or "move".</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>A one-line description shown in the tooltip and the status bar.</summary>
    string Description { get; }

    /// <summary>A 24×24 stroke icon geometry.</summary>
    Geometry Icon { get; }

    /// <summary>A shortcut such as "W", or null.</summary>
    string? Shortcut { get; }

    /// <summary>The tool rail group, such as "Transform" or "Tiles"; groups are separated by lines.</summary>
    string Group { get; }

    /// <summary>The position in the rail; lower comes first.</summary>
    int Order { get; }

    /// <summary>The pointer shown over the viewport, or null for the arrow.</summary>
    Cursor? Cursor => null;

    /// <summary>Whether the tool can be used now, such as only while a tile map is selected; unavailable tools are dimmed.</summary>
    bool IsAvailable(ViewportToolContext context) => true;

    /// <summary>Whether a drag or multi-step operation is under way that <see cref="Cancel"/> would abandon.</summary>
    bool IsOperationInProgress => false;

    void Activate(ViewportToolContext context)
    {
    }

    void Deactivate(ViewportToolContext context)
    {
    }

    void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e)
    {
    }

    void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e)
    {
    }

    void PointerReleased(ViewportToolContext context, ViewportPointerEventArgs e)
    {
    }

    /// <summary>Called when the pointer leaves the viewport.</summary>
    void PointerExited(ViewportToolContext context)
    {
    }

    /// <summary>Handles a key press while the viewport has focus; return true when handled.</summary>
    bool KeyDown(ViewportToolContext context, KeyEventArgs e) => false;

    void KeyUp(ViewportToolContext context, KeyEventArgs e)
    {
    }

    /// <summary>Draws the tool's overlay, such as handles or a brush preview, above the scene and the selection outlines.</summary>
    void Render(ViewportToolContext context, DrawingContext drawing)
    {
    }

    /// <summary>Creates the controls shown in the toolbar while the tool is active, or null; called once.</summary>
    Control? CreateOptionsView() => null;

    /// <summary>Abandons an operation in progress, such as when Escape is pressed or another tool is chosen.</summary>
    void Cancel(ViewportToolContext context)
    {
    }
}

public static class ViewportToolServiceCollectionExtensions
{
    public static IServiceCollection AddViewportTool<T>(this IServiceCollection services)
        where T : class, IViewportTool
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IViewportTool, T>();
        return services;
    }
}
