using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Talesmith.UI.Controls;

/// <summary>Plots a <see cref="ValueHistory"/> as a filled line chart with an optional threshold marker.</summary>
public class Sparkline : Control
{
    public static readonly StyledProperty<ValueHistory?> HistoryProperty =
        AvaloniaProperty.Register<Sparkline, ValueHistory?>(nameof(History));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum));

    public static readonly StyledProperty<double> ThresholdProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Threshold));

    public static readonly StyledProperty<IBrush?> ThresholdBrushProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(ThresholdBrush));

    private static readonly AvaloniaList<double> ThresholdDashes = [3, 3];

    private IPen? _strokePen;
    private IPen? _thresholdPen;

    static Sparkline()
    {
        AffectsRender<Sparkline>(StrokeProperty, FillProperty, MaximumProperty, ThresholdProperty, ThresholdBrushProperty);
    }

    /// <summary>Gets or sets the samples to plot.</summary>
    public ValueHistory? History
    {
        get => GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    /// <summary>Gets or sets the line brush.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Gets or sets the brush painted beneath the line.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets the value mapped to the top edge; zero scales to the data.</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the value at which a dashed marker is drawn; zero hides it.</summary>
    public double Threshold
    {
        get => GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    /// <summary>Gets or sets the brush of the threshold marker.</summary>
    public IBrush? ThresholdBrush
    {
        get => GetValue(ThresholdBrushProperty);
        set => SetValue(ThresholdBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == HistoryProperty)
        {
            if (change.OldValue is ValueHistory oldHistory)
            {
                oldHistory.Changed -= OnHistoryChanged;
            }

            if (change.NewValue is ValueHistory newHistory && this.IsAttachedToVisualTree())
            {
                newHistory.Changed += OnHistoryChanged;
            }

            InvalidateVisual();
        }
        else if (change.Property == StrokeProperty)
        {
            _strokePen = null;
        }
        else if (change.Property == ThresholdBrushProperty)
        {
            _thresholdPen = null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        History?.Changed += OnHistoryChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        History?.Changed -= OnHistoryChanged;
    }

    public override void Render(DrawingContext context)
    {
        var history = History;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (history is null || history.Count < 2 || width <= 0 || height <= 0)
        {
            return;
        }

        var max = Maximum > 0 ? Maximum : Math.Max(history.Max, Threshold) * 1.15;
        if (max <= 0)
        {
            return;
        }

        var step = width / (history.Capacity - 1);
        var startX = width - ((history.Count - 1) * step);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            for (var i = 0; i < history.Count; i++)
            {
                var point = new Point(startX + (i * step), Y(history[i]));
                if (i == 0)
                {
                    ctx.BeginFigure(point, false);
                }
                else
                {
                    ctx.LineTo(point);
                }
            }

            ctx.EndFigure(false);
        }

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(startX, height), true);
                for (var i = 0; i < history.Count; i++)
                {
                    ctx.LineTo(new Point(startX + (i * step), Y(history[i])));
                }

                ctx.LineTo(new Point(width, height));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(fill, null, area);
        }

        if (Threshold > 0 && Threshold <= max && ThresholdBrush is { } thresholdBrush)
        {
            _thresholdPen ??= new Pen(thresholdBrush, 1, new DashStyle(ThresholdDashes, 0));
            var y = Math.Round(Y(Threshold)) + 0.5;
            context.DrawLine(_thresholdPen, new Point(0, y), new Point(width, y));
        }

        if (Stroke is { } stroke)
        {
            _strokePen ??= new Pen(stroke, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, _strokePen, line);
        }

        double Y(double value) => height - (Math.Clamp(value / max, 0, 1) * (height - 1.5)) - 0.75;
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => InvalidateVisual();
}
