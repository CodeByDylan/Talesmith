using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.Mathematics;
using AvaloniaGradientStop = Avalonia.Media.GradientStop;
using EngineColor = Talesmith.Mathematics.Color;

namespace Talesmith.UI.Controls;

/// <summary>Draws a <see cref="Gradient"/> left to right; translucent gradients show their opaque colors above their alpha over a checkerboard.</summary>
public class GradientSwatch : Control
{
    public static readonly StyledProperty<Gradient?> GradientProperty =
        AvaloniaProperty.Register<GradientSwatch, Gradient?>(nameof(Gradient));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<GradientSwatch>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<GradientSwatch>();

    private IBrush? _brush;
    private IBrush? _opaqueBrush;
    private IPen? _border;

    static GradientSwatch()
    {
        AffectsRender<GradientSwatch>(GradientProperty, CornerRadiusProperty, BorderBrushProperty);
    }

    public Gradient? Gradient
    {
        get => GetValue(GradientProperty);
        set => SetValue(GradientProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>Creates a horizontal brush showing <paramref name="gradient"/>.</summary>
    public static IBrush CreateBrush(Gradient gradient) => CreateBrush(gradient, opaque: false);

    private static IBrush CreateBrush(Gradient gradient, bool opaque)
    {
        var stops = gradient.Stops;
        if (stops.Length == 0)
            return Brushes.White;
        if (stops.Length == 1)
            return new SolidColorBrush(ToAvalonia(stops[0].Color, opaque));

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };
        foreach (var stop in stops)
            brush.GradientStops.Add(new AvaloniaGradientStop(ToAvalonia(stop.Color, opaque), Math.Clamp(stop.Position, 0, 1)));
        return brush;
    }

    private static Avalonia.Media.Color ToAvalonia(EngineColor color, bool opaque) =>
        opaque ? Avalonia.Media.Color.FromRgb(color.R, color.G, color.B) : color.ToAvalonia();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GradientProperty)
            _brush = _opaqueBrush = null;
        else if (change.Property == BorderBrushProperty)
            _border = null;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Gradient is not { } gradient || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var shape = new RoundedRect(bounds, CornerRadius);
        using (context.PushClip(shape))
        {
            _brush ??= CreateBrush(gradient);
            if (gradient.Stops.Any(s => s.Color.A < 255))
            {
                var split = Math.Round(bounds.Height * 0.55);
                var alpha = new Rect(0, split, bounds.Width, bounds.Height - split);
                _opaqueBrush ??= CreateBrush(gradient, opaque: true);
                context.FillRectangle(_opaqueBrush, new Rect(0, 0, bounds.Width, split));
                EngineColors.DrawChecker(context, alpha, Math.Max(2, Math.Round(alpha.Height / 2)));
                context.FillRectangle(_brush, alpha);
            }
            else
                context.FillRectangle(_brush, bounds);
        }

        if (BorderBrush is { } border)
        {
            _border ??= new Pen(border);
            context.DrawRectangle(null, _border, new RoundedRect(bounds.Deflate(0.5), CornerRadius));
        }
    }
}
