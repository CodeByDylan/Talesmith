using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>Draws a <see cref="Curve"/> over time 0 to 1 as a small filled line, for previews and preset buttons.</summary>
public class CurveSwatch : Control
{
    public static readonly StyledProperty<Curve?> CurveProperty =
        AvaloniaProperty.Register<CurveSwatch, Curve?>(nameof(Curve));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CurveSwatch, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CurveSwatch, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CurveSwatch, double>(nameof(StrokeThickness), 1.5);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        Decorator.PaddingProperty.AddOwner<CurveSwatch>();

    private StreamGeometry? _line;
    private StreamGeometry? _area;
    private Size _geometrySize;
    private IPen? _pen;

    static CurveSwatch()
    {
        AffectsRender<CurveSwatch>(CurveProperty, StrokeProperty, FillProperty, StrokeThicknessProperty, PaddingProperty);
    }

    public Curve? Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurveProperty || change.Property == PaddingProperty)
            _line = null;
        else if (change.Property == StrokeProperty || change.Property == StrokeThicknessProperty)
            _pen = null;
    }

    public override void Render(DrawingContext context)
    {
        if (Curve is not { } curve || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        if (_line is null || _geometrySize != Bounds.Size)
            BuildGeometry(curve);

        if (Fill is { } fill)
            context.DrawGeometry(fill, null, _area!);
        if (Stroke is { } stroke)
        {
            _pen ??= new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, _pen, _line!);
        }
    }

    private void BuildGeometry(Curve curve)
    {
        _geometrySize = Bounds.Size;
        var area = new Rect(Bounds.Size).Deflate(Padding);
        var (min, max) = CurveEditing.ValueRange(curve, 0, 1, 32);
        min = Math.Min(min, 0);
        max = Math.Max(max, Math.Max(1, min + 1e-3f));
        var view = new CurveViewport(0, 1, min, max);
        var samples = Math.Max(8, (int)area.Width);

        _line = new StreamGeometry();
        _area = new StreamGeometry();
        using var line = _line.Open();
        using var fill = _area.Open();
        var baseline = view.ToScreen(0, Math.Clamp(0, min, max), area).Y;
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            var point = view.ToScreen(t, curve.Evaluate((float)t), area);
            if (i == 0)
            {
                line.BeginFigure(point, false);
                fill.BeginFigure(new Point(point.X, baseline), true);
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
}
