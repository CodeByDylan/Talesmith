using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>The common part of the move, rotate, scale and rect tools: handles around the selection's pivot that are hit-tested in screen space,
/// a drag that applies each step to the scene at once and becomes one undo step on release, and clicking and marquee selection elsewhere.</summary>
public abstract class TransformTool(EntityDataService entities) : IViewportTool
{
    public const string ToolGroup = "Transform";

    private static readonly Typeface ReadoutFace = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private readonly SelectTool _select = new();
    private UndoTransaction? _transaction;
    private Cursor? _cursor;

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract Geometry Icon { get; }

    public abstract string? Shortcut { get; }

    public string Group => ToolGroup;

    public abstract int Order { get; }

    public Cursor? Cursor => _cursor;

    public bool IsOperationInProgress => Selection is not null || _select.IsOperationInProgress;

    /// <summary>The handle part under the pointer, or null.</summary>
    protected int? Hovered { get; private set; }

    /// <summary>The handle part being dragged, or null.</summary>
    protected int? Active { get; private set; }

    /// <summary>The selection as it was when the drag started; null while not dragging.</summary>
    internal TransformSelection? Selection { get; private set; }

    /// <summary>The pointer's world position when the drag started.</summary>
    protected Vector2 Start { get; private set; }

    /// <summary>The pointer's screen position when the drag started.</summary>
    protected Point StartScreen { get; private set; }

    /// <summary>A short text drawn next to the pointer while dragging, such as the distance moved.</summary>
    protected string? Readout { get; set; }

    protected EntityDataService Entities => entities;

    public void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);
        if (e.Properties.IsLeftButtonPressed && TransformSelection.Capture(context, entities) is { } selection && HitTest(context, selection, e.Position) is { } part)
        {
            Selection = selection;
            Active = part;
            Start = e.World;
            StartScreen = e.Position;
            _transaction = context.Undo.BeginTransaction(Describe(selection));
            e.Source.Pointer.Capture(context.View);
            e.Handled = true;
            context.Hovered = null;
            context.Invalidate();
            return;
        }

        _select.PointerPressed(context, e);
    }

    public void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);
        if (Selection is { } selection && Active is { } part)
        {
            Drag(context, selection, part, e);
            e.Handled = true;
            context.Invalidate();
            return;
        }

        var hovered = !_select.IsOperationInProgress && TransformSelection.Capture(context, entities) is { } current ? HitTest(context, current, e.Position) : null;
        if (hovered != Hovered)
        {
            Hovered = hovered;
            _cursor = hovered is { } h ? CursorFor(h) : null;
            context.Hint = hovered is not null ? HintFor(hovered.Value) : Description;
            context.Invalidate();
        }

        if (hovered is null)
            _select.PointerMoved(context, e);
    }

    public void PointerReleased(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(e);
        if (Selection is null)
        {
            _select.PointerReleased(context, e);
            return;
        }

        _transaction?.Dispose();
        _transaction = null;
        context.Undo.Seal();
        End(context);
        e.Source.Pointer.Capture(null);
        e.Handled = true;
    }

    public void PointerExited(ViewportToolContext context)
    {
        _select.PointerExited(context);
        if (Hovered is null)
            return;
        Hovered = null;
        _cursor = null;
        context.Invalidate();
    }

    public void Cancel(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _transaction?.Cancel();
        _transaction = null;
        End(context);
        _select.Cancel(context);
    }

    public void Deactivate(ViewportToolContext context)
    {
        Hovered = null;
        _cursor = null;
    }

    public void Render(ViewportToolContext context, DrawingContext drawing)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(drawing);
        _select.Render(context, drawing);
        var selection = TransformSelection.Capture(context, entities);
        if (selection is null)
            return;
        DrawHandles(context, drawing, Selection ?? selection, selection, GizmoPalette.Current);
        if (Readout is { } text && Active is not null)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ReadoutFace, 11, Brushes.White);
            var at = context.ToScreen(selection.Pivot) + new global::Avalonia.Vector(18, -30);
            var box = new Rect(at.X - 6, at.Y - 3, formatted.Width + 12, formatted.Height + 6);
            drawing.DrawRectangle(GizmoContext.Fill(Color.FromArgb(220, 20, 21, 26), 1), null, box, 5, 5);
            drawing.DrawText(formatted, at);
        }
    }

    /// <summary>Which handle part is at a screen position, or null.</summary>
    internal abstract int? HitTest(ViewportToolContext context, TransformSelection selection, Point position);

    /// <summary>Applies a drag step of a handle part.</summary>
    internal abstract void Drag(ViewportToolContext context, TransformSelection start, int part, ViewportPointerEventArgs e);

    /// <summary>Draws the handles; <paramref name="start"/> is the selection when the drag started, <paramref name="current"/> as it is now.</summary>
    internal abstract void DrawHandles(ViewportToolContext context, DrawingContext drawing, TransformSelection start, TransformSelection current, GizmoPalette palette);

    protected abstract string Verb { get; }

    protected virtual Cursor CursorFor(int part) => new(StandardCursorType.SizeAll);

    protected virtual string HintFor(int part) => Description;

    private string Describe(TransformSelection selection) =>
        selection.Entities.Count == 1 ? $"{Verb} {entities.GetName(selection.Entities[0].Id)}" : $"{Verb} {selection.Entities.Count} entities";

    private void End(ViewportToolContext context)
    {
        Selection = null;
        Active = null;
        Readout = null;
        context.Hint = Description;
        context.Invalidate();
    }

    /// <summary>The distance from a point to a segment, in screen pixels.</summary>
    protected static double DistanceToSegment(Point point, Point a, Point b)
    {
        var ab = b - a;
        var length = ab.X * ab.X + ab.Y * ab.Y;
        var t = length == 0 ? 0 : Math.Clamp(((point.X - a.X) * ab.X + (point.Y - a.Y) * ab.Y) / length, 0, 1);
        var closest = a + ab * t;
        return Point.Distance(point, closest);
    }

    protected static Point Offset(Point origin, Vector2 direction, double length) => new(origin.X + direction.X * length, origin.Y + direction.Y * length);

    /// <summary>Draws an axis line with an arrow or square tip.</summary>
    protected static void Axis(DrawingContext drawing, Point origin, Vector2 direction, double length, Color color, bool square)
    {
        var end = Offset(origin, direction, length);
        drawing.DrawLine(GizmoContext.Pen(Color.FromArgb(110, 0, 0, 0), 4, false, 1), origin, end);
        drawing.DrawLine(GizmoContext.Pen(color, 2, false, 1), origin, end);
        if (square)
        {
            GizmoContext.DrawHandle(drawing, end, color, GizmoHandleShape.Square, 10, GizmoPalette.Current);
            return;
        }

        var normal = new Vector2(-direction.Y, direction.X);
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(Offset(origin, direction, length + 12), true);
            stream.LineTo(Offset(end, normal, 6));
            stream.LineTo(Offset(end, normal, -6));
            stream.EndFigure(true);
        }

        drawing.DrawGeometry(GizmoContext.Fill(color, 1), GizmoContext.Pen(Color.FromArgb(150, 0, 0, 0), 1, false, 1), geometry);
    }
}
