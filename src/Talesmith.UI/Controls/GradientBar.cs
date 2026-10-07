using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.Mathematics;
using GradientStop = Talesmith.Mathematics.GradientStop;

namespace Talesmith.UI.Controls;

/// <summary>The stops bar of a <see cref="GradientEditor"/>: click to add a stop, drag stops to move them, drag a stop away or press Delete
/// to remove it.</summary>
public class GradientBar : Control
{
    public static readonly StyledProperty<Gradient> GradientProperty =
        AvaloniaProperty.Register<GradientBar, Gradient>(nameof(Gradient), Gradient.White, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<int> SelectedStopIndexProperty =
        AvaloniaProperty.Register<GradientBar, int>(nameof(SelectedStopIndex), -1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
        AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(MarkerBrush));

    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        AvaloniaProperty.Register<GradientBar, IBrush?>(nameof(SelectionBrush));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<GradientBar>();

    private const double MarkerArea = 18;
    private const double MarkerSize = 12;
    private const double Inset = MarkerSize / 2 + 1;
    private const double RemoveDistance = 28;

    private IBrush? _brush;
    private bool _dragging;
    private bool _removing;
    private double _grabOffset;
    private GradientStop _dragged;

    static GradientBar()
    {
        FocusableProperty.OverrideDefaultValue<GradientBar>(true);
        AffectsRender<GradientBar>(GradientProperty, SelectedStopIndexProperty, MarkerBrushProperty, SelectionBrushProperty, BorderBrushProperty);
    }

    /// <summary>Raised before the first change of an edit.</summary>
    public event EventHandler<RoutedEventArgs>? EditStarted
    {
        add => AddHandler(ValueEdit.StartedEvent, value);
        remove => RemoveHandler(ValueEdit.StartedEvent, value);
    }

    /// <summary>Raised after the last change of an edit.</summary>
    public event EventHandler<RoutedEventArgs>? EditCompleted
    {
        add => AddHandler(ValueEdit.CompletedEvent, value);
        remove => RemoveHandler(ValueEdit.CompletedEvent, value);
    }

    public Gradient Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    /// <summary>Gets or sets the index of the selected stop, or -1.</summary>
    public int SelectedStopIndex
    {
        get => GetValue(SelectedStopIndexProperty);
        set => SetValue(SelectedStopIndexProperty, value);
    }

    /// <summary>Gets or sets the outline of the stop markers.</summary>
    public IBrush? MarkerBrush
    {
        get => GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    /// <summary>Gets or sets the outline of the selected stop marker.</summary>
    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Replaces the color of the selected stop.</summary>
    public void SetSelectedColor(Mathematics.Color color)
    {
        var index = SelectedStopIndex;
        if (index < 0 || index >= Gradient.Stops.Length)
            return;
        SetCurrentValue(GradientProperty, new Gradient(Gradient.Stops.SetItem(index, Gradient.Stops[index] with { Color = color })));
    }

    /// <summary>Moves the selected stop and keeps it selected.</summary>
    public void SetSelectedPosition(float position)
    {
        var index = SelectedStopIndex;
        if (index < 0 || index >= Gradient.Stops.Length)
            return;
        MoveStop(index, Math.Clamp(position, 0, 1));
    }

    /// <summary>Removes the selected stop as one edit; the last stop stays.</summary>
    public void RemoveSelectedStop()
    {
        var index = SelectedStopIndex;
        var stops = Gradient.Stops;
        if (index < 0 || index >= stops.Length || stops.Length <= 1)
            return;
        ValueEdit.Apply(this, () =>
        {
            SetCurrentValue(GradientProperty, new Gradient(stops.RemoveAt(index)));
            SetCurrentValue(SelectedStopIndexProperty, Math.Min(index, Gradient.Stops.Length - 1));
        });
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GradientProperty)
        {
            _brush = null;
            if (SelectedStopIndex >= Gradient.Stops.Length)
                SetCurrentValue(SelectedStopIndexProperty, Gradient.Stops.Length - 1);
        }
    }

    public override void Render(DrawingContext context)
    {
        var strip = Strip;
        if (strip.Width <= 0 || strip.Height <= 0)
            return;

        var shape = new RoundedRect(strip, 5);
        using (context.PushClip(shape))
        {
            EngineColors.DrawChecker(context, strip, 6);
            _brush ??= GradientSwatch.CreateBrush(Gradient);
            context.FillRectangle(_brush, strip);
        }

        if (BorderBrush is { } border)
            context.DrawRectangle(null, new Pen(border), new RoundedRect(strip.Deflate(0.5), 5));

        var stops = Gradient.Stops;
        for (var i = 0; i < stops.Length; i++)
        {
            if (i != SelectedStopIndex)
                DrawMarker(context, stops[i], selected: false);
        }

        if (SelectedStopIndex >= 0 && SelectedStopIndex < stops.Length)
            DrawMarker(context, stops[SelectedStopIndex], selected: true);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        Focus();
        var position = e.GetPosition(this);
        var hit = HitStop(position);
        ValueEdit.RaiseStarted(this);
        if (hit < 0)
        {
            var at = (float)Math.Clamp(ToPosition(position.X), 0, 1);
            var stop = new GradientStop(at, Gradient.Evaluate(at));
            var gradient = new Gradient(Gradient.Stops.Add(stop));
            SetCurrentValue(GradientProperty, gradient);
            hit = gradient.Stops.IndexOf(stop);
        }

        SetCurrentValue(SelectedStopIndexProperty, hit);
        _dragged = Gradient.Stops[hit];
        _grabOffset = position.X - ToX(_dragged.Position);
        _dragging = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (!_dragging)
        {
            Cursor = HitStop(position) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : new Cursor(StandardCursorType.Hand);
            return;
        }

        var removing = Gradient.Stops.Length > 1 && (position.Y > Bounds.Height + RemoveDistance || position.Y < -RemoveDistance);
        if (removing != _removing)
        {
            _removing = removing;
            Opacity = removing ? 0.6 : 1;
        }

        MoveStop(SelectedStopIndex, (float)Math.Clamp(ToPosition(position.X - _grabOffset), 0, 1));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back)
        {
            RemoveSelectedStop();
            e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right && SelectedStopIndex >= 0)
        {
            var step = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1f : 0.01f) * (e.Key == Key.Left ? -1 : 1);
            ValueEdit.Apply(this, () => SetSelectedPosition(Gradient.Stops[SelectedStopIndex].Position + step));
            e.Handled = true;
        }
    }

    private Rect Strip => new(Inset, 0, Math.Max(0, Bounds.Width - Inset * 2), Math.Max(0, Bounds.Height - MarkerArea));

    private double ToX(float position) => Strip.X + position * Strip.Width;

    private double ToPosition(double x) => (x - Strip.X) / Math.Max(1, Strip.Width);

    private int HitStop(Point position)
    {
        var stops = Gradient.Stops;
        var best = -1;
        var bestDistance = MarkerSize / 2 + 3;
        for (var i = 0; i < stops.Length; i++)
        {
            var distance = Math.Abs(position.X - ToX(stops[i].Position));
            if (distance <= bestDistance && position.Y >= Strip.Bottom - 4)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void MoveStop(int index, float position)
    {
        var stops = Gradient.Stops;
        if (index < 0 || index >= stops.Length)
            return;
        var moved = stops[index] with { Position = position };
        var gradient = new Gradient(stops.SetItem(index, moved));
        SetCurrentValue(GradientProperty, gradient);
        SetCurrentValue(SelectedStopIndexProperty, gradient.Stops.IndexOf(moved));
        _dragged = moved;
    }

    private void EndDrag()
    {
        if (!_dragging)
            return;
        _dragging = false;
        if (_removing)
        {
            _removing = false;
            Opacity = 1;
            var index = SelectedStopIndex;
            if (index >= 0 && Gradient.Stops.Length > 1)
            {
                SetCurrentValue(GradientProperty, new Gradient(Gradient.Stops.RemoveAt(index)));
                SetCurrentValue(SelectedStopIndexProperty, Math.Min(index, Gradient.Stops.Length - 1));
            }
        }

        ValueEdit.RaiseCompleted(this);
    }

    private void DrawMarker(DrawingContext context, GradientStop stop, bool selected)
    {
        var x = Math.Round(ToX(stop.Position));
        var top = Strip.Bottom + 3;
        var body = new Rect(x - MarkerSize / 2, top + 3, MarkerSize, MarkerSize);
        var outline = selected ? SelectionBrush ?? MarkerBrush ?? Brushes.White : MarkerBrush ?? Brushes.Gray;
        var pen = new Pen(outline, selected ? 2 : 1.25);

        var arrow = new StreamGeometry();
        using (var ctx = arrow.Open())
        {
            ctx.BeginFigure(new Point(x, top - 1), true);
            ctx.LineTo(new Point(x + 4, top + 3.5));
            ctx.LineTo(new Point(x - 4, top + 3.5));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(outline, null, arrow);
        var rounded = new RoundedRect(body, 3);
        using (context.PushClip(rounded))
        {
            if (stop.Color.A < 255)
                EngineColors.DrawChecker(context, body, 3);
            context.FillRectangle(new ImmutableSolidColorBrush(stop.Color.ToAvalonia()), body);
        }

        context.DrawRectangle(null, pen, rounded);
    }
}
