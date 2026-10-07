using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Talesmith.Mathematics;

/// <summary>An 8-bit-per-channel color with straight (not premultiplied) alpha, laid out as R, G, B, A in memory.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static Color White => new(255, 255, 255);

    public static Color Black => new(0, 0, 0);

    public static Color Transparent => new(0, 0, 0, 0);

    /// <summary>Packs the color as 0xAARRGGBB.</summary>
    public uint Argb => (uint)(A << 24 | R << 16 | G << 8 | B);

    /// <summary>The color as floating-point components from 0 to 1, in R, G, B, A order.</summary>
    public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    public static Color FromVector4(Vector4 value)
    {
        var clamped = Vector4.Clamp(value, Vector4.Zero, Vector4.One) * 255f + new Vector4(0.5f);
        return new Color((byte)clamped.X, (byte)clamped.Y, (byte)clamped.Z, (byte)clamped.W);
    }

    public Color WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>Multiplies two colors channel by channel, as used for tinting.</summary>
    public static Color operator *(Color a, Color b) => FromVector4(a.ToVector4() * b.ToVector4());

    public static Color Lerp(Color from, Color to, float amount) => FromVector4(Vector4.Lerp(from.ToVector4(), to.ToVector4(), Math.Clamp(amount, 0, 1)));

    /// <summary>Parses "#RGB", "#RRGGBB" or "#AARRGGBB", with or without the leading "#".</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var hex = text.AsSpan().Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            Span<char> expanded = [hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]];
            return TryParse(new string(expanded), out color);
        }

        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        if (hex.Length == 6)
            value |= 0xFF000000;
        color = new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value, (byte)(value >> 24));
        return true;
    }

    public static Color Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a color. Use #RRGGBB or #AARRGGBB.");

    public override string ToString() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}
