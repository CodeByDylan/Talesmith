using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Editor.Assets.Controls;
using PixelRect = Talesmith.Assets.Textures.PixelRect;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>The sprite editor's canvas: draws the slices over the texture and lets you select, draw, move and resize them and drag their pivots,
/// snapping to whole pixels.</summary>
/// <remarks>Click selects (Ctrl toggles, Shift adds), dragging on empty space draws a slice, dragging a selected slice moves the selection,
/// the handles resize, and the round handle moves the pivot. Escape cancels a drag. The middle or right button pans; the wheel zooms.</remarks>
public sealed class SpriteCanvas : PixelCanvas
{
    public static readonly StyledProperty<SpriteEditorViewModel?> EditorProperty = AvaloniaProperty.Register<SpriteCanvas, SpriteEditorViewModel?>(nameof(Editor));

    private const double HandleSize = 7;
    private const double DragThreshold = 3;

    private DragKind _drag;
    private Point _pressScreen;
    private Point _pressImage;
    private bool _dragStarted;
    private SliceItem? _target;
    private Handle _handle;
    private PixelRect _drawn;
    private string? _hover;
    private SpriteSheetState? _startState;

    public SpriteCanvas()
    {
        PanWithLeftButton = false;
        ShowPixelGrid = true;
    }

    public SpriteEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override bool HandlesDoubleClick => true;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EditorProperty)
            return;
        if (change.OldValue is SpriteEditorViewModel old)
        {
            old.SlicesChanged -= OnSlicesChanged;
            old.PropertyChanged -= OnEditorChanged;
        }

        if (change.NewValue is SpriteEditorViewModel editor)
        {
            editor.SlicesChanged += OnSlicesChanged;
            editor.PropertyChanged += OnEditorChanged;
        }
    }

    private void OnSlicesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpriteEditorViewModel.GridPreview) or nameof(SpriteEditorViewModel.ShowGridPreview))
            InvalidateVisual();
    }

    protected override void RenderOverlay(DrawingContext context)
    {
        if (Editor is not { } editor)
            return;
        var accent = Accent ?? Brushes.MediumSlateBlue;
        var accentColor = accent is ISolidColorBrush solid ? solid.Color : Colors.SlateBlue;
        if (editor.ShowGridPreview && editor.GridPreview is { } preview)
        {
            var dashed = new Pen(new SolidColorBrush(Color.FromArgb(200, 245, 158, 11)), 1) { DashStyle = new DashStyle([3, 3], 0) };
            foreach (var cell in preview)
                context.DrawRectangle(dashed, ToScreen(ToRect(cell)).Deflate(0.5));
        }

        var outline = new Pen(new SolidColorBrush(accentColor, 0.75), 1);
        var hover = new Pen(new SolidColorBrush(accentColor, 1), 1.5);
        var selected = new Pen(accent, 2);
        var fill = new SolidColorBrush(accentColor, 0.18);
        var chipBackground = new SolidColorBrush(Color.FromArgb(200, 20, 22, 28));
        var labels = editor.Slices.Count <= 600;
        foreach (var slice in editor.Slices)
        {
            var rect = ToScreen(ToRect(slice.Rect));
            if (slice.IsSelected)
                context.FillRectangle(fill, rect);
            context.DrawRectangle(slice.IsSelected ? selected : slice.Name == _hover ? hover : outline, rect.Deflate(0.5));
            if (labels && rect.Width >= 34 && rect.Height >= 20)
                DrawChip(context, slice.Name, new Point(rect.X + 2, rect.Y + 2), slice.IsSelected ? accent : chipBackground, rect.Width - 4);
        }

        if (editor.Primary is { IsSelected: true } primary)
        {
            var rect = ToScreen(ToRect(primary.Rect));
            foreach (var (handle, point) in Handles(rect))
            {
                _ = handle;
                var box = new Rect(point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize);
                context.DrawRectangle(Brushes.White, new Pen(accent, 1.5), box, 1.5, 1.5);
            }

            var pivot = PivotPoint(primary);
            context.DrawEllipse(null, new Pen(Brushes.Black, 3), pivot, 5, 5);
            context.DrawEllipse(Brushes.White, new Pen(accent, 2), pivot, 4.5, 4.5);
            context.DrawLine(new Pen(accent, 1), pivot - new Vector(9, 0), pivot + new Vector(9, 0));
            context.DrawLine(new Pen(accent, 1), pivot - new Vector(0, 9), pivot + new Vector(0, 9));
            if (_dragStarted && _drag is DragKind.Move or DragKind.Resize)
                DrawChip(context, $"{primary.Rect.X}, {primary.Rect.Y}  ·  {primary.Rect.Width} × {primary.Rect.Height}", new Point(rect.X, rect.Bottom + 6), chipBackground);
        }

        if (_drag == DragKind.Create && _dragStarted && !_drawn.IsEmpty)
        {
            var rect = ToScreen(ToRect(_drawn));
            context.FillRectangle(fill, rect);
            context.DrawRectangle(new Pen(Brushes.White, 3), rect);
            context.DrawRectangle(new Pen(accent, 1.5), rect);
            DrawChip(context, $"{_drawn.Width} × {_drawn.Height}", new Point(rect.X, rect.Bottom + 6), chipBackground);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (Editor is not { } editor || !point.Properties.IsLeftButtonPressed || ImageSize.Width == 0)
        {
            base.OnPointerPressed(e);
            return;
        }

        Focus();
        _pressScreen = point.Position;
        _pressImage = ToImage(point.Position);
        _dragStarted = false;
        _startState = editor.State;
        var toggle = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var add = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (editor.Primary is { IsSelected: true } primary && !toggle && !add)
        {
            if (Distance(PivotPoint(primary), point.Position) <= 7)
            {
                Start(DragKind.Pivot, primary, e);
                return;
            }

            foreach (var (handle, position) in Handles(ToScreen(ToRect(primary.Rect))))
            {
                if (Math.Abs(position.X - point.Position.X) <= HandleSize && Math.Abs(position.Y - point.Position.Y) <= HandleSize)
                {
                    _handle = handle;
                    Start(DragKind.Resize, primary, e);
                    return;
                }
            }
        }

        var hit = HitTest(point.Position);
        if (hit is null)
        {
            if (!toggle && !add)
                editor.Select(null);
            _drawn = default;
            Start(DragKind.Create, null, e);
            return;
        }

        if (toggle || add || !hit.IsSelected)
            editor.Select(hit.Name, toggle, add);
        else
            editor.Select(hit.Name, toggle: false, add: true);
        Start(hit.IsSelected ? DragKind.Move : DragKind.None, hit, e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_drag == DragKind.None || Editor is not { } editor || IsPanning)
        {
            UpdateHover(position);
            return;
        }

        if (!_dragStarted)
        {
            if (Distance(position, _pressScreen) < DragThreshold)
                return;
            _dragStarted = true;
            if (_drag != DragKind.Create)
                editor.BeginDrag();
        }

        var image = ToImage(position);
        var size = ImageSize;
        switch (_drag)
        {
            case DragKind.Create:
            {
                var x0 = Snap(Math.Min(_pressImage.X, image.X), size.Width);
                var y0 = Snap(Math.Min(_pressImage.Y, image.Y), size.Height);
                var x1 = Snap(Math.Max(_pressImage.X, image.X), size.Width);
                var y1 = Snap(Math.Max(_pressImage.Y, image.Y), size.Height);
                _drawn = new PixelRect(x0, y0, x1 - x0, y1 - y0);
                InvalidateVisual();
                break;
            }
            case DragKind.Move when _startState is { } start:
            {
                var dx = (int)Math.Round(image.X - _pressImage.X);
                var dy = (int)Math.Round(image.Y - _pressImage.Y);
                var selected = editor.SelectedNames.ToHashSet(StringComparer.Ordinal);
                editor.DragTo(start with
                {
                    Slices = [.. start.Slices.Select(s => selected.Contains(s.Name)
                        ? s with { Rect = s.Rect with { X = Math.Clamp(s.Rect.X + dx, 0, Math.Max(0, size.Width - s.Rect.Width)), Y = Math.Clamp(s.Rect.Y + dy, 0, Math.Max(0, size.Height - s.Rect.Height)) } }
                        : s)]
                });
                break;
            }
            case DragKind.Resize when _startState is { } start && _target is { } target:
            {
                var original = start.Slices.First(s => s.Name == target.Name).Rect;
                int left = original.X, top = original.Y, right = original.Right, bottom = original.Bottom;
                var x = Snap(image.X, size.Width);
                var y = Snap(image.Y, size.Height);
                if (_handle is Handle.Left or Handle.TopLeft or Handle.BottomLeft)
                    left = Math.Min(x, right - 1);
                if (_handle is Handle.Right or Handle.TopRight or Handle.BottomRight)
                    right = Math.Max(x, left + 1);
                if (_handle is Handle.Top or Handle.TopLeft or Handle.TopRight)
                    top = Math.Min(y, bottom - 1);
                if (_handle is Handle.Bottom or Handle.BottomLeft or Handle.BottomRight)
                    bottom = Math.Max(y, top + 1);
                var rect = new PixelRect(left, top, right - left, bottom - top);
                editor.DragTo(start with { Slices = [.. start.Slices.Select(s => s.Name == target.Name ? s with { Rect = rect } : s)] });
                break;
            }
            case DragKind.Pivot when _startState is { } start && _target is { } target:
            {
                var rect = start.Slices.First(s => s.Name == target.Name).Rect;
                var px = Math.Clamp(Math.Round(image.X * 2) / 2, rect.X, rect.Right);
                var py = Math.Clamp(Math.Round(image.Y * 2) / 2, rect.Y, rect.Bottom);
                var pivot = new System.Numerics.Vector2((float)((px - rect.X) / rect.Width), (float)((py - rect.Y) / rect.Height));
                editor.DragTo(start with { Slices = [.. start.Slices.Select(s => s.Name == target.Name ? s with { Pivot = pivot } : s)] });
                break;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == DragKind.None || Editor is not { } editor)
            return;
        var kind = _drag;
        _drag = DragKind.None;
        e.Pointer.Capture(null);
        if (!_dragStarted)
        {
            if (kind == DragKind.Move && _target is not null && (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control)) == 0)
                editor.Select(_target.Name);
            return;
        }

        switch (kind)
        {
            case DragKind.Create:
                if (_drawn.Width >= 1 && _drawn.Height >= 1)
                    editor.AddSlice(_drawn);
                _drawn = default;
                break;
            case DragKind.Move:
                editor.EndDrag(editor.SelectedNames.Count == 1 ? $"Move sprite {editor.SelectedNames[0]}" : "Move sprites");
                break;
            case DragKind.Resize:
                editor.EndDrag($"Resize sprite {_target?.Name}");
                break;
            case DragKind.Pivot:
                editor.EndDrag($"Move pivot of {_target?.Name}");
                break;
        }

        _dragStarted = false;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _drag != DragKind.None)
        {
            if (_dragStarted && _drag != DragKind.Create)
                Editor?.CancelDrag();
            _drag = DragKind.None;
            _dragStarted = false;
            _drawn = default;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void Start(DragKind kind, SliceItem? target, PointerPressedEventArgs e)
    {
        _drag = kind;
        _target = target;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    private void UpdateHover(Point position)
    {
        var hit = HitTest(position);
        var cursor = StandardCursorType.Cross;
        if (Editor?.Primary is { IsSelected: true } primary)
        {
            if (Distance(PivotPoint(primary), position) <= 7)
                cursor = StandardCursorType.Hand;
            else if (Handles(ToScreen(ToRect(primary.Rect))).FirstOrDefault(h => Math.Abs(h.Point.X - position.X) <= HandleSize && Math.Abs(h.Point.Y - position.Y) <= HandleSize) is { Point: var p } match && p != default)
                cursor = match.Handle switch
                {
                    Handle.Left or Handle.Right => StandardCursorType.SizeWestEast,
                    Handle.Top or Handle.Bottom => StandardCursorType.SizeNorthSouth,
                    Handle.TopLeft => StandardCursorType.TopLeftCorner,
                    Handle.TopRight => StandardCursorType.TopRightCorner,
                    Handle.BottomLeft => StandardCursorType.BottomLeftCorner,
                    _ => StandardCursorType.BottomRightCorner
                };
            else if (hit is { IsSelected: true })
                cursor = StandardCursorType.SizeAll;
        }

        Cursor = new Cursor(cursor);
        if (hit?.Name != _hover)
        {
            _hover = hit?.Name;
            InvalidateVisual();
        }
    }

    private SliceItem? HitTest(Point screen)
    {
        if (Editor is not { } editor)
            return null;
        var image = ToImage(screen);
        for (var i = editor.Slices.Count - 1; i >= 0; i--)
        {
            var rect = editor.Slices[i].Rect;
            if (image.X >= rect.X && image.X < rect.Right && image.Y >= rect.Y && image.Y < rect.Bottom)
                return editor.Slices[i];
        }

        return null;
    }

    private Point PivotPoint(SliceItem slice) =>
        ToScreen(new Point(slice.Rect.X + slice.Pivot.X * slice.Rect.Width, slice.Rect.Y + slice.Pivot.Y * slice.Rect.Height));

    private static Rect ToRect(PixelRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static int Snap(double value, int max) => (int)Math.Clamp(Math.Round(value), 0, max);

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static IEnumerable<(Handle Handle, Point Point)> Handles(Rect rect)
    {
        yield return (Handle.TopLeft, rect.TopLeft);
        yield return (Handle.TopRight, rect.TopRight);
        yield return (Handle.BottomLeft, rect.BottomLeft);
        yield return (Handle.BottomRight, rect.BottomRight);
        if (rect.Width >= 24)
        {
            yield return (Handle.Top, new Point(rect.Center.X, rect.Top));
            yield return (Handle.Bottom, new Point(rect.Center.X, rect.Bottom));
        }

        if (rect.Height >= 24)
        {
            yield return (Handle.Left, new Point(rect.Left, rect.Center.Y));
            yield return (Handle.Right, new Point(rect.Right, rect.Center.Y));
        }
    }

    private enum DragKind
    {
        None,
        Create,
        Move,
        Resize,
        Pivot
    }

    private enum Handle
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
