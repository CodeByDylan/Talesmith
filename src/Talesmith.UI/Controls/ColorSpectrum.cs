using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A saturation/value square for a fixed hue.</summary>
public class ColorSpectrum : Control
{
    public static readonly StyledProperty<double> HueProperty =
        AvaloniaProperty.Register<ColorSpectrum, double>(nameof(Hue));

    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<ColorSpectrum, double>(nameof(Saturation), 1, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ColorSpectrum, double>(nameof(Value), 1, defaultBindingMode: BindingMode.TwoWay);

    private static readonly IBrush WhiteFade = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) }
    };

    private static readonly IBrush BlackFade = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 1) }
    };

    static ColorSpectrum()
    {
        AffectsRender<ColorSpectrum>(HueProperty, SaturationProperty, ValueProperty);
        CursorProperty.OverrideDefaultValue<ColorSpectrum>(new Cursor(StandardCursorType.Cross));
    }

    /// <summary>The hue in degrees, from 0 to 360.</summary>
    public double Hue
    {
        get => GetValue(HueProperty);
        set => SetValue(HueProperty, value);
    }

    /// <summary>The saturation from 0 to 1, along the horizontal axis.</summary>
    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    /// <summary>The value (brightness) from 0 to 1, along the vertical axis with 1 at the top.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        using (context.PushClip(new RoundedRect(bounds, 6)))
        {
            context.FillRectangle(new SolidColorBrush(new HsvColor(1, Hue, 1, 1).ToRgb()), bounds);
            context.FillRectangle(WhiteFade, bounds);
            context.FillRectangle(BlackFade, bounds);
        }

        var marker = new Point(Saturation * bounds.Width, (1 - Value) * bounds.Height);
        var fill = new SolidColorBrush(new HsvColor(1, Hue, Saturation, Value).ToRgb());
        context.DrawEllipse(fill, new Pen(Brushes.White, 2.5), marker, 7, 7);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), marker, 8.5, 8.5);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Update(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
            Update(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Update(Point point)
    {
        Saturation = Math.Clamp(point.X / Math.Max(1, Bounds.Width), 0, 1);
        Value = 1 - Math.Clamp(point.Y / Math.Max(1, Bounds.Height), 0, 1);
    }
}
