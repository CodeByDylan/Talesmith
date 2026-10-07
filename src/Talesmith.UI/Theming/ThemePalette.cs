using Avalonia.Media;

namespace Talesmith.UI.Theming;

/// <summary>The set of semantic colors a theme variant is built from.</summary>
public sealed record ThemePalette
{
    /// <summary>The default light palette.</summary>
    public static ThemePalette Light { get; } = new()
    {
        Accent = Color.Parse("#5B5BEF"),
        AccentForeground = Color.Parse("#FFFFFF"),
        Background = Color.Parse("#F3F4F6"),
        Surface = Color.Parse("#FFFFFF"),
        SurfaceRaised = Color.Parse("#FFFFFF"),
        SurfaceSunken = Color.Parse("#F7F8FA"),
        SurfaceHover = Color.Parse("#0F1E2533"),
        SurfacePressed = Color.Parse("#1C1E2533"),
        BorderSubtle = Color.Parse("#E5E7EB"),
        BorderStrong = Color.Parse("#D2D6DC"),
        TextPrimary = Color.Parse("#14161B"),
        TextSecondary = Color.Parse("#4A5160"),
        TextMuted = Color.Parse("#858C99"),
        TextDisabled = Color.Parse("#B9BEC7"),
        Success = Color.Parse("#16A34A"),
        Warning = Color.Parse("#D97706"),
        Danger = Color.Parse("#DC2626"),
        Info = Color.Parse("#2563EB"),
        Scrim = Color.Parse("#590F1117"),
    };

    /// <summary>The default dark palette.</summary>
    public static ThemePalette Dark { get; } = new()
    {
        Accent = Color.Parse("#7073F6"),
        AccentForeground = Color.Parse("#FFFFFF"),
        Background = Color.Parse("#0E0F12"),
        Surface = Color.Parse("#16171B"),
        SurfaceRaised = Color.Parse("#1E2025"),
        SurfaceSunken = Color.Parse("#111215"),
        SurfaceHover = Color.Parse("#12FFFFFF"),
        SurfacePressed = Color.Parse("#1EFFFFFF"),
        BorderSubtle = Color.Parse("#25272D"),
        BorderStrong = Color.Parse("#33363E"),
        TextPrimary = Color.Parse("#ECEDF0"),
        TextSecondary = Color.Parse("#A7ABB5"),
        TextMuted = Color.Parse("#70757F"),
        TextDisabled = Color.Parse("#4A4E57"),
        Success = Color.Parse("#22C55E"),
        Warning = Color.Parse("#F59E0B"),
        Danger = Color.Parse("#EF4444"),
        Info = Color.Parse("#3B82F6"),
        Scrim = Color.Parse("#A6000000"),
    };

    public Color Accent { get; init; }
    public Color AccentForeground { get; init; }
    public Color Background { get; init; }
    public Color Surface { get; init; }
    public Color SurfaceRaised { get; init; }
    public Color SurfaceSunken { get; init; }
    public Color SurfaceHover { get; init; }
    public Color SurfacePressed { get; init; }
    public Color BorderSubtle { get; init; }
    public Color BorderStrong { get; init; }
    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextMuted { get; init; }
    public Color TextDisabled { get; init; }
    public Color Success { get; init; }
    public Color Warning { get; init; }
    public Color Danger { get; init; }
    public Color Info { get; init; }
    public Color Scrim { get; init; }

    /// <summary>Gets whether the palette describes a dark variant, based on the luminance of <see cref="Background"/>.</summary>
    public bool IsDark => ColorMath.Luminance(Background) < 0.5;

    /// <summary>Gets the accent color used for hovered accent surfaces.</summary>
    public Color AccentHover => ColorMath.Mix(Accent, IsDark ? Colors.White : Colors.Black, IsDark ? 0.12 : 0.08);

    /// <summary>Gets the accent color used for pressed accent surfaces.</summary>
    public Color AccentPressed => ColorMath.Mix(Accent, Colors.Black, IsDark ? 0.12 : 0.16);

    /// <summary>Gets a translucent accent color for selection fills.</summary>
    public Color AccentSubtle => ColorMath.WithAlpha(Accent, IsDark ? 0.2 : 0.12);

    /// <summary>Returns a copy of this palette using the given accent color.</summary>
    public ThemePalette WithAccent(Color accent) => this with { Accent = accent };
}
