using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>The interactive plot of a <see cref="CurveEditor"/>: a zoomable grid with the curve, its keys and the selected key's tangent handles.</summary>
/// <remarks>
/// Double-click adds a key, dragging moves keys and handles (Ctrl snaps to the grid, Shift locks the axis, Alt moves one handle only),
/// Delete removes the selected key, the wheel zooms (Shift: time only, Ctrl: value only), dragging the background pans and F fits the
/// view. Right-click a key for its interpolation and tangents.
/// </remarks>
public class CurveCanvas : Control
{
    public static readonly StyledProperty<Curve> CurveProperty =
        AvaloniaProperty.Register<CurveCanvas, Curve>(nameof(Curve), CurvePresets.Constant, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> SelectedKeyIndexProperty =
        AvaloniaProperty.Register<CurveCanvas, int>(nameof(SelectedKeyIndex), -1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<CurveCanvas>();

    public static readonly StyledProperty<IBrush?> RangeBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(RangeBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> AxisBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(AxisBrush));

    public static readonly StyledProperty<IBrush?> LabelBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(LabelBrush));

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(CurveBrush));

    public static readonly StyledProperty<IBrush?> CurveFillProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(CurveFill));

    public static readonly StyledProperty<IBrush?> KeyBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(KeyBrush));

    public static readonly StyledProperty<IBrush?> KeyFillProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(KeyFill));

    public static readonly StyledProperty<IBrush?> HandleBrushProperty =
        AvaloniaProperty.Register<CurveCanvas, IBrush?>(nameof(HandleBrush));

    public static readonly DirectProperty<CurveCanvas, CurveViewport> ViewportProperty =
        AvaloniaProperty.RegisterDirect<CurveCanvas, CurveViewport>(nameof(Viewport), c => c.Viewport, (c, v) => c.Viewport = v);

    private const double HitRadius = 8;
    private const double HandleLength = 44;
    private const double KeySize = 5;
    private static readonly Thickness Margins = new(38, 10, 12, 22);
    private static readonly Typeface LabelFace = new(FontFamily.Default);

    private readonly Dictionary<string, FormattedText> _labels = new(StringComparer.Ordinal);
    private CurveViewport _viewport = CurveViewport.Unit;
    private StreamGeometry? _line;
    private StreamGeometry? _fill;
    private Size _geometrySize;
    private Pens? _pens;
    private Drag _drag;
    private Hit _hover;
    private Point _dragStart;
    private Point _lastPointer;
    private (float Time, float Value) _keyStart;
    private int _menuKey = -1;
    private bool _fitPending = true;
    private bool _keepInViewPending;
    private bool _editing;

    static CurveCanvas()
    {
        FocusableProperty.OverrideDefaultValue<CurveCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<CurveCanvas>(true);
        AffectsRender<CurveCanvas>(SelectedKeyIndexProperty, BackgroundProperty, RangeBrushProperty, CurveFillProperty, ViewportProperty);
    }

    public CurveCanvas()
    {
        ContextFlyout = CreateMenu();
    }

    /// <summary>Raised before the first change of a drag or command.</summary>
    public event EventHandler<RoutedEventArgs>? EditStarted
    {
        add => AddHandler(ValueEdit.StartedEvent, value);
        remove => RemoveHandler(ValueEdit.StartedEvent, value);
    }

    /// <summary>Raised after the last change of a drag or command.</summary>
    public event EventHandler<RoutedEventArgs>? EditCompleted
    {
        add => AddHandler(ValueEdit.CompletedEvent, value);
        remove => RemoveHandler(ValueEdit.CompletedEvent, value);
    }

    public Curve Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    /// <summary>Gets or sets the index of the selected key, or -1.</summary>
    public int SelectedKeyIndex
    {
        get => GetValue(SelectedKeyIndexProperty);
        set => SetValue(SelectedKeyIndexProperty, value);
    }

    /// <summary>Gets or sets the part of curve space shown.</summary>
    public CurveViewport Viewport
    {
        get => _viewport;
        set
        {
            if (SetAndRaise(ViewportProperty, ref _viewport, value))
                _line = null;
        }
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>Gets or sets the fill of the time range 0 to 1, where curves usually live.</summary>
    public IBrush? RangeBrush
    {
        get => GetValue(RangeBrushProperty);
        set => SetValue(RangeBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public IBrush? LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush? CurveBrush
    {
        get => GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    public IBrush? CurveFill
    {
        get => GetValue(CurveFillProperty);
        set => SetValue(CurveFillProperty, value);
    }

    public IBrush? KeyBrush
    {
        get => GetValue(KeyBrushProperty);
        set => SetValue(KeyBrushProperty, value);
    }

    public IBrush? KeyFill
    {
        get => GetValue(KeyFillProperty);
        set => SetValue(KeyFillProperty, value);
    }

    public IBrush? HandleBrush
    {
        get => GetValue(HandleBrushProperty);
        set => SetValue(HandleBrushProperty, value);
    }

    private Rect PlotArea => new Rect(Bounds.Size).Deflate(Margins);

    /// <summary>Shows time 0 to 1 and the whole curve.</summary>
    public void FitView() => Viewport = CurveViewport.Fit(Curve);

    /// <summary>Replaces the curve as one edit.</summary>
    public void ApplyCurve(Curve curve)
    {
        Edit(() =>
        {
            SetCurrentValue(SelectedKeyIndexProperty, -1);
            SetCurrentValue(CurveProperty, curve);
        });
        FitView();
    }

    /// <summary>Removes the selected key as one edit; the last key stays.</summary>
    public void DeleteSelectedKey()
    {
        var index = SelectedKeyIndex;
        if (index < 0 || Curve.Keys.Length <= 1)
            return;
        Edit(() =>
        {
            SetCurrentValue(CurveProperty, CurveEditing.RemoveKey(Curve, index));
            SetCurrentValue(SelectedKeyIndexProperty, Math.Min(index, Curve.Keys.Length - 1));
        });
    }

    /// <summary>Sets the interpolation of the selected key as one edit.</summary>
    public void SetSelectedInterpolation(CurveInterpolation interpolation)
    {
        if (SelectedKeyIndex < 0)
            return;
        var index = SelectedKeyIndex;
        Edit(() => SetCurrentValue(CurveProperty, CurveEditing.SetInterpolation(Curve, index, interpolation)));
    }

    /// <summary>Moves the selected key to an exact time and value as one edit.</summary>
    public void SetSelectedKey(float time, float value)
    {
        if (SelectedKeyIndex < 0)
            return;
        var index = SelectedKeyIndex;
        Edit(() => SetCurrentValue(CurveProperty, CurveEditing.MoveKey(Curve, index, time, value)));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CurveProperty)
        {
            _line = null;
            if (SelectedKeyIndex >= Curve.Keys.Length)
                SetCurrentValue(SelectedKeyIndexProperty, Curve.Keys.Length - 1);
            if (!_editing && _drag == Drag.None)
                _keepInViewPending = true;
            InvalidateVisual();
        }
        else if (change.Property == CurveBrushProperty || change.Property == GridBrushProperty || change.Property == AxisBrushProperty
                 || change.Property == KeyBrushProperty || change.Property == KeyFillProperty || change.Property == HandleBrushProperty
                 || change.Property == LabelBrushProperty)
        {
            _pens = null;
            _labels.Clear();
            InvalidateVisual();
        }
        else if (change.Property == BoundsProperty)
        {
            _line = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < Margins.Left + Margins.Right + 8 || bounds.Height < Margins.Top + Margins.Bottom + 8)
            return;

        if (_fitPending || (_keepInViewPending && !IsCurveInView()))
        {
            _viewport = CurveViewport.Fit(Curve);
            _line = null;
        }

        _fitPending = false;
        _keepInViewPending = false;

        _pens ??= new Pens(this);
        var area = PlotArea;
        if (Background is { } background)
            context.FillRectangle(background, bounds);

        using (context.PushClip(area))
        {
            if (RangeBrush is { } range)
            {
                var left = Viewport.ToScreen(0, 0, area).X;
                var right = Viewport.ToScreen(1, 0, area).X;
                context.FillRectangle(range, new Rect(left, area.Y, Math.Max(0, right - left), area.Height));
            }

            DrawGrid(context, area);
            if (_line is null || _geometrySize != Bounds.Size)
                BuildGeometry(area);
            if (CurveFill is { } fill)
                context.DrawGeometry(fill, null, _fill!);
            context.DrawGeometry(null, _pens.Curve, _line!);
        }

        DrawLabels(context, area);
        DrawKeys(context, area);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        var position = point.Position;
        _lastPointer = position;

        if (point.Properties.IsMiddleButtonPressed)
        {
            BeginDrag(Drag.Pan, position, e);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            if (point.Properties.IsRightButtonPressed)
            {
                _menuKey = HitTest(position) is { Kind: HitKind.Key } key ? key.Index : -1;
                if (_menuKey >= 0)
                    SetCurrentValue(SelectedKeyIndexProperty, _menuKey);
            }

            return;
        }

        var hit = HitTest(position);
        switch (hit.Kind)
        {
            case HitKind.InHandle or HitKind.OutHandle:
                BeginDrag(hit.Kind == HitKind.InHandle ? Drag.InHandle : Drag.OutHandle, position, e);
                ValueEdit.RaiseStarted(this);
                break;
            case HitKind.Key:
                SetCurrentValue(SelectedKeyIndexProperty, hit.Index);
                var key = Curve.Keys[hit.Index];
                _keyStart = (key.Time, key.Value);
                BeginDrag(Drag.Key, position, e);
                ValueEdit.RaiseStarted(this);
                break;
            default:
                if (e.ClickCount == 2)
                {
                    AddKeyAt(position, e.KeyModifiers);
                    e.Handled = true;
                    return;
                }

                SetCurrentValue(SelectedKeyIndexProperty, -1);
                BeginDrag(Drag.Pan, position, e);
                break;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var area = PlotArea;

        switch (_drag)
        {
            case Drag.None:
                var hover = HitTest(position);
                if (hover != _hover)
                {
                    _hover = hover;
                    Cursor = hover.Kind == HitKind.None ? Cursor.Default : new Cursor(StandardCursorType.Hand);
                    InvalidateVisual();
                }

                return;
            case Drag.Pan:
                Viewport = Viewport.Pan(position - _lastPointer, area);
                break;
            case Drag.Key:
                MoveKey(position, e.KeyModifiers, area);
                break;
            case Drag.InHandle or Drag.OutHandle:
                MoveHandle(position, e.KeyModifiers, area);
                break;
        }

        _lastPointer = position;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Drag.None)
            return;
        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover.Kind == HitKind.None)
            return;
        _hover = default;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = Math.Pow(1.15, e.Delta.Y);
        var time = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 1 : factor;
        var value = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : factor;
        Viewport = Viewport.ZoomAt(e.GetPosition(this), PlotArea, time, value);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Delete or Key.Back:
                DeleteSelectedKey();
                e.Handled = true;
                break;
            case Key.F:
                FitView();
                e.Handled = true;
                break;
            case Key.Left when Curve.Keys.Length > 0:
                SetCurrentValue(SelectedKeyIndexProperty, Math.Max(0, SelectedKeyIndex - 1));
                e.Handled = true;
                break;
            case Key.Right when Curve.Keys.Length > 0:
                SetCurrentValue(SelectedKeyIndexProperty, Math.Min(Curve.Keys.Length - 1, SelectedKeyIndex + 1));
                e.Handled = true;
                break;
        }
    }

    private void BeginDrag(Drag drag, Point position, PointerPressedEventArgs e)
    {
        _drag = drag;
        _dragStart = position;
        _lastPointer = position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void EndDrag()
    {
        var drag = _drag;
        _drag = Drag.None;
        if (drag is Drag.Key or Drag.InHandle or Drag.OutHandle)
        {
            ValueEdit.RaiseCompleted(this);
            KeepCurveInView();
        }
    }

    private void KeepCurveInView()
    {
        if (!IsCurveInView())
            FitView();
    }

    private bool IsCurveInView()
    {
        foreach (var key in Curve.Keys)
        {
            if (!Viewport.Contains(key.Time, key.Value))
                return false;
        }

        return true;
    }

    private void AddKeyAt(Point position, KeyModifiers modifiers)
    {
        var (time, value) = Snap(Viewport.ToCurve(position, PlotArea), modifiers);
        Edit(() =>
        {
            SetCurrentValue(CurveProperty, CurveEditing.AddKey(Curve, time, value, out var index));
            SetCurrentValue(SelectedKeyIndexProperty, index);
        });
    }

    private void MoveKey(Point position, KeyModifiers modifiers, Rect area)
    {
        var index = SelectedKeyIndex;
        if (index < 0)
            return;

        var (time, value) = Viewport.ToCurve(position, area);
        var (startTime, startValue) = Viewport.ToCurve(_dragStart, area);
        var t = _keyStart.Time + (float)(time - startTime);
        var v = _keyStart.Value + (float)(value - startValue);
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            var delta = position - _dragStart;
            if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                v = _keyStart.Value;
            else
                t = _keyStart.Time;
        }

        (t, v) = Snap((t, v), modifiers);
        SetCurrentValue(CurveProperty, CurveEditing.MoveKey(Curve, index, t, v));
    }

    private void MoveHandle(Point position, KeyModifiers modifiers, Rect area)
    {
        var index = SelectedKeyIndex;
        if (index < 0)
            return;

        var key = Curve.Keys[index];
        var (time, value) = Viewport.ToCurve(position, area);
        var isOut = _drag == Drag.OutHandle;
        var slope = CurveEditing.SlopeFromHandle((key.Time, key.Value), ((float)time, (float)value), isOut);
        if (modifiers.HasFlag(KeyModifiers.Control))
            slope = MathF.Round(slope * 4) / 4;

        var curve = modifiers.HasFlag(KeyModifiers.Alt)
            ? CurveEditing.SetTangents(Curve, index, isOut ? key.InTangent : slope, isOut ? slope : key.OutTangent)
            : CurveEditing.SetTangents(Curve, index, slope, slope);
        SetCurrentValue(CurveProperty, curve);
    }

    private (float Time, float Value) Snap((double Time, double Value) point, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Control))
            return ((float)point.Time, (float)point.Value);
        var area = PlotArea;
        var timeStep = (float)CurveViewport.GridStep(Viewport.TimeSpan, area.Width, 48) / 2;
        var valueStep = (float)CurveViewport.GridStep(Viewport.ValueSpan, area.Height, 32) / 2;
        return (CurveEditing.Snap((float)point.Time, timeStep), CurveEditing.Snap((float)point.Value, valueStep));
    }

    private void Edit(Action change)
    {
        _editing = true;
        try
        {
            ValueEdit.Apply(this, change);
        }
        finally
        {
            _editing = false;
        }
    }

    private Hit HitTest(Point position)
    {
        var area = PlotArea;
        var keys = Curve.Keys;
        var selected = SelectedKeyIndex;
        if (selected >= 0 && selected < keys.Length && HasHandles(selected))
        {
            var (inHandle, outHandle) = Handles(selected, area);
            if (selected > 0 && Point.Distance(position, inHandle) <= HitRadius)
                return new Hit(HitKind.InHandle, selected);
            if (selected < keys.Length - 1 && Point.Distance(position, outHandle) <= HitRadius)
                return new Hit(HitKind.OutHandle, selected);
        }

        var best = -1;
        var bestDistance = HitRadius;
        for (var i = 0; i < keys.Length; i++)
        {
            var distance = Point.Distance(position, Viewport.ToScreen(keys[i].Time, keys[i].Value, area));
            if (distance <= bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best >= 0 ? new Hit(HitKind.Key, best) : default;
    }

    private bool HasHandles(int index)
    {
        var keys = Curve.Keys;
        var leaving = index < keys.Length - 1 && keys[index].Interpolation == CurveInterpolation.Smooth;
        var arriving = index > 0 && keys[index - 1].Interpolation == CurveInterpolation.Smooth;
        return leaving || arriving;
    }

    private (Point In, Point Out) Handles(int index, Rect area)
    {
        var key = Curve.Keys[index];
        var center = Viewport.ToScreen(key.Time, key.Value, area);
        var scaleX = area.Width / Viewport.TimeSpan;
        var scaleY = area.Height / Viewport.ValueSpan;
        return (center - Direction(key.InTangent), center + Direction(key.OutTangent));

        Vector Direction(float slope)
        {
            var vector = new Vector(scaleX, -slope * scaleY);
            var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
            return length <= 0 ? default : vector / length * HandleLength;
        }
    }

    private void BuildGeometry(Rect area)
    {
        _geometrySize = Bounds.Size;
        var curve = Curve;
        var baseline = Math.Clamp(Viewport.ToScreen(0, 0, area).Y, area.Top, area.Bottom);
        _line = new StreamGeometry();
        _fill = new StreamGeometry();
        using var line = _line.Open();
        using var fill = _fill.Open();
        var samples = Math.Max(2, (int)area.Width);
        for (var i = 0; i <= samples; i++)
        {
            var x = area.X + area.Width * i / samples;
            var (time, _) = Viewport.ToCurve(new Point(x, 0), area);
            var point = new Point(x, Viewport.ToScreen(time, curve.Evaluate((float)time), area).Y);
            if (i == 0)
            {
                line.BeginFigure(point, false);
                fill.BeginFigure(new Point(x, baseline), true);
            }
            else
            {
                line.LineTo(point);
            }

            fill.LineTo(point);
        }

        line.EndFigure(false);
        fill.LineTo(new Point(area.Right, baseline));
        fill.EndFigure(true);
    }

    private void DrawGrid(DrawingContext context, Rect area)
    {
        var pens = _pens!;
        var timeStep = CurveViewport.GridStep(Viewport.TimeSpan, area.Width, 48);
        for (var t = Math.Ceiling(Viewport.TimeMin / timeStep) * timeStep; t <= Viewport.TimeMax; t += timeStep)
        {
            var x = Math.Round(Viewport.ToScreen(t, 0, area).X) + 0.5;
            context.DrawLine(Math.Abs(t) < timeStep / 2 || Math.Abs(t - 1) < timeStep / 2 ? pens.Axis : pens.Grid, new Point(x, area.Top), new Point(x, area.Bottom));
        }

        var valueStep = CurveViewport.GridStep(Viewport.ValueSpan, area.Height, 32);
        for (var v = Math.Ceiling(Viewport.ValueMin / valueStep) * valueStep; v <= Viewport.ValueMax; v += valueStep)
        {
            var y = Math.Round(Viewport.ToScreen(0, v, area).Y) + 0.5;
            context.DrawLine(Math.Abs(v) < valueStep / 2 ? pens.Axis : pens.Grid, new Point(area.Left, y), new Point(area.Right, y));
        }
    }

    private void DrawLabels(DrawingContext context, Rect area)
    {
        var timeStep = CurveViewport.GridStep(Viewport.TimeSpan, area.Width, 48);
        for (var t = Math.Ceiling(Viewport.TimeMin / timeStep) * timeStep; t <= Viewport.TimeMax; t += timeStep)
        {
            var text = Label(t, timeStep);
            var x = Viewport.ToScreen(t, 0, area).X - text.Width / 2;
            context.DrawText(text, new Point(Math.Clamp(x, area.Left - 4, area.Right - text.Width + 4), area.Bottom + 5));
        }

        var valueStep = CurveViewport.GridStep(Viewport.ValueSpan, area.Height, 32);
        for (var v = Math.Ceiling(Viewport.ValueMin / valueStep) * valueStep; v <= Viewport.ValueMax; v += valueStep)
        {
            var text = Label(v, valueStep);
            var y = Viewport.ToScreen(0, v, area).Y - text.Height / 2;
            context.DrawText(text, new Point(area.Left - text.Width - 7, Math.Clamp(y, area.Top - 4, area.Bottom - text.Height + 4)));
        }
    }

    private void DrawKeys(DrawingContext context, Rect area)
    {
        var pens = _pens!;
        var keys = Curve.Keys;
        var selected = SelectedKeyIndex;

        if (selected >= 0 && selected < keys.Length && HasHandles(selected))
        {
            var center = Viewport.ToScreen(keys[selected].Time, keys[selected].Value, area);
            var (inHandle, outHandle) = Handles(selected, area);
            if (selected > 0)
                DrawHandle(context, pens, center, inHandle, _hover is { Kind: HitKind.InHandle } || _drag == Drag.InHandle);
            if (selected < keys.Length - 1)
                DrawHandle(context, pens, center, outHandle, _hover is { Kind: HitKind.OutHandle } || _drag == Drag.OutHandle);
        }

        for (var i = 0; i < keys.Length; i++)
        {
            var center = Viewport.ToScreen(keys[i].Time, keys[i].Value, area);
            if (!area.Inflate(KeySize).Contains(center))
                continue;
            var isSelected = i == selected;
            var isHovered = _hover is { Kind: HitKind.Key } hover && hover.Index == i;
            var size = isSelected || isHovered ? KeySize + 1 : KeySize;
            var diamond = Diamond(center, size);
            context.DrawGeometry(isSelected ? pens.AccentBrush : pens.KeyFillBrush, isSelected ? pens.SelectedOutline : isHovered ? pens.Accent : pens.Key, diamond);
        }

        if (_drag == Drag.Key && selected >= 0 && selected < keys.Length)
        {
            var key = keys[selected];
            var text = Readout($"{key.Time.ToString("0.###", CultureInfo.CurrentCulture)}, {key.Value.ToString("0.###", CultureInfo.CurrentCulture)}");
            var center = Viewport.ToScreen(key.Time, key.Value, area);
            var origin = new Point(Math.Clamp(center.X - text.Width / 2, 2, Bounds.Width - text.Width - 2), Math.Max(2, center.Y - text.Height - 12));
            context.DrawRectangle(pens.ReadoutBackground, null, new RoundedRect(new Rect(origin, new Size(text.Width, text.Height)).Inflate(new Thickness(5, 2)), 4));
            context.DrawText(text, origin);
        }
    }

    private static void DrawHandle(DrawingContext context, Pens pens, Point key, Point handle, bool active)
    {
        context.DrawLine(pens.Handle, key, handle);
        context.DrawEllipse(active ? pens.AccentBrush : pens.KeyFillBrush, active ? pens.Accent : pens.Handle, handle, 3.5, 3.5);
    }

    private static StreamGeometry Diamond(Point center, double size)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(new Point(center.X, center.Y - size), true);
        ctx.LineTo(new Point(center.X + size, center.Y));
        ctx.LineTo(new Point(center.X, center.Y + size));
        ctx.LineTo(new Point(center.X - size, center.Y));
        ctx.EndFigure(true);
        return geometry;
    }

    private FormattedText Label(double value, double step)
    {
        var decimals = Math.Clamp((int)Math.Ceiling(-Math.Log10(step)), 0, 6);
        if (Math.Abs(value) < step / 1000)
            value = 0;
        var text = Math.Round(value, decimals).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
        if (!_labels.TryGetValue(text, out var formatted))
        {
            if (_labels.Count > 256)
                _labels.Clear();
            formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, LabelBrush ?? Brushes.Gray);
            _labels[text] = formatted;
        }

        return formatted;
    }

    private FormattedText Readout(string text) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 11, KeyBrush ?? Brushes.White);

    private MenuFlyout CreateMenu()
    {
        var menu = new MenuFlyout();
        // A menu flyout whose items start out empty never shows the items added when it opens.
        Fill();
        menu.Opening += (_, _) => Fill();
        return menu;

        void Fill()
        {
            menu.Items.Clear();
            var index = _menuKey;
            var hasKey = index >= 0 && index < Curve.Keys.Length;
            if (hasKey)
            {
                var interpolation = Curve.Keys[index].Interpolation;
                foreach (var (name, mode) in new[] { ("Smooth", CurveInterpolation.Smooth), ("Linear", CurveInterpolation.Linear), ("Constant", CurveInterpolation.Constant) })
                {
                    var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = interpolation == mode, GroupName = "interpolation" };
                    item.Click += (_, _) => SetSelectedInterpolation(mode);
                    menu.Items.Add(item);
                }

                menu.Items.Add(new Separator());
                AddItem(menu, "Flat tangents", null, () => Edit(() => SetCurrentValue(CurveProperty, CurveEditing.Flatten(Curve, index))));
                AddItem(menu, "Auto tangents", null, () => Edit(() => SetCurrentValue(CurveProperty, CurveEditing.AutoTangents(Curve, index))));
                menu.Items.Add(new Separator());
                AddItem(menu, "Delete key", Icons.Trash, DeleteSelectedKey, Curve.Keys.Length > 1, "Del");
            }
            else
            {
                var position = _lastPointer;
                AddItem(menu, "Add key", Icons.Plus, () => AddKeyAt(position, KeyModifiers.None));
            }

            menu.Items.Add(new Separator());
            AddItem(menu, "Fit view", Icons.Maximize, FitView, true, "F");
        }
    }

    private static void AddItem(MenuFlyout menu, string header, Geometry? icon, Action action, bool enabled = true, string? gesture = null)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 },
            InputGesture = gesture is null ? null : KeyGesture.Parse(gesture == "Del" ? "Delete" : gesture)
        };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private enum Drag
    {
        None,
        Pan,
        Key,
        InHandle,
        OutHandle
    }

    private enum HitKind
    {
        None,
        Key,
        InHandle,
        OutHandle
    }

    private readonly record struct Hit(HitKind Kind, int Index);

    private sealed class Pens(CurveCanvas canvas)
    {
        public IPen Grid { get; } = new Pen(canvas.GridBrush ?? Brushes.Gray, 1);

        public IPen Axis { get; } = new Pen(canvas.AxisBrush ?? Brushes.Gray, 1);

        public IPen Curve { get; } = new Pen(canvas.CurveBrush ?? Brushes.CornflowerBlue, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        public IPen Key { get; } = new Pen(canvas.KeyBrush ?? Brushes.White, 1.5);

        public IPen Accent { get; } = new Pen(canvas.CurveBrush ?? Brushes.CornflowerBlue, 1.5);

        public IPen SelectedOutline { get; } = new Pen(canvas.KeyBrush ?? Brushes.White, 1.5);

        public IPen Handle { get; } = new Pen(canvas.HandleBrush ?? Brushes.Gray, 1);

        public IBrush AccentBrush { get; } = canvas.CurveBrush ?? Brushes.CornflowerBlue;

        public IBrush KeyFillBrush { get; } = canvas.KeyFill ?? Brushes.Black;

        public IBrush ReadoutBackground { get; } = canvas.Background ?? Brushes.Black;
    }
}
