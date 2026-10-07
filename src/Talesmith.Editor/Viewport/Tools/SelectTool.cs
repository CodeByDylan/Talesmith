using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Ecs;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Color = Avalonia.Media.Color;
using Transform = Talesmith.Runtime.Components.Transform;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>Selects entities by clicking or dragging a marquee, and moves the selection by dragging it.</summary>
/// <remarks>Shift adds to the selection, Ctrl toggles; holding Ctrl while moving snaps to the grid even when snapping is off.</remarks>
public sealed class SelectTool : IViewportTool
{
    public const string ToolGroup = "Select";
    private const double DragThreshold = 4;

    private Point? _pressPosition;
    private Rect? _marquee;
    private DragMove? _move;

    public string Id => "select";

    public string Name => "Select";

    public string Description => "Click to select, drag to move; drag on empty space to select an area. Shift adds, Ctrl toggles.";

    public Geometry Icon => Icons.MousePointer;

    public string? Shortcut => "Q";

    public string Group => ToolGroup;

    public int Order => 0;

    public bool IsOperationInProgress => _marquee is not null || _move is not null;

    public void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed)
            return;
        _pressPosition = e.Position;
        var hit = context.Picker.HitTest(e.World, context.Camera.Zoom);
        var mode = ModeOf(e.Modifiers);
        if (hit is { } id)
        {
            if (mode == SelectionMode.Replace && !context.Selection.IsSelected(id))
                context.Selection.SelectEntity(id);
            else if (mode != SelectionMode.Replace)
                context.Selection.SelectEntity(id, mode);
            if (mode == SelectionMode.Replace && e.ClickCount < 2)
                _move = DragMove.Begin(context, e.World);
        }

        e.Source.Pointer.Capture(context.View);
        e.Handled = true;
        context.Invalidate();
    }

    public void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (_pressPosition is not { } start)
        {
            var hovered = context.Picker.HitTest(e.World, context.Camera.Zoom);
            if (hovered != context.Hovered)
            {
                context.Hovered = hovered;
                context.Invalidate();
            }

            return;
        }

        var distance = Point.Distance(start, e.Position);
        if (_move is { } move)
        {
            if (move.IsDragging || distance >= DragThreshold)
            {
                move.Update(context, e.World, (e.Modifiers & KeyModifiers.Control) != 0);
                context.Hovered = null;
            }
        }
        else if (_marquee is not null || distance >= DragThreshold)
        {
            _marquee = new Rect(start, e.Position).Normalize();
        }

        context.Invalidate();
    }

    public void PointerReleased(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (_pressPosition is null)
            return;
        var mode = ModeOf(e.Modifiers);
        if (_marquee is { } marquee)
        {
            var a = context.ToWorld(marquee.TopLeft);
            var b = context.ToWorld(marquee.BottomRight);
            var found = context.Picker.Query(Rect2.FromEdges(a.X, a.Y, b.X, b.Y), context.Camera.Zoom);
            context.Selection.SelectEntities(found, mode == SelectionMode.Toggle ? SelectionMode.Toggle : mode);
        }
        else if (_move is { IsDragging: true } move)
        {
            move.End(context);
        }
        else if (context.Picker.HitTest(e.World, context.Camera.Zoom) is null && mode == SelectionMode.Replace)
        {
            context.Selection.SelectEntities([]);
        }
        else if (_move is not null && context.Picker.HitTest(e.World, context.Camera.Zoom) is { } clicked && context.Selection.Entities.Count > 1)
        {
            context.Selection.SelectEntity(clicked);
        }

        Reset(context, e);
    }

    public void PointerExited(ViewportToolContext context)
    {
        if (context.Hovered is null)
            return;
        context.Hovered = null;
        context.Invalidate();
    }

    public void Cancel(ViewportToolContext context)
    {
        _move?.Cancel(context);
        _move = null;
        _marquee = null;
        _pressPosition = null;
        context.Invalidate();
    }

    public void Render(ViewportToolContext context, DrawingContext drawing)
    {
        if (_marquee is not { } marquee)
            return;
        var accent = AccentColor();
        drawing.DrawRectangle(new ImmutableSolidColorBrush(accent, 0.12), new ImmutablePen(new ImmutableSolidColorBrush(accent, 0.9), 1), marquee);
    }

    internal static Color AccentColor() =>
        Application.Current?.TryGetResource("AccentColor", Application.Current.ActualThemeVariant, out var value) == true && value is Color color
            ? color
            : Color.Parse("#6366F1");

    private static SelectionMode ModeOf(KeyModifiers modifiers) =>
        (modifiers & KeyModifiers.Control) != 0 ? SelectionMode.Toggle
        : (modifiers & KeyModifiers.Shift) != 0 ? SelectionMode.Add
        : SelectionMode.Replace;

    private void Reset(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        _pressPosition = null;
        _marquee = null;
        _move = null;
        e.Source.Pointer.Capture(null);
        e.Handled = true;
        context.Invalidate();
    }

    /// <summary>Moves the selected entities with the pointer as one undo step.</summary>
    private sealed class DragMove
    {
        private readonly List<(Guid Id, Vector2 Start, Matrix3x2 WorldToParent)> _entities;
        private readonly Vector2 _origin;
        private readonly Vector2 _anchor;
        private UndoTransaction? _transaction;

        private DragMove(List<(Guid, Vector2, Matrix3x2)> entities, Vector2 origin, Vector2 anchor)
        {
            _entities = entities;
            _origin = origin;
            _anchor = anchor;
        }

        public bool IsDragging => _transaction is not null;

        public static DragMove? Begin(ViewportToolContext context, Vector2 world)
        {
            if (context.Document is not { } model || context.World.World is not { } runtime)
                return null;
            var entities = new List<(Guid, Vector2, Matrix3x2)>();
            foreach (var id in model.GetTopLevel(context.Selection.Entities))
            {
                var entity = model.Find(id);
                if (entity is null || entity.Editor.Locked || entity.FindComponent("Transform") is not { } transform)
                    continue;
                var position = JsonFormats.ReadVector2(transform.Data["position"] ?? new JsonArray(0, 0));
                entities.Add((id, position, WorldToParent(context, runtime, entity.Parent)));
            }

            if (entities.Count == 0)
                return null;
            var anchor = context.World.TryGetEntity(entities[^1].Item1, out var primary) && runtime.TryGet<Transform>(primary, out var t) ? t.Position : world;
            return new DragMove(entities, world, anchor);
        }

        public void Update(ViewportToolContext context, Vector2 world, bool forceSnap)
        {
            if (context.Document is not { } model)
                return;
            _transaction ??= context.Undo.BeginTransaction(_entities.Count == 1 ? $"Move {model.Find(_entities[0].Id)?.Name}" : $"Move {_entities.Count} entities");
            var target = context.Options.Snap(_anchor + world - _origin, forceSnap);
            var delta = target - _anchor;
            foreach (var (id, start, worldToParent) in _entities)
            {
                if (!model.Contains(id))
                    continue;
                var local = start + Vector2.TransformNormal(delta, worldToParent);
                model.SetProperty(id, "Transform", "position", JsonFormats.WriteVector2(local));
            }
        }

        public void End(ViewportToolContext context)
        {
            _transaction?.Dispose();
            _transaction = null;
            context.Undo.Seal();
        }

        public void Cancel(ViewportToolContext context)
        {
            _transaction?.Cancel();
            _transaction = null;
        }

        private static Matrix3x2 WorldToParent(ViewportToolContext context, World runtime, Guid? parent)
        {
            if (parent is not { } parentId || !context.World.TryGetEntity(parentId, out var entity) || !runtime.TryGet<Transform>(entity, out var transform))
                return Matrix3x2.Identity;
            var toWorld = Matrix3x2.CreateScale(transform.Scale) * Matrix3x2.CreateRotation(transform.Rotation);
            return Matrix3x2.Invert(toWorld, out var inverse) ? inverse : Matrix3x2.Identity;
        }
    }
}
