using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Talesmith.UI.Controls;

/// <summary>Which characters a text field accepts while typing.</summary>
public enum InputFilterMode
{
    None,
    Integer,
    Decimal,
    Hex
}

/// <summary>Restricts typed characters in text boxes and numeric fields to those valid for the expected value.</summary>
/// <remarks>
/// Set <c>ui:InputFilter.Mode</c> on a <see cref="TextBox"/>. Every <see cref="NumericUpDown"/> is filtered automatically once
/// <see cref="EnableForNumericFields"/> has run: integers when its <see cref="NumericUpDown.FormatString"/> has no decimals or its
/// parsing style excludes a decimal point, decimals otherwise.
/// </remarks>
public static class InputFilter
{
    public static readonly AttachedProperty<InputFilterMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<TextBox, InputFilterMode>("Mode", typeof(InputFilter));

    private static bool _numericFieldsEnabled;

    static InputFilter() =>
        ModeProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            box.RemoveHandler(InputElement.TextInputEvent, OnTextBoxInput);
            box.AddHandler(InputElement.TextInputEvent, OnTextBoxInput, RoutingStrategies.Tunnel);
        });

    public static InputFilterMode GetMode(TextBox box) => box.GetValue(ModeProperty);

    public static void SetMode(TextBox box, InputFilterMode value) => box.SetValue(ModeProperty, value);

    /// <summary>Filters typing in every <see cref="NumericUpDown"/> in the application.</summary>
    public static void EnableForNumericFields()
    {
        if (_numericFieldsEnabled)
            return;
        _numericFieldsEnabled = true;
        InputElement.TextInputEvent.AddClassHandler<NumericUpDown>(OnNumericInput, RoutingStrategies.Tunnel);
    }

    /// <summary>Whether <paramref name="text"/> may be typed into a field of the given mode.</summary>
    public static bool Accepts(InputFilterMode mode, string text, bool allowNegative = true)
    {
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        foreach (var c in text)
        {
            var valid = mode switch
            {
                InputFilterMode.Integer => char.IsAsciiDigit(c) || (allowNegative && c == '-'),
                InputFilterMode.Decimal => char.IsAsciiDigit(c) || (allowNegative && c == '-') || c == '.' || separator.Contains(c),
                InputFilterMode.Hex => char.IsAsciiHexDigit(c) || c == '#',
                _ => true
            };
            if (!valid)
                return false;
        }

        return true;
    }

    private static void OnTextBoxInput(object? sender, TextInputEventArgs e)
    {
        if (sender is TextBox box && e.Text is { } text && !Accepts(GetMode(box), text))
            e.Handled = true;
    }

    private static void OnNumericInput(NumericUpDown field, TextInputEventArgs e)
    {
        if (e.Text is not { } text)
            return;
        var mode = IsInteger(field) ? InputFilterMode.Integer : InputFilterMode.Decimal;
        if (!Accepts(mode, text, field.Minimum < 0))
            e.Handled = true;
    }

    internal static bool IsInteger(NumericUpDown field)
    {
        if (!field.ParsingNumberStyle.HasFlag(NumberStyles.AllowDecimalPoint))
            return true;
        var format = field.FormatString;
        return !string.IsNullOrEmpty(format) && !format.Contains('.') && (format.Contains('0') || format is "D" or "N0" or "F0");
    }
}
