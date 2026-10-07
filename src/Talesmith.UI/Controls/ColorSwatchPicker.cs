using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>Picks a color from a set of preset swatches or from a typed hex value.</summary>
[TemplatePart(SwatchesPartName, typeof(Panel))]
[TemplatePart(HexInputPartName, typeof(TextBox))]
public class ColorSwatchPicker : TemplatedControl
{
    private const string SwatchesPartName = "PART_Swatches";
    private const string HexInputPartName = "PART_HexInput";
    private const string PickerButtonPartName = "PART_PickerButton";

    /// <summary>A balanced set of swatches used when <see cref="Colors"/> is not set.</summary>
    public static IList<Color> DefaultColors { get; } = Array.AsReadOnly(new[]
    {
        Color.Parse("#6366F1"), Color.Parse("#8B5CF6"), Color.Parse("#EC4899"), Color.Parse("#EF4444"), Color.Parse("#F97316"),
        Color.Parse("#EAB308"), Color.Parse("#22C55E"), Color.Parse("#14B8A6"), Color.Parse("#0EA5E9"), Color.Parse("#64748B")
    });

    public static readonly StyledProperty<IList<Color>?> ColorsProperty =
        AvaloniaProperty.Register<ColorSwatchPicker, IList<Color>?>(nameof(Colors), DefaultColors);

    public static readonly StyledProperty<Color> SelectedColorProperty =
        AvaloniaProperty.Register<ColorSwatchPicker, Color>(nameof(SelectedColor), defaultBindingMode: BindingMode.TwoWay);

    private Panel? _swatches;
    private TextBox? _hexInput;

    /// <summary>Gets or sets the preset colors offered as swatches.</summary>
    public IList<Color>? Colors
    {
        get => GetValue(ColorsProperty);
        set => SetValue(ColorsProperty, value);
    }

    /// <summary>Gets or sets the chosen color.</summary>
    public Color SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_hexInput is not null)
        {
            _hexInput.LostFocus -= OnHexInputLostFocus;
            _hexInput.KeyDown -= OnHexInputKeyDown;
        }

        _swatches = e.NameScope.Find<Panel>(SwatchesPartName);
        _hexInput = e.NameScope.Find<TextBox>(HexInputPartName);
        if (e.NameScope.Find<Button>(PickerButtonPartName) is { } picker)
            picker.Flyout = CreateFlyout();

        if (_hexInput is not null)
        {
            InputFilter.SetMode(_hexInput, InputFilterMode.Hex);
            _hexInput.LostFocus += OnHexInputLostFocus;
            _hexInput.KeyDown += OnHexInputKeyDown;
        }

        RebuildSwatches();
        UpdateHexText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColorsProperty)
        {
            RebuildSwatches();
        }
        else if (change.Property == SelectedColorProperty)
        {
            UpdateSelection();
            UpdateHexText();
        }
    }

    private void RebuildSwatches()
    {
        if (_swatches is null)
        {
            return;
        }

        _swatches.Children.Clear();
        foreach (var color in Colors ?? [])
        {
            var button = new ToggleButton
            {
                Classes = { "swatch" },
                Tag = color,
                Content = new ColorSwatch { Color = color },
                IsChecked = color == SelectedColor,
            };
            ToolTip.SetTip(button, ToHex(color));
            button.Click += OnSwatchClick;
            _swatches.Children.Add(button);
        }

        var custom = new Button
        {
            Classes = { "swatch-button", "custom" },
            Content = new SymbolIcon { Data = Icons.Plus, Size = 14 },
            Flyout = CreateFlyout()
        };
        ToolTip.SetTip(custom, "Custom color…");
        _swatches.Children.Add(custom);
    }

    private Flyout CreateFlyout() => ColorFlyout.Create(() => SelectedColor, c => SelectedColor = c, () => true, () => null);

    private void UpdateSelection()
    {
        if (_swatches is null)
        {
            return;
        }

        foreach (var button in _swatches.Children.OfType<ToggleButton>())
        {
            button.IsChecked = button.Tag is Color color && color == SelectedColor;
        }
    }

    private void UpdateHexText()
    {
        _hexInput?.Text = ToHex(SelectedColor);
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: Color color })
        {
            SelectedColor = color;
            UpdateSelection();
        }
    }

    private void OnHexInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHexText();
            e.Handled = true;
        }
    }

    private void OnHexInputLostFocus(object? sender, RoutedEventArgs e) => CommitHexText();

    private void CommitHexText()
    {
        var text = _hexInput?.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && !text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (Color.TryParse(text, out var color))
        {
            SelectedColor = color;
        }

        UpdateHexText();
    }

    private static string ToHex(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
