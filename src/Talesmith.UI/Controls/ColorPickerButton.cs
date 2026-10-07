using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A compact color field showing a swatch and hex value that opens a <see cref="ColorPicker"/>.</summary>
[TemplatePart(ButtonPart, typeof(Button))]
public class ColorPickerButton : TemplatedControl
{
    private const string ButtonPart = "PART_Button";

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorPickerButton, Color>(nameof(Color), Avalonia.Media.Colors.White, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IList<Color>?> ColorsProperty =
        AvaloniaProperty.Register<ColorPickerButton, IList<Color>?>(nameof(Colors), ColorSwatchPicker.DefaultColors);

    public static readonly StyledProperty<bool> IsAlphaEnabledProperty =
        AvaloniaProperty.Register<ColorPickerButton, bool>(nameof(IsAlphaEnabled), true);

    public static readonly DirectProperty<ColorPickerButton, string> HexProperty =
        AvaloniaProperty.RegisterDirect<ColorPickerButton, string>(nameof(Hex), b => b.Hex);

    private string _hex = "#FFFFFF";

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Preset swatches offered under the picker.</summary>
    public IList<Color>? Colors
    {
        get => GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    public bool IsAlphaEnabled
    {
        get => GetValue(IsAlphaEnabledProperty);
        set => SetValue(IsAlphaEnabledProperty, value);
    }

    /// <summary>The color formatted as <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public string Hex
    {
        get => _hex;
        private set => SetAndRaise(HexProperty, ref _hex, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Button>(ButtonPart) is { } button)
            button.Flyout = ColorFlyout.Create(() => Color, c => Color = c, () => IsAlphaEnabled, () => Colors);
        Hex = ColorFlyout.Hex(Color);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty)
            Hex = ColorFlyout.Hex(Color);
    }
}
