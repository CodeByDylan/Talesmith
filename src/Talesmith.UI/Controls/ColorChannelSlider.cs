using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>The channel a <see cref="ColorChannelSlider"/> edits.</summary>
public enum ColorChannel
{
    Hue,
    Alpha
}

/// <summary>A horizontal gradient slider for the hue or alpha of a color.</summary>
public class ColorChannelSlider : Control
{
    public static readonly StyledProperty<ColorChannel> ChannelProperty =
        AvaloniaProperty.Register<ColorChannelSlider, ColorChannel>(nameof(Channel));

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ColorChannelSlider, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorChannelSlider, Color>(nameof(Color), Colors.White);

    private const double TrackHeight = 12;

    static ColorChannelSlider()
    {
        AffectsRender<ColorChannelSlider>(ChannelProperty, ValueProperty, ColorProperty);
        HeightProperty.OverrideDefaultValue<ColorChannelSlider>(18);
        CursorProperty.OverrideDefaultValue<ColorChannelSlider>(new Cursor(StandardCursorType.Hand));
    }

    public ColorChannel Channel
    {
        get => GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    /// <summary>Degrees from 0 to 360 for hue, 0 to 1 for alpha.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The opaque color whose alpha ramp is shown for <see cref="ColorChannel.Alpha"/>.</summary>
    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private double Maximum => Channel == ColorChannel.Hue ? 360 : 1;

    public override void Render(DrawingContext context)
    {
        var track = new Rect(0, (Bounds.Height - TrackHeight) / 2, Bounds.Width, TrackHeight);
        var rounded = new RoundedRect(track, TrackHeight / 2);
        using (context.PushClip(rounded))
        {
            if (Channel == ColorChannel.Alpha)
                DrawChecker(context, track);
            context.FillRectangle(TrackBrush(), track);
        }

        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 1), rounded);

        var x = Math.Clamp(Value / Maximum, 0, 1) * Bounds.Width;
        var thumbX = Math.Clamp(x, 8, Math.Max(8, Bounds.Width - 8));
        var thumb = Channel == ColorChannel.Hue ? new HsvColor(1, Value, 1, 1).ToRgb() : Color.FromArgb((byte)(Value * 255), Color.R, Color.G, Color.B);
        context.DrawEllipse(new SolidColorBrush(thumb), new Pen(Brushes.White, 2.5), new Point(thumbX, Bounds.Height / 2), 7.5, 7.5);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), 1), new Point(thumbX, Bounds.Height / 2), 9, 9);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Update(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (ReferenceEquals(e.Pointer.Captured, this))
            Update(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void Update(double x) => Value = Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1) * Maximum;

    private LinearGradientBrush TrackBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative)
        };

        if (Channel == ColorChannel.Hue)
        {
            for (var i = 0; i <= 6; i++)
                brush.GradientStops.Add(new GradientStop(new HsvColor(1, i * 60 % 360, 1, 1).ToRgb(), i / 6.0));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, Color.R, Color.G, Color.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, Color.R, Color.G, Color.B), 1));
        }

        return brush;
    }

    private static void DrawChecker(DrawingContext context, Rect area)
    {
        const double cell = 6;
        context.FillRectangle(Brushes.White, area);
        var dark = new SolidColorBrush(Color.FromRgb(204, 208, 214));
        for (var y = 0; y * cell < area.Height; y++)
        {
            for (var x = 0; x * cell < area.Width; x++)
            {
                if ((x + y) % 2 == 1)
                    context.FillRectangle(dark, new Rect(area.X + x * cell, area.Y + y * cell, cell, cell));
            }
        }
    }
}
