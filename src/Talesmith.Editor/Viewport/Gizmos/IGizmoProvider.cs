using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>Draws gizmos for a kind of component in the scene viewport, such as a light's radius or a collider's outline, and offers handles that
/// edit the component by dragging.</summary>
/// <remarks>
/// <para>Register providers with <see cref="GizmoServiceCollectionExtensions.AddGizmoProvider{T}"/>. Every frame the viewport calls
/// <see cref="Draw"/> on each provider; query <see cref="GizmoContext.World"/> for the provider's component and use
/// <see cref="GizmoContext.IsSelected"/> to draw selected entities fully and others faintly or not at all. Draw in world coordinates through the
/// context's helpers, which keep lines and handles at a constant screen size.</para>
/// <para>Handles are collected for selected entities only. Dragging a handle sets one property of the component through the editor's undoable
/// edits, applied to the viewport at once and committed as one undo step when the drag ends.</para>
/// </remarks>
public interface IGizmoProvider
{
    void Draw(GizmoContext context);

    /// <summary>Adds the handles of the selected entities' components.</summary>
    void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
    {
    }
}

/// <summary>How a handle looks.</summary>
public enum GizmoHandleShape
{
    Square,
    Circle,
    Diamond
}

/// <summary>A draggable point that edits one property of a component.</summary>
/// <param name="Entity">The document id of the entity whose component the handle edits.</param>
/// <param name="Component">The component type name.</param>
/// <param name="Path">The property path within the component's data, as in <c>JsonPaths</c>.</param>
/// <param name="Position">Where the handle is, in world coordinates.</param>
/// <param name="Drag">Computes the property's new value from the drag; return null to leave it unchanged.</param>
public sealed record GizmoHandle(Guid Entity, string Component, string Path, Vector2 Position, Func<GizmoDrag, JsonNode?> Drag)
{
    public GizmoHandleShape Shape { get; init; } = GizmoHandleShape.Square;

    /// <summary>Tells apart handles that edit the same property, such as the width and height handles of a box.</summary>
    public string? Key { get; init; }

    /// <summary>The handle's color; null uses the accent color.</summary>
    public global::Avalonia.Media.Color? Color { get; init; }

    /// <summary>What dragging does, shown in the status bar while hovering, such as "Drag to change the radius".</summary>
    public string? Hint { get; init; }

    public StandardCursorType Cursor { get; init; } = StandardCursorType.SizeAll;
}

/// <summary>The state of a handle drag.</summary>
/// <param name="World">The pointer's world position.</param>
/// <param name="Start">The pointer's world position when the drag started.</param>
/// <param name="StartValue">The property's value when the drag started.</param>
/// <param name="Snap">Whether to snap, because snapping is on or Ctrl is held.</param>
public sealed record GizmoDrag(Vector2 World, Vector2 Start, JsonNode? StartValue, KeyModifiers Modifiers, bool Snap);

public static class GizmoServiceCollectionExtensions
{
    public static IServiceCollection AddGizmoProvider<T>(this IServiceCollection services)
        where T : class, IGizmoProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IGizmoProvider, T>();
        return services;
    }
}
