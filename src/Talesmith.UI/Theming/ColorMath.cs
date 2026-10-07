using Avalonia.Media;

namespace Talesmith.UI.Theming;

/// <summary>Color blending helpers used to derive theme colors.</summary>
public static class ColorMath
{
    /// <summary>Linearly interpolates from <paramref name="from"/> towards <paramref name="to"/> by <paramref name="amount"/>.</summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            Lerp(from.A, to.A, amount),
            Lerp(from.R, to.R, amount),
            Lerp(from.G, to.G, amount),
            Lerp(from.B, to.B, amount));
    }

    /// <summary>Returns <paramref name="color"/> with its alpha channel set to <paramref name="opacity"/>.</summary>
    public static Color WithAlpha(Color color, double opacity) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);

    /// <summary>Returns the relative luminance of <paramref name="color"/> in the range 0 to 1.</summary>
    public static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static byte Lerp(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
