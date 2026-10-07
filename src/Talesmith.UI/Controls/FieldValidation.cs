using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;

namespace Talesmith.UI.Controls;

/// <summary>Shows readable validation messages on input fields and restores the last valid value when an invalid one is left behind.</summary>
/// <remarks>
/// A cleared or unparsable field cannot be converted to its bound property. Instead of the conversion exception, the field explains
/// what it expects, such as "A value is required: enter a whole number from 0 to 256.", and when focus leaves the field it shows the
/// bound value again.
/// </remarks>
public static class FieldValidation
{
    private static bool _enabled;

    /// <summary>Applies readable messages and value restoring to every <see cref="NumericUpDown"/> and <see cref="TextBox"/>.</summary>
    public static void Enable()
    {
        if (_enabled)
            return;
        _enabled = true;

        Control.LoadedEvent.AddClassHandler<NumericUpDown>((field, _) => DataValidationErrors.SetErrorConverter(field, error => Describe(field, error)));
        Control.LoadedEvent.AddClassHandler<TextBox>((box, _) => DataValidationErrors.SetErrorConverter(box, error => Describe(box, error)));
        InputElement.LostFocusEvent.AddClassHandler<NumericUpDown>((field, _) => RestoreWhenInvalid(field, NumericUpDown.ValueProperty), handledEventsToo: true);
        InputElement.LostFocusEvent.AddClassHandler<TextBox>((box, _) => RestoreWhenInvalid(box, TextBox.TextProperty), handledEventsToo: true);
    }

    /// <summary>Describes a validation error on <paramref name="field"/> in words the reader can act on.</summary>
    public static object Describe(Control field, object error)
    {
        if (error is not Exception exception)
            return error;

        while (exception is TargetInvocationException or AggregateException && exception.InnerException is { } inner)
            exception = inner;

        return field switch
        {
            NumericUpDown numeric => Expect(numeric.Value is null, $"{(InputFilter.IsInteger(numeric) ? "a whole number" : "a number")}{Range(numeric.Minimum, numeric.Maximum)}"),
            TextBox box when ExpectedFor(box, exception) is { } expected => Expect(string.IsNullOrWhiteSpace(box.Text), expected),
            _ when IsConversionFailure(exception) => "This value isn't valid here.",
            _ => exception.Message
        };
    }

    private static string Expect(bool empty, string expected) =>
        empty ? $"A value is required: enter {expected}." : $"Enter {expected}.";

    private static string Range(decimal minimum, decimal maximum)
    {
        var hasMinimum = minimum > decimal.MinValue;
        var hasMaximum = maximum < decimal.MaxValue;
        return (hasMinimum, hasMaximum) switch
        {
            (true, true) => $" from {Format(minimum)} to {Format(maximum)}",
            (true, false) => $" of {Format(minimum)} or more",
            (false, true) => $" up to {Format(maximum)}",
            _ => string.Empty
        };
    }

    private static string Format(decimal value) => value.ToString("#,0.###", CultureInfo.CurrentCulture);

    private static string? ExpectedFor(TextBox box, Exception exception) => InputFilter.GetMode(box) switch
    {
        InputFilterMode.Integer => "a whole number",
        InputFilterMode.Decimal => "a number",
        InputFilterMode.Hex => "a color such as #3B82F6",
        _ when !IsConversionFailure(exception) => null,
        _ when exception.Message.Contains("System.Int", StringComparison.Ordinal) || exception.Message.Contains("System.UInt", StringComparison.Ordinal) => "a whole number",
        _ when exception.Message.Contains("System.Double", StringComparison.Ordinal) || exception.Message.Contains("System.Single", StringComparison.Ordinal)
            || exception.Message.Contains("System.Decimal", StringComparison.Ordinal) => "a number",
        _ => "a valid value"
    };

    private static bool IsConversionFailure(Exception exception) =>
        exception is InvalidCastException or FormatException or OverflowException;

    private static void RestoreWhenInvalid(Control field, Avalonia.AvaloniaProperty property) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!field.IsKeyboardFocusWithin && DataValidationErrors.GetHasErrors(field))
                BindingOperations.GetBindingExpressionBase(field, property)?.UpdateTarget();
        });
}
