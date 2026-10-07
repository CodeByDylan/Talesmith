using Avalonia.Input;

namespace Talesmith.UI.Controls;

/// <summary>The arithmetic behind dragging and stepping numeric fields.</summary>
public static class NumberScrub
{
    /// <summary>The step multiplier while Shift is held.</summary>
    public const double FineFactor = 0.1;

    /// <summary>The step multiplier while Ctrl is held.</summary>
    public const double CoarseFactor = 10;

    /// <summary>Gets the step multiplier for the held modifiers: Shift is fine, Ctrl is coarse.</summary>
    public static double Factor(KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Shift))
            return FineFactor;
        return modifiers.HasFlag(KeyModifiers.Control) ? CoarseFactor : 1;
    }

    /// <summary>Gets the value after dragging <paramref name="pixels"/> from <paramref name="start"/>, one step per pixel, snapped to the step.</summary>
    public static double Drag(double start, double pixels, double step, double factor, double minimum, double maximum)
    {
        var increment = Math.Abs(step * factor);
        if (increment <= 0 || !double.IsFinite(increment))
            return Math.Clamp(start, minimum, maximum);

        var value = Math.Round((start + pixels * increment) / increment) * increment;
        return Math.Clamp(Tidy(value, increment), minimum, maximum);
    }

    /// <summary>Gets the value after stepping <paramref name="direction"/> steps from <paramref name="value"/>.</summary>
    public static double Step(double value, int direction, double step, double factor, double minimum, double maximum)
    {
        var increment = Math.Abs(step * factor);
        return Math.Clamp(Tidy(value + direction * increment, increment), minimum, maximum);
    }

    /// <summary>Removes floating-point noise below the precision of <paramref name="increment"/>, so 0.1 + 0.2 stays 0.3.</summary>
    public static double Tidy(double value, double increment)
    {
        if (increment <= 0 || !double.IsFinite(increment) || !double.IsFinite(value))
            return value;
        var decimals = (int)Math.Clamp(Math.Ceiling(-Math.Log10(increment)) + 1, 0, 15);
        return Math.Round(value, decimals);
    }
}
