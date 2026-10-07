using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.Logging;
using Talesmith.Ecs;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>The component gizmos of the viewport: draws every <see cref="IGizmoProvider"/>, lets the pointer drag their handles, and picks
/// entities for the inspector's eyedropper.</summary>
/// <remarks>The viewport overlay gives it pointer input before the active tool, so handles work with any tool. A handle drag applies each step to
/// the scene and the viewport at once and becomes one undo step when released; Escape cancels it.</remarks>
public sealed partial class GizmoLayer(IEnumerable<IGizmoProvider> providers, IEditWorld editWorld, ISelectionService selection, EntityDataService entities,
    IUndoService undo, ViewportOptions options, EntityPicker picker, ILogger<GizmoLayer> logger, EditorPluginGuard plugins)
{
    private const double HandleRadius = 8;

    private readonly IGizmoProvider[] _providers = [.. providers];
    private readonly List<GizmoHandle> _handles = [];
    private readonly HashSet<Entity> _selected = [];
    private GizmoHandle? _hovered;
    private Drag? _drag;
    private Action<Guid?>? _pick;
    private Guid? _pickHover;

    /// <summary>The handles collected by the last <see cref="Render"/>.</summary>
    internal IReadOnlyList<GizmoHandle> Handles => _handles;

    /// <summary>Whether a handle is being dragged.</summary>
    public bool IsDragging => _drag is not null;

    /// <summary>Whether the next click in the viewport picks an entity for the eyedropper.</summary>
    public bool IsPicking => _pick is not null;

    /// <summary>The pointer to show while over a handle, dragging one or picking; null leaves it to the active tool.</summary>
    public Cursor? Cursor => _pick is not null ? new Cursor(StandardCursorType.Cross)
        : (_drag?.Handle ?? _hovered) is { } handle ? new Cursor(handle.Cursor)
        : null;

    /// <summary>Raised when the layer needs the overlay drawn again, such as when picking starts.</summary>
    public event EventHandler? InvalidateRequested;

    /// <summary>Makes the next click in the viewport pick an entity, or null for empty space; Escape picks nothing and does not call back.</summary>
    public void BeginPick(Action<Guid?> picked)
    {
        ArgumentNullException.ThrowIfNull(picked);
        _pick = picked;
        InvalidateRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Render(ViewportToolContext tools, DrawingContext drawing)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(drawing);
        if (editWorld.World is not { } world)
            return;
        _selected.Clear();
        foreach (var id in selection.Entities)
        {
            if (editWorld.TryGetEntity(id, out var entity))
                _selected.Add(entity);
        }

        var hovered = tools.Hovered is { } hover && editWorld.TryGetEntity(hover, out var hoveredEntity) ? hoveredEntity : Entity.Null;
        var context = new GizmoContext(drawing, tools.Camera, world, editWorld, _selected, hovered);
        _handles.Clear();
        foreach (var provider in _providers)
        {
            if (plugins.IsFaulted(provider))
                continue;
            try
            {
                provider.Draw(context);
                if (_selected.Count > 0)
                    provider.CollectHandles(context, _handles);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                if (!plugins.Isolate(provider, "draw its gizmos", ex))
                    LogProviderFailed(logger, ex, provider.GetType().Name);
            }
        }

        var active = _drag?.Handle;
        foreach (var handle in _handles)
        {
            var highlighted = active is not null ? Same(handle, active) : _hovered is not null && Same(handle, _hovered);
            context.Handle(handle.Position, handle.Color ?? context.Palette.Accent, handle.Shape, highlighted);
        }

        if (_pick is not null && _pickHover is { } target && picker.Collect(tools.Camera.Zoom).FirstOrDefault(v => v.DocumentId == target) is { } visual)
        {
            Span<Vector2> corners = visual.Corners;
            context.Polyline(corners, context.Palette.Highlight, closed: true, thickness: 2, fillOpacity: 0.12);
        }
    }

    /// <summary>Starts a handle drag or a pick; returns whether the press was used.</summary>
    public bool PointerPressed(ViewportToolContext tools, ViewportPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(e);
        if (!e.Properties.IsLeftButtonPressed)
            return false;
        if (_pick is { } pick)
        {
            _pick = null;
            _pickHover = null;
            tools.Hint = null;
            pick(picker.HitTest(e.World, tools.Camera.Zoom));
            e.Handled = true;
            tools.Invalidate();
            return true;
        }

        if (HitTest(tools, e.Position) is not { } handle)
            return false;
        var start = entities.Get(handle.Entity, handle.Component, handle.Path)?.DeepClone();
        _drag = new Drag(handle, e.World, start, undo.BeginTransaction($"Edit {ShortType(handle.Component)} {handle.Path}"));
        e.Source.Pointer.Capture(tools.View);
        e.Handled = true;
        tools.Hint = handle.Hint;
        tools.Invalidate();
        return true;
    }

    /// <summary>Moves a dragged handle or updates which handle is under the pointer; returns whether the move was used.</summary>
    public bool PointerMoved(ViewportToolContext tools, ViewportPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(e);
        if (_pick is not null)
        {
            var target = picker.HitTest(e.World, tools.Camera.Zoom);
            if (target != _pickHover)
            {
                _pickHover = target;
                tools.Invalidate();
            }

            tools.Hint = "Click an entity to pick it; Escape cancels";
            return true;
        }

        if (_drag is { } drag)
        {
            var snap = options.SnapToGrid || (e.Modifiers & KeyModifiers.Control) != 0;
            try
            {
                if (drag.Handle.Drag(new GizmoDrag(e.World, drag.Start, drag.StartValue, e.Modifiers, snap)) is { } value)
                    entities.Set(drag.Handle.Entity, drag.Handle.Component, drag.Handle.Path, value);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogProviderFailed(logger, ex, drag.Handle.Component);
            }

            e.Handled = true;
            tools.Invalidate();
            return true;
        }

        var hovered = HitTest(tools, e.Position);
        if (!Same(hovered, _hovered))
        {
            _hovered = hovered;
            if (hovered?.Hint is { } hint)
                tools.Hint = hint;
            tools.Invalidate();
        }

        return hovered is not null;
    }

    /// <summary>Ends a handle drag; returns whether the release was used.</summary>
    public bool PointerReleased(ViewportToolContext tools, ViewportPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(e);
        if (_drag is not { } drag)
            return false;
        _drag = null;
        drag.Transaction.Dispose();
        undo.Seal();
        e.Source.Pointer.Capture(null);
        e.Handled = true;
        tools.Invalidate();
        return true;
    }

    /// <summary>Abandons a drag or a pick; returns whether there was one.</summary>
    public bool Cancel()
    {
        if (_drag is { } drag)
        {
            _drag = null;
            drag.Transaction.Cancel();
            InvalidateRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (_pick is null)
            return false;
        _pick = null;
        _pickHover = null;
        InvalidateRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private GizmoHandle? HitTest(ViewportToolContext tools, Point position)
    {
        GizmoHandle? best = null;
        var bestDistance = HandleRadius;
        for (var i = _handles.Count - 1; i >= 0; i--)
        {
            var distance = Point.Distance(tools.ToScreen(_handles[i].Position), position);
            if (distance <= bestDistance)
            {
                best = _handles[i];
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool Same(GizmoHandle? a, GizmoHandle? b) =>
        a is not null && b is not null && a.Entity == b.Entity && a.Component == b.Component && a.Path == b.Path && a.Key == b.Key;

    private static string ShortType(string type) => type[(type.LastIndexOf('.') + 1)..];

    [LoggerMessage(Level = LogLevel.Warning, Message = "The gizmo of {Provider} failed")]
    private static partial void LogProviderFailed(ILogger logger, Exception exception, string provider);

    private sealed record Drag(GizmoHandle Handle, Vector2 Start, JsonNode? StartValue, UndoTransaction Transaction);
}
