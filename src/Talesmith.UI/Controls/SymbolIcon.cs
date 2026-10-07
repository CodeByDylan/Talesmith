using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>Draws a 24×24 line-icon geometry at an arbitrary size using the inherited foreground.</summary>
public class SymbolIcon : Control
{
    private const double ViewBoxSize = 24;

    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<SymbolIcon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<SymbolIcon, double>(nameof(Size), 16);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SymbolIcon, double>(nameof(StrokeThickness), 2);

    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<SymbolIcon, bool>(nameof(IsFilled));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SymbolIcon>();

    private IPen? _pen;

    static SymbolIcon()
    {
        AffectsMeasure<SymbolIcon>(SizeProperty);
        AffectsRender<SymbolIcon>(DataProperty, StrokeThicknessProperty, IsFilledProperty, ForegroundProperty);
    }

    /// <summary>Gets or sets the icon geometry, expressed on a 24×24 grid.</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Gets or sets the rendered width and height in device-independent pixels.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets or sets the stroke thickness in 24×24 grid units.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Gets or sets whether the geometry is filled instead of stroked.</summary>
    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    /// <summary>Gets or sets the icon brush.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ForegroundProperty || change.Property == StrokeThicknessProperty)
        {
            _pen = null;
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        var data = Data;
        var brush = Foreground;
        if (data is null || brush is null || Size <= 0)
        {
            return;
        }

        var scale = Size / ViewBoxSize;
        var offsetX = Math.Round((Bounds.Width - Size) / 2);
        var offsetY = Math.Round((Bounds.Height - Size) / 2);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            if (IsFilled)
            {
                context.DrawGeometry(brush, null, data);
            }
            else
            {
                _pen ??= new Pen(brush, StrokeThickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, _pen, data);
            }
        }
    }
}
