using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.UI;
using Color = Avalonia.Media.Color;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>Moves the selection along an axis or freely; Ctrl snaps to the grid and Shift keeps a free move on its main axis.</summary>
public sealed class MoveTool(EntityDataService entities) : TransformTool(entities)
{
    private const int AxisXPart = 0;
    private const int AxisYPart = 1;
    private const int FreePart = 2;
    private const double Length = 78;

    public override string Id => "move";

    public override string Name => "Move";

    public override string Description => "Drag an arrow to move along it or the square to move freely. Ctrl snaps to the grid, Shift keeps to one axis.";

    public override Geometry Icon => Icons.Move;

    public override string? Shortcut => "W";

    public override int Order => 0;

    protected override string Verb => "Move";

    internal override int? HitTest(ViewportToolContext context, TransformSelection selection, Point position)
    {
        var origin = context.ToScreen(selection.Pivot);
        if (FreeSquare(origin, selection).Contains(position) || Point.Distance(origin, position) < 7)
            return FreePart;
        if (DistanceToSegment(position, origin, Offset(origin, selection.AxisX, Length + 12)) < 7)
            return AxisXPart;
        if (DistanceToSegment(position, origin, Offset(origin, selection.AxisY, Length + 12)) < 7)
            return AxisYPart;
        return null;
    }

    internal override void Drag(ViewportToolContext context, TransformSelection start, int part, ViewportPointerEventArgs e)
    {
        var delta = e.World - Start;
        var shift = (e.Modifiers & KeyModifiers.Shift) != 0;
        var snap = (e.Modifiers & KeyModifiers.Control) != 0;
        if (part == FreePart && shift)
            part = MathF.Abs(Vector2.Dot(delta, start.AxisX)) >= MathF.Abs(Vector2.Dot(delta, start.AxisY)) ? AxisXPart : AxisYPart;
        delta = part switch
        {
            AxisXPart => start.AxisX * Vector2.Dot(delta, start.AxisX),
            AxisYPart => start.AxisY * Vector2.Dot(delta, start.AxisY),
            _ => delta
        };

        var target = context.Options.Snap(start.Pivot + delta, snap);
        if (target != start.Pivot + delta)
        {
            delta = target - start.Pivot;
            if (part == AxisXPart)
                delta = start.AxisX * Vector2.Dot(delta, start.AxisX);
            else if (part == AxisYPart)
                delta = start.AxisY * Vector2.Dot(delta, start.AxisY);
        }

        start.Apply(Entities, entity => entity.Start with { Position = entity.Start.Position + delta }, position: true, rotation: false, scale: false);
        Readout = string.Create(CultureInfo.InvariantCulture, $"Δ {delta.X:0.##}, {delta.Y:0.##}");
    }

    internal override void DrawHandles(ViewportToolContext context, DrawingContext drawing, TransformSelection start, TransformSelection current, GizmoPalette palette)
    {
        var origin = context.ToScreen(current.Pivot);
        var highlight = Active ?? Hovered;
        if (Active is not null)
        {
            var from = context.ToScreen(start.Pivot);
            drawing.DrawLine(GizmoContext.Pen(palette.Highlight, 1, true, 0.8), from, origin);
            drawing.DrawEllipse(null, GizmoContext.Pen(palette.Highlight, 1, false, 0.8), from, 3, 3);
        }

        Axis(drawing, origin, current.AxisX, Length, highlight == AxisXPart ? palette.Highlight : palette.AxisX, square: false);
        Axis(drawing, origin, current.AxisY, Length, highlight == AxisYPart ? palette.Highlight : palette.AxisY, square: false);
        var square = FreeSquare(origin, current);
        var color = highlight == FreePart ? palette.Highlight : palette.Accent;
        drawing.DrawRectangle(GizmoContext.Fill(color, 0.35), GizmoContext.Pen(color, 1.5, false, 1), square, 2, 2);
        drawing.DrawEllipse(GizmoContext.Fill(Colors.White, 1), GizmoContext.Pen(Color.FromArgb(180, 0, 0, 0), 1, false, 1), origin, 3, 3);
    }

    protected override Cursor CursorFor(int part) => new(part switch
    {
        AxisXPart => StandardCursorType.SizeWestEast,
        AxisYPart => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.SizeAll
    });

    protected override string HintFor(int part) => part switch
    {
        AxisXPart => "Drag to move along X; Ctrl snaps to the grid",
        AxisYPart => "Drag to move along Y; Ctrl snaps to the grid",
        _ => "Drag to move freely; Ctrl snaps, Shift keeps to one axis"
    };

    private static Rect FreeSquare(Point origin, TransformSelection selection)
    {
        var corner = Offset(Offset(origin, selection.AxisX, 14), selection.AxisY, 14);
        return new Rect(corner.X - 1, corner.Y - 1, 16, 16).Normalize();
    }
}

/// <summary>Rotates the selection around its pivot by dragging the ring; Ctrl snaps to 15°.</summary>
public sealed class RotateTool(EntityDataService entities) : TransformTool(entities)
{
    private const int RingPart = 0;
    private const double Radius = 66;
    private float _angle;

    public override string Id => "rotate";

    public override string Name => "Rotate";

    public override string Description => "Drag the ring to rotate around the pivot. Ctrl snaps to 15°.";

    public override Geometry Icon => Icons.RotateCw;

    public override string? Shortcut => "E";

    public override int Order => 1;

    protected override string Verb => "Rotate";

    internal override int? HitTest(ViewportToolContext context, TransformSelection selection, Point position) =>
        Math.Abs(Point.Distance(context.ToScreen(selection.Pivot), position) - Radius) < 8 ? RingPart : null;

    internal override void Drag(ViewportToolContext context, TransformSelection start, int part, ViewportPointerEventArgs e)
    {
        var from = Start - start.Pivot;
        var to = e.World - start.Pivot;
        var angle = GizmoMath.Wrap(MathF.Atan2(to.Y, to.X) - MathF.Atan2(from.Y, from.X));
        var snap = (e.Modifiers & KeyModifiers.Control) != 0;
        if (snap)
            angle = GizmoMath.SnapAngle(angle, true);
        else if (context.Options.SnapToGrid && context.Options.RotationSnapDegrees > 0)
            angle = GizmoMath.SnapAngle(angle, true, (float)context.Options.RotationSnapDegrees);
        _angle = angle;
        start.Apply(Entities, entity => entity.Start with
        {
            Position = start.Pivot + GizmoMath.Rotate(entity.Start.Position - start.Pivot, angle),
            Rotation = entity.Start.Rotation + angle
        }, position: start.Entities.Count > 1 || start.Entities[0].Start.Position != start.Pivot, rotation: true, scale: false);
        Readout = string.Create(CultureInfo.InvariantCulture, $"{angle * 180 / MathF.PI:0.#}°");
    }

    internal override void DrawHandles(ViewportToolContext context, DrawingContext drawing, TransformSelection start, TransformSelection current, GizmoPalette palette)
    {
        var origin = context.ToScreen(current.Pivot);
        var highlight = (Active ?? Hovered) == RingPart;
        var color = highlight ? palette.Highlight : palette.Accent;
        drawing.DrawEllipse(GizmoContext.Fill(color, 0.06), GizmoContext.Pen(Color.FromArgb(110, 0, 0, 0), 4.5, false, 1), origin, Radius, Radius);
        drawing.DrawEllipse(null, GizmoContext.Pen(color, 2.25, false, 1), origin, Radius, Radius);
        if (Active is not null)
        {
            var from = Start - start.Pivot;
            var startAngle = MathF.Atan2(from.Y, from.X);
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(origin, true);
                var segments = Math.Max(2, (int)(Math.Abs(_angle) / MathF.Tau * 72));
                for (var i = 0; i <= segments; i++)
                {
                    var a = startAngle + _angle * i / segments;
                    stream.LineTo(new Point(origin.X + Math.Cos(a) * Radius, origin.Y + Math.Sin(a) * Radius));
                }

                stream.EndFigure(true);
            }

            drawing.DrawGeometry(GizmoContext.Fill(palette.Highlight, 0.22), GizmoContext.Pen(palette.Highlight, 1, false, 0.9), geometry);
        }

        var axis = Offset(origin, current.AxisX, Radius);
        drawing.DrawLine(GizmoContext.Pen(palette.AxisX, 1.5, true, 0.8), origin, axis);
        GizmoContext.DrawHandle(drawing, axis, color, GizmoHandleShape.Circle, 9, palette);
        drawing.DrawEllipse(GizmoContext.Fill(Colors.White, 1), GizmoContext.Pen(Color.FromArgb(180, 0, 0, 0), 1, false, 1), origin, 3, 3);
    }

    protected override Cursor CursorFor(int part) => new(StandardCursorType.Hand);

    protected override string HintFor(int part) => "Drag to rotate; Ctrl snaps to 15°";
}

/// <summary>Scales the selection along an axis or uniformly from its pivot; Ctrl snaps to tenths.</summary>
public sealed class ScaleTool(EntityDataService entities) : TransformTool(entities)
{
    private const int AxisXPart = 0;
    private const int AxisYPart = 1;
    private const int UniformPart = 2;
    private const double Length = 72;

    public override string Id => "scale";

    public override string Name => "Scale";

    public override string Description => "Drag an axis handle to stretch along it or the center to scale evenly. Ctrl snaps to tenths.";

    public override Geometry Icon => Icons.Scale;

    public override string? Shortcut => "R";

    public override int Order => 2;

    protected override string Verb => "Scale";

    internal override int? HitTest(ViewportToolContext context, TransformSelection selection, Point position)
    {
        var origin = context.ToScreen(selection.Pivot);
        if (Point.Distance(origin, position) < 10)
            return UniformPart;
        if (DistanceToSegment(position, origin, Offset(origin, selection.AxisX, Length + 6)) < 7)
            return AxisXPart;
        if (DistanceToSegment(position, origin, Offset(origin, selection.AxisY, Length + 6)) < 7)
            return AxisYPart;
        return null;
    }

    internal override void Drag(ViewportToolContext context, TransformSelection start, int part, ViewportPointerEventArgs e)
    {
        var snap = (e.Modifiers & KeyModifiers.Control) != 0;
        float Factor(Vector2 axis)
        {
            var from = Vector2.Dot(Start - start.Pivot, axis);
            var to = Vector2.Dot(e.World - start.Pivot, axis);
            return MathF.Abs(from) * context.Camera.Zoom < 4 ? 1 + (to - from) * context.Camera.Zoom / (float)Length : to / from;
        }

        var factor = part switch
        {
            AxisXPart => new Vector2(Factor(start.AxisX), 1),
            AxisYPart => new Vector2(1, Factor(start.AxisY)),
            _ => new Vector2(MathF.Max(0.01f, 1 + (float)((e.Position.X - StartScreen.X) - (e.Position.Y - StartScreen.Y)) / 120))
        };
        if (snap)
            factor = new Vector2(MathF.Round(factor.X * 10) / 10, MathF.Round(factor.Y * 10) / 10);
        start.Apply(Entities, entity =>
        {
            var offset = GizmoMath.Rotate(entity.Start.Position - start.Pivot, -start.Rotation) * factor;
            var relative = GizmoMath.Wrap(entity.Start.Rotation - start.Rotation);
            var aligned = MathF.Abs(MathF.Sin(relative * 2)) < 0.01f;
            var swap = aligned && MathF.Abs(MathF.Cos(relative)) < 0.5f;
            var scale = entity.Start.Scale * (swap ? new Vector2(factor.Y, factor.X) : aligned ? factor : new Vector2(MathF.Sqrt(MathF.Abs(factor.X * factor.Y))));
            return entity.Start with { Position = start.Pivot + GizmoMath.Rotate(offset, start.Rotation), Scale = scale };
        }, position: start.Entities.Count > 1 || start.Entities[0].Start.Position != start.Pivot, rotation: false, scale: true);
        Readout = part == UniformPart
            ? string.Create(CultureInfo.InvariantCulture, $"×{factor.X:0.##}")
            : string.Create(CultureInfo.InvariantCulture, $"×{factor.X:0.##}, {factor.Y:0.##}");
    }

    internal override void DrawHandles(ViewportToolContext context, DrawingContext drawing, TransformSelection start, TransformSelection current, GizmoPalette palette)
    {
        var origin = context.ToScreen(current.Pivot);
        var highlight = Active ?? Hovered;
        Axis(drawing, origin, current.AxisX, Length, highlight == AxisXPart ? palette.Highlight : palette.AxisX, square: true);
        Axis(drawing, origin, current.AxisY, Length, highlight == AxisYPart ? palette.Highlight : palette.AxisY, square: true);
        GizmoContext.DrawHandle(drawing, origin, highlight == UniformPart ? palette.Highlight : palette.Accent, GizmoHandleShape.Square, 13, palette);
    }

    protected override Cursor CursorFor(int part) => new(part switch
    {
        AxisXPart => StandardCursorType.SizeWestEast,
        AxisYPart => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.BottomRightCorner
    });

    protected override string HintFor(int part) => part == UniformPart ? "Drag to scale evenly; Ctrl snaps to tenths" : "Drag to stretch along the axis; Ctrl snaps to tenths";
}

/// <summary>Resizes the selection's bounds by its edges and corners, or moves it by dragging inside; Alt resizes around the center, Shift keeps the
/// aspect ratio at corners and Ctrl snaps edges to the grid.</summary>
public sealed class RectTool(EntityDataService entities) : TransformTool(entities)
{
    private const int InsidePart = 8;
    private static readonly Vector2[] Anchors =
    [
        new(0, 0), new(0.5f, 0), new(1, 0), new(1, 0.5f), new(1, 1), new(0.5f, 1), new(0, 1), new(0, 0.5f)
    ];

    public override string Id => "rect";

    public override string Name => "Rect";

    public override string Description => "Drag an edge or corner to resize, inside to move. Alt resizes around the center, Shift keeps the aspect, Ctrl snaps.";

    public override Geometry Icon => Icons.SquareDashed;

    public override string? Shortcut => "T";

    public override int Order => 3;

    protected override string Verb => "Resize";

    internal override int? HitTest(ViewportToolContext context, TransformSelection selection, Point position)
    {
        var rect = context.Camera.WorldToScreen(selection.Bounds);
        for (var i = 0; i < Anchors.Length; i++)
        {
            if (Point.Distance(At(rect, Anchors[i]), position) < 8)
                return i;
        }

        return rect.Contains(position) ? InsidePart : null;
    }

    internal override void Drag(ViewportToolContext context, TransformSelection start, int part, ViewportPointerEventArgs e)
    {
        var bounds = start.Bounds;
        var delta = e.World - Start;
        var snap = (e.Modifiers & KeyModifiers.Control) != 0;
        if (part == InsidePart)
        {
            var target = context.Options.Snap(new Vector2(bounds.X, bounds.Y) + delta, snap);
            var move = target - new Vector2(bounds.X, bounds.Y);
            start.Apply(Entities, entity => entity.Start with { Position = entity.Start.Position + move }, position: true, rotation: false, scale: false);
            Readout = string.Create(CultureInfo.InvariantCulture, $"Δ {move.X:0.##}, {move.Y:0.##}");
            return;
        }

        var anchor = Anchors[part];
        var symmetric = (e.Modifiers & KeyModifiers.Alt) != 0;
        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.X + bounds.Width;
        var bottom = bounds.Y + bounds.Height;
        float Snapped(float value) => snap && context.Options.GridSize > 0 ? MathF.Round(value / (float)context.Options.GridSize) * (float)context.Options.GridSize : value;
        if (anchor.X == 0)
            left = Snapped(left + delta.X);
        else if (anchor.X == 1)
            right = Snapped(right + delta.X);
        if (anchor.Y == 0)
            top = Snapped(top + delta.Y);
        else if (anchor.Y == 1)
            bottom = Snapped(bottom + delta.Y);
        if (symmetric)
        {
            var center = new Vector2(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
            if (anchor.X == 0)
                right = 2 * center.X - left;
            else if (anchor.X == 1)
                left = 2 * center.X - right;
            if (anchor.Y == 0)
                bottom = 2 * center.Y - top;
            else if (anchor.Y == 1)
                top = 2 * center.Y - bottom;
        }

        var sx = bounds.Width > 0.001f ? (right - left) / bounds.Width : 1;
        var sy = bounds.Height > 0.001f ? (bottom - top) / bounds.Height : 1;
        if ((e.Modifiers & KeyModifiers.Shift) != 0 && anchor.X != 0.5f && anchor.Y != 0.5f)
        {
            var uniform = MathF.Abs(sx) > MathF.Abs(sy) ? sx : sy;
            sx = sy = uniform;
            if (anchor.X == 0)
                left = right - bounds.Width * sx;
            else
                right = left + bounds.Width * sx;
            if (anchor.Y == 0)
                top = bottom - bounds.Height * sy;
            else
                bottom = top + bounds.Height * sy;
        }

        var origin = new Vector2(bounds.X, bounds.Y);
        var factor = new Vector2(sx, sy);
        start.Apply(Entities, entity => entity.Start with
        {
            Position = new Vector2(left, top) + (entity.Start.Position - origin) * factor,
            Scale = entity.Start.Scale * factor
        }, position: true, rotation: false, scale: true);
        Readout = string.Create(CultureInfo.InvariantCulture, $"{MathF.Abs(right - left):0.#} × {MathF.Abs(bottom - top):0.#}");
    }

    internal override void DrawHandles(ViewportToolContext context, DrawingContext drawing, TransformSelection start, TransformSelection current, GizmoPalette palette)
    {
        var rect = context.Camera.WorldToScreen(current.Bounds);
        var highlight = Active ?? Hovered;
        var color = palette.Accent;
        drawing.DrawRectangle(highlight == InsidePart ? GizmoContext.Fill(color, 0.08) : null, GizmoContext.Pen(color, 1.5, false, 1), rect.Deflate(-0.5));
        var center = rect.Center;
        drawing.DrawEllipse(null, GizmoContext.Pen(color, 1.5, false, 0.9), center, 4, 4);
        for (var i = 0; i < Anchors.Length; i++)
            GizmoContext.DrawHandle(drawing, At(rect, Anchors[i]), highlight == i ? palette.Highlight : Colors.White, GizmoHandleShape.Square, i % 2 == 0 ? 9 : 8, palette);
    }

    protected override Cursor CursorFor(int part) => new(part switch
    {
        0 => StandardCursorType.TopLeftCorner,
        1 => StandardCursorType.TopSide,
        2 => StandardCursorType.TopRightCorner,
        3 => StandardCursorType.RightSide,
        4 => StandardCursorType.BottomRightCorner,
        5 => StandardCursorType.BottomSide,
        6 => StandardCursorType.BottomLeftCorner,
        7 => StandardCursorType.LeftSide,
        _ => StandardCursorType.SizeAll
    });

    protected override string HintFor(int part) => part == InsidePart ? "Drag to move; Ctrl snaps to the grid" : "Drag to resize; Alt around the center, Shift keeps the aspect";

    private static Point At(Rect rect, Vector2 anchor) => new(rect.X + rect.Width * anchor.X, rect.Y + rect.Height * anchor.Y);
}
