using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Talesmith.UI.Controls;

/// <summary>Displays a color as a rounded chip, with a checkerboard behind translucent colors.</summary>
public class ColorSwatch : Control
{
    private const double CheckerSize = 4;

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorSwatch, Color>(nameof(Color));

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        Border.CornerRadiusProperty.AddOwner<ColorSwatch>();

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        Border.BorderBrushProperty.AddOwner<ColorSwatch>();

    private static readonly IBrush CheckerLight = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush CheckerDark = new ImmutableSolidColorBrush(Color.FromRgb(0xD4, 0xD7, 0xDD));

    static ColorSwatch()
    {
        AffectsRender<ColorSwatch>(ColorProperty, CornerRadiusProperty, BorderBrushProperty);
    }

    /// <summary>Gets or sets the displayed color.</summary>
    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets or sets the corner radius of the chip.</summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>Gets or sets the brush of the 1px outline.</summary>
    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var shape = new RoundedRect(bounds, CornerRadius);

        using (context.PushClip(shape))
        {
            if (Color.A < 255)
            {
                context.FillRectangle(CheckerLight, bounds);
                for (var y = 0.0; y < bounds.Height; y += CheckerSize)
                {
                    var odd = (int)(y / CheckerSize) % 2 == 1;
                    for (var x = odd ? CheckerSize : 0; x < bounds.Width; x += CheckerSize * 2)
                    {
                        context.FillRectangle(CheckerDark, new Rect(x, y, CheckerSize, CheckerSize));
                    }
                }
            }

            context.FillRectangle(new ImmutableSolidColorBrush(Color), bounds);
        }

        if (BorderBrush is { } border)
        {
            context.DrawRectangle(null, new Pen(border), new RoundedRect(bounds.Deflate(0.5), CornerRadius));
        }
    }
}
