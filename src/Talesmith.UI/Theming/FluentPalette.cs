using Avalonia.Media;
using Avalonia.Themes.Fluent;

namespace Talesmith.UI.Theming;

/// <summary>Maps a <see cref="ThemePalette"/> onto Fluent's system color palette.</summary>
internal static class FluentPalette
{
    public static ColorPaletteResources Create(ThemePalette palette)
    {
        var resources = new ColorPaletteResources();
        Apply(resources, palette);
        return resources;
    }

    public static void Apply(ColorPaletteResources target, ThemePalette p)
    {
        target.Accent = p.Accent;
        target.RegionColor = p.Background;
        target.ErrorText = p.Danger;

        target.BaseHigh = p.TextPrimary;
        target.BaseMediumHigh = p.TextSecondary;
        target.BaseMedium = p.TextMuted;
        target.BaseMediumLow = ColorMath.WithAlpha(p.TextPrimary, 0.4);
        target.BaseLow = p.BorderSubtle;

        target.AltHigh = p.Surface;
        target.AltMediumHigh = ColorMath.WithAlpha(p.Surface, 0.8);
        target.AltMedium = ColorMath.WithAlpha(p.Surface, 0.6);
        target.AltMediumLow = ColorMath.WithAlpha(p.Surface, 0.4);
        target.AltLow = ColorMath.WithAlpha(p.Surface, 0.2);

        target.ChromeAltLow = p.TextPrimary;
        target.ChromeBlackHigh = Colors.Black;
        target.ChromeBlackMedium = ColorMath.WithAlpha(Colors.Black, 0.6);
        target.ChromeBlackMediumLow = ColorMath.WithAlpha(Colors.Black, 0.4);
        target.ChromeBlackLow = ColorMath.WithAlpha(Colors.Black, 0.2);
        target.ChromeDisabledHigh = p.BorderSubtle;
        target.ChromeDisabledLow = p.TextDisabled;
        target.ChromeGray = p.TextMuted;
        target.ChromeHigh = p.BorderStrong;
        target.ChromeLow = p.Background;
        target.ChromeMedium = p.SurfaceSunken;
        target.ChromeMediumLow = p.SurfaceRaised;
        target.ChromeWhite = Colors.White;

        target.ListLow = p.SurfaceHover;
        target.ListMedium = p.SurfacePressed;
    }
}
