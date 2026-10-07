using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A full color editor: saturation/value spectrum, hue and alpha sliders, hex and RGBA fields.</summary>
[TemplatePart(SpectrumPart, typeof(ColorSpectrum))]
[TemplatePart(HuePart, typeof(ColorChannelSlider))]
[TemplatePart(AlphaPart, typeof(ColorChannelSlider))]
[TemplatePart(HexPart, typeof(TextBox))]
public class ColorPicker : TemplatedControl
{
    private const string SpectrumPart = "PART_Spectrum";
    private const string HuePart = "PART_Hue";
    private const string AlphaPart = "PART_Alpha";
    private const string HexPart = "PART_Hex";
    private const string RedPart = "PART_Red";
    private const string GreenPart = "PART_Green";
    private const string BluePart = "PART_Blue";
    private const string AlphaValuePart = "PART_AlphaValue";
    private const string OriginalPart = "PART_Original";

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorPicker, Color>(nameof(Color), Colors.White, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Color> OriginalColorProperty =
        AvaloniaProperty.Register<ColorPicker, Color>(nameof(OriginalColor), Colors.White);

    public static readonly StyledProperty<bool> IsAlphaEnabledProperty =
        AvaloniaProperty.Register<ColorPicker, bool>(nameof(IsAlphaEnabled), true);

    private ColorSpectrum? _spectrum;
    private ColorChannelSlider? _hue;
    private ColorChannelSlider? _alpha;
    private TextBox? _hex;
    private NumericUpDown? _red;
    private NumericUpDown? _green;
    private NumericUpDown? _blue;
    private NumericUpDown? _alphaValue;
    private Button? _original;
    private HsvColor _hsv = new(1, 0, 0, 1);
    private bool _syncing;

    /// <summary>The edited color.</summary>
    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The color before editing, shown for comparison and restored when clicked.</summary>
    public Color OriginalColor
    {
        get => GetValue(OriginalColorProperty);
        set => SetValue(OriginalColorProperty, value);
    }

    /// <summary>Whether transparency can be edited.</summary>
    public bool IsAlphaEnabled
    {
        get => GetValue(IsAlphaEnabledProperty);
        set => SetValue(IsAlphaEnabledProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        Detach();

        _spectrum = e.NameScope.Find<ColorSpectrum>(SpectrumPart);
        _hue = e.NameScope.Find<ColorChannelSlider>(HuePart);
        _alpha = e.NameScope.Find<ColorChannelSlider>(AlphaPart);
        _hex = e.NameScope.Find<TextBox>(HexPart);
        _red = e.NameScope.Find<NumericUpDown>(RedPart);
        _green = e.NameScope.Find<NumericUpDown>(GreenPart);
        _blue = e.NameScope.Find<NumericUpDown>(BluePart);
        _alphaValue = e.NameScope.Find<NumericUpDown>(AlphaValuePart);
        _original = e.NameScope.Find<Button>(OriginalPart);

        _spectrum?.PropertyChanged += OnPartChanged;
        _hue?.PropertyChanged += OnPartChanged;
        _alpha?.PropertyChanged += OnPartChanged;
        foreach (var field in Fields())
            field.ValueChanged += OnChannelChanged;
        if (_hex is not null)
        {
            InputFilter.SetMode(_hex, InputFilterMode.Hex);
            _hex.KeyDown += OnHexKeyDown;
            _hex.LostFocus += OnHexLostFocus;
        }

        _original?.Click += OnOriginalClick;

        _hsv = Color.ToHsv();
        SyncParts();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty && !_syncing)
        {
            var hsv = Color.ToHsv();
            _hsv = hsv.S <= 0 || hsv.V <= 0 ? new HsvColor(hsv.A, _hsv.H, hsv.S, hsv.V) : hsv;
            SyncParts();
        }
    }

    private IEnumerable<NumericUpDown> Fields() => new[] { _red, _green, _blue, _alphaValue }.OfType<NumericUpDown>();

    private void Detach()
    {
        _spectrum?.PropertyChanged -= OnPartChanged;
        _hue?.PropertyChanged -= OnPartChanged;
        _alpha?.PropertyChanged -= OnPartChanged;
        foreach (var field in Fields())
            field.ValueChanged -= OnChannelChanged;
        if (_hex is not null)
        {
            _hex.KeyDown -= OnHexKeyDown;
            _hex.LostFocus -= OnHexLostFocus;
        }

        _original?.Click -= OnOriginalClick;
    }

    private void OnPartChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_syncing)
            return;

        if (sender == _spectrum && (e.Property == ColorSpectrum.SaturationProperty || e.Property == ColorSpectrum.ValueProperty))
            Apply(new HsvColor(_hsv.A, _hsv.H, _spectrum!.Saturation, _spectrum.Value));
        else if (sender == _hue && e.Property == ColorChannelSlider.ValueProperty)
            Apply(new HsvColor(_hsv.A, Math.Clamp(_hue!.Value, 0, 359.999), _hsv.S, _hsv.V));
        else if (sender == _alpha && e.Property == ColorChannelSlider.ValueProperty)
            Apply(new HsvColor(_alpha!.Value, _hsv.H, _hsv.S, _hsv.V));
    }

    private void OnChannelChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_syncing || _red?.Value is not { } r || _green?.Value is not { } g || _blue?.Value is not { } b)
            return;
        var a = _alphaValue?.Value is { } alpha ? alpha : 255;
        SetFromRgb(Color.FromArgb(ToByte(a), ToByte(r), ToByte(g), ToByte(b)));
    }

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitHex();
        e.Handled = true;
    }

    private void OnHexLostFocus(object? sender, RoutedEventArgs e) => CommitHex();

    private void OnOriginalClick(object? sender, RoutedEventArgs e) => SetFromRgb(OriginalColor);

    private void CommitHex()
    {
        var text = _hex?.Text?.Trim() ?? string.Empty;
        if (!text.StartsWith('#'))
            text = "#" + text;
        if (Color.TryParse(text, out var color))
            SetFromRgb(IsAlphaEnabled || text.Length != 9 ? color : Color.FromArgb(255, color.R, color.G, color.B));
        else
            SyncParts();
    }

    private void SetFromRgb(Color color)
    {
        var hsv = color.ToHsv();
        Apply(hsv.S <= 0 || hsv.V <= 0 ? new HsvColor(hsv.A, _hsv.H, hsv.S, hsv.V) : hsv, color);
    }

    private void Apply(HsvColor hsv, Color? exact = null)
    {
        _hsv = IsAlphaEnabled ? hsv : new HsvColor(1, hsv.H, hsv.S, hsv.V);
        var color = exact ?? _hsv.ToRgb();
        if (!IsAlphaEnabled)
            color = Color.FromArgb(255, color.R, color.G, color.B);

        _syncing = true;
        Color = color;
        _syncing = false;
        SyncParts();
    }

    private void SyncParts()
    {
        _syncing = true;
        var color = Color;
        _spectrum?.Hue = _hsv.H;
        _spectrum?.Saturation = _hsv.S;
        _spectrum?.Value = _hsv.V;
        _hue?.Value = _hsv.H;
        _alpha?.Value = color.A / 255.0;
        _alpha?.Color = Color.FromRgb(color.R, color.G, color.B);
        _red?.Value = color.R;
        _green?.Value = color.G;
        _blue?.Value = color.B;
        _alphaValue?.Value = color.A;
        _hex?.Text = color.A == 255 || !IsAlphaEnabled
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        _syncing = false;
    }

    private static byte ToByte(decimal value) => (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
