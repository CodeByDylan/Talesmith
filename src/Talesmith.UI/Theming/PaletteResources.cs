using Avalonia.Controls;
using Avalonia.Media;

namespace Talesmith.UI.Theming;

/// <summary>The theme-variant dictionary that exposes a <see cref="ThemePalette"/> as brushes, colors and shadows.</summary>
internal sealed class PaletteResources
{
    private static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);

    private static readonly (string Key, Func<ThemePalette, Color> Color)[] Tokens =
    [
        ("Accent", p => p.Accent),
        ("AccentHover", p => p.AccentHover),
        ("AccentPressed", p => p.AccentPressed),
        ("AccentSubtle", p => p.AccentSubtle),
        ("AccentForeground", p => p.AccentForeground),
        ("Background", p => p.Background),
        ("Surface", p => p.Surface),
        ("SurfaceRaised", p => p.SurfaceRaised),
        ("SurfaceSunken", p => p.SurfaceSunken),
        ("SurfaceHover", p => p.SurfaceHover),
        ("SurfacePressed", p => p.SurfacePressed),
        ("BorderSubtle", p => p.BorderSubtle),
        ("BorderStrong", p => p.BorderStrong),
        ("TextPrimary", p => p.TextPrimary),
        ("TextSecondary", p => p.TextSecondary),
        ("TextMuted", p => p.TextMuted),
        ("TextDisabled", p => p.TextDisabled),
        ("Success", p => p.Success),
        ("Warning", p => p.Warning),
        ("Danger", p => p.Danger),
        ("Info", p => p.Info),
        ("Scrim", p => p.Scrim),
    ];

    private static readonly (string Key, Func<ThemePalette, Color> Color)[] FluentBrushes =
    [
        ("TextControlForeground", p => p.TextPrimary),
        ("TextControlForegroundPointerOver", p => p.TextPrimary),
        ("TextControlForegroundFocused", p => p.TextPrimary),
        ("TextControlForegroundDisabled", p => p.TextDisabled),
        ("TextControlBackground", p => p.SurfaceSunken),
        ("TextControlBackgroundPointerOver", p => p.SurfaceSunken),
        ("TextControlBackgroundFocused", p => p.SurfaceSunken),
        ("TextControlBackgroundDisabled", p => p.SurfaceSunken),
        ("TextControlBorderBrush", p => p.BorderStrong),
        ("TextControlBorderBrushPointerOver", p => ColorMath.Mix(p.BorderStrong, p.TextMuted, 0.35)),
        ("TextControlBorderBrushFocused", p => p.Accent),
        ("TextControlBorderBrushDisabled", p => p.BorderSubtle),
        ("TextControlPlaceholderForeground", p => p.TextMuted),
        ("TextControlPlaceholderForegroundPointerOver", p => p.TextMuted),
        ("TextControlPlaceholderForegroundFocused", p => p.TextMuted),
        ("TextControlPlaceholderForegroundDisabled", p => p.TextDisabled),
        ("TextControlSelectionHighlightColor", p => ColorMath.WithAlpha(p.Accent, 0.35)),
        ("TextControlButtonForeground", p => p.TextMuted),
        ("TextControlButtonForegroundPointerOver", p => p.TextPrimary),
        ("TextControlButtonForegroundPressed", p => p.TextPrimary),
        ("TextControlButtonBackground", _ => Transparent),
        ("TextControlButtonBackgroundPointerOver", p => p.SurfaceHover),
        ("TextControlButtonBackgroundPressed", p => p.SurfacePressed),
        ("TextControlButtonBorderBrush", _ => Transparent),
        ("TextControlButtonBorderBrushPointerOver", _ => Transparent),
        ("TextControlButtonBorderBrushPressed", _ => Transparent),

        ("ComboBoxBackground", p => p.SurfaceSunken),
        ("ComboBoxBackgroundPointerOver", p => p.SurfaceSunken),
        ("ComboBoxBackgroundPressed", p => p.SurfaceSunken),
        ("ComboBoxBackgroundDisabled", p => p.SurfaceSunken),
        ("ComboBoxBackgroundUnfocused", p => p.SurfaceSunken),
        ("ComboBoxBackgroundBorderBrushFocused", p => p.Accent),
        ("ComboBoxBackgroundBorderBrushUnfocused", p => p.BorderStrong),
        ("ComboBoxBorderBrush", p => p.BorderStrong),
        ("ComboBoxBorderBrushPointerOver", p => ColorMath.Mix(p.BorderStrong, p.TextMuted, 0.35)),
        ("ComboBoxBorderBrushPressed", p => p.Accent),
        ("ComboBoxBorderBrushDisabled", p => p.BorderSubtle),
        ("ComboBoxForeground", p => p.TextPrimary),
        ("ComboBoxForegroundDisabled", p => p.TextDisabled),
        ("ComboBoxForegroundFocused", p => p.TextPrimary),
        ("ComboBoxForegroundFocusedPressed", p => p.TextPrimary),
        ("ComboBoxPlaceHolderForeground", p => p.TextMuted),
        ("ComboBoxPlaceHolderForegroundFocusedPressed", p => p.TextMuted),
        ("ComboBoxDropDownGlyphForeground", p => p.TextMuted),
        ("ComboBoxDropDownGlyphForegroundDisabled", p => p.TextDisabled),
        ("ComboBoxDropDownGlyphForegroundFocused", p => p.TextPrimary),
        ("ComboBoxDropDownGlyphForegroundFocusedPressed", p => p.TextPrimary),
        ("ComboBoxDropDownBackground", p => p.SurfaceRaised),
        ("ComboBoxDropDownBorderBrush", p => p.BorderStrong),
        ("ComboBoxItemForeground", p => p.TextPrimary),
        ("ComboBoxItemForegroundPointerOver", p => p.TextPrimary),
        ("ComboBoxItemForegroundPressed", p => p.TextPrimary),
        ("ComboBoxItemForegroundSelected", p => p.TextPrimary),
        ("ComboBoxItemForegroundSelectedPointerOver", p => p.TextPrimary),
        ("ComboBoxItemForegroundSelectedPressed", p => p.TextPrimary),
        ("ComboBoxItemForegroundDisabled", p => p.TextDisabled),
        ("ComboBoxItemForegroundSelectedDisabled", p => p.TextDisabled),
        ("ComboBoxItemBackground", _ => Transparent),
        ("ComboBoxItemBackgroundPointerOver", p => p.SurfaceHover),
        ("ComboBoxItemBackgroundPressed", p => p.SurfacePressed),
        ("ComboBoxItemBackgroundSelected", p => p.AccentSubtle),
        ("ComboBoxItemBackgroundSelectedPointerOver", p => p.AccentSubtle),
        ("ComboBoxItemBackgroundSelectedPressed", p => p.AccentSubtle),
        ("ComboBoxItemBackgroundDisabled", _ => Transparent),
        ("ComboBoxItemBackgroundSelectedDisabled", _ => Transparent),
        ("ComboBoxItemBorderBrushPointerOver", _ => Transparent),
        ("ComboBoxItemBorderBrushPressed", _ => Transparent),
        ("ComboBoxItemBorderBrushSelected", _ => Transparent),
        ("ComboBoxItemBorderBrushSelectedPointerOver", _ => Transparent),
        ("ComboBoxItemBorderBrushSelectedPressed", _ => Transparent),
        ("ComboBoxItemBorderBrushDisabled", _ => Transparent),
        ("ComboBoxItemBorderBrushSelectedDisabled", _ => Transparent),

        ("CheckBoxForegroundUnchecked", p => p.TextPrimary),
        ("CheckBoxForegroundUncheckedPointerOver", p => p.TextPrimary),
        ("CheckBoxForegroundUncheckedPressed", p => p.TextPrimary),
        ("CheckBoxForegroundUncheckedDisabled", p => p.TextDisabled),
        ("CheckBoxForegroundChecked", p => p.TextPrimary),
        ("CheckBoxForegroundCheckedPointerOver", p => p.TextPrimary),
        ("CheckBoxForegroundCheckedPressed", p => p.TextPrimary),
        ("CheckBoxForegroundCheckedDisabled", p => p.TextDisabled),
        ("CheckBoxForegroundIndeterminate", p => p.TextPrimary),
        ("CheckBoxForegroundIndeterminatePointerOver", p => p.TextPrimary),
        ("CheckBoxForegroundIndeterminatePressed", p => p.TextPrimary),
        ("CheckBoxForegroundIndeterminateDisabled", p => p.TextDisabled),
        ("CheckBoxBackgroundUnchecked", _ => Transparent),
        ("CheckBoxBackgroundUncheckedPointerOver", _ => Transparent),
        ("CheckBoxBackgroundUncheckedPressed", _ => Transparent),
        ("CheckBoxBackgroundUncheckedDisabled", _ => Transparent),
        ("CheckBoxBackgroundChecked", _ => Transparent),
        ("CheckBoxBackgroundCheckedPointerOver", _ => Transparent),
        ("CheckBoxBackgroundCheckedPressed", _ => Transparent),
        ("CheckBoxBackgroundCheckedDisabled", _ => Transparent),
        ("CheckBoxBackgroundIndeterminate", _ => Transparent),
        ("CheckBoxBackgroundIndeterminatePointerOver", _ => Transparent),
        ("CheckBoxBackgroundIndeterminatePressed", _ => Transparent),
        ("CheckBoxBackgroundIndeterminateDisabled", _ => Transparent),
        ("CheckBoxBorderBrushUnchecked", _ => Transparent),
        ("CheckBoxBorderBrushUncheckedPointerOver", _ => Transparent),
        ("CheckBoxBorderBrushUncheckedPressed", _ => Transparent),
        ("CheckBoxBorderBrushUncheckedDisabled", _ => Transparent),
        ("CheckBoxBorderBrushChecked", _ => Transparent),
        ("CheckBoxBorderBrushCheckedPointerOver", _ => Transparent),
        ("CheckBoxBorderBrushCheckedPressed", _ => Transparent),
        ("CheckBoxBorderBrushCheckedDisabled", _ => Transparent),
        ("CheckBoxBorderBrushIndeterminate", _ => Transparent),
        ("CheckBoxBorderBrushIndeterminatePointerOver", _ => Transparent),
        ("CheckBoxBorderBrushIndeterminatePressed", _ => Transparent),
        ("CheckBoxBorderBrushIndeterminateDisabled", _ => Transparent),
        ("CheckBoxCheckBackgroundStrokeUnchecked", p => p.BorderStrong),
        ("CheckBoxCheckBackgroundStrokeUncheckedPointerOver", p => p.TextMuted),
        ("CheckBoxCheckBackgroundStrokeUncheckedPressed", p => p.TextMuted),
        ("CheckBoxCheckBackgroundStrokeUncheckedDisabled", p => p.BorderSubtle),
        ("CheckBoxCheckBackgroundStrokeChecked", p => p.Accent),
        ("CheckBoxCheckBackgroundStrokeCheckedPointerOver", p => p.AccentHover),
        ("CheckBoxCheckBackgroundStrokeCheckedPressed", p => p.AccentPressed),
        ("CheckBoxCheckBackgroundStrokeCheckedDisabled", p => p.BorderSubtle),
        ("CheckBoxCheckBackgroundStrokeIndeterminate", p => p.Accent),
        ("CheckBoxCheckBackgroundStrokeIndeterminatePointerOver", p => p.AccentHover),
        ("CheckBoxCheckBackgroundStrokeIndeterminatePressed", p => p.AccentPressed),
        ("CheckBoxCheckBackgroundStrokeIndeterminateDisabled", p => p.BorderSubtle),
        ("CheckBoxCheckBackgroundFillUnchecked", p => p.SurfaceSunken),
        ("CheckBoxCheckBackgroundFillUncheckedPointerOver", p => p.SurfaceSunken),
        ("CheckBoxCheckBackgroundFillUncheckedPressed", p => p.SurfacePressed),
        ("CheckBoxCheckBackgroundFillUncheckedDisabled", p => p.SurfaceSunken),
        ("CheckBoxCheckBackgroundFillChecked", p => p.Accent),
        ("CheckBoxCheckBackgroundFillCheckedPointerOver", p => p.AccentHover),
        ("CheckBoxCheckBackgroundFillCheckedPressed", p => p.AccentPressed),
        ("CheckBoxCheckBackgroundFillCheckedDisabled", p => p.TextDisabled),
        ("CheckBoxCheckBackgroundFillIndeterminate", p => p.Accent),
        ("CheckBoxCheckBackgroundFillIndeterminatePointerOver", p => p.AccentHover),
        ("CheckBoxCheckBackgroundFillIndeterminatePressed", p => p.AccentPressed),
        ("CheckBoxCheckBackgroundFillIndeterminateDisabled", p => p.TextDisabled),
        ("CheckBoxCheckGlyphForegroundUnchecked", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundUncheckedPointerOver", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundUncheckedPressed", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundUncheckedDisabled", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundChecked", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundCheckedPointerOver", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundCheckedPressed", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundCheckedDisabled", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundIndeterminate", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundIndeterminatePointerOver", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundIndeterminatePressed", p => p.AccentForeground),
        ("CheckBoxCheckGlyphForegroundIndeterminateDisabled", p => p.AccentForeground),

        ("RadioButtonForeground", p => p.TextPrimary),
        ("RadioButtonForegroundPointerOver", p => p.TextPrimary),
        ("RadioButtonForegroundPressed", p => p.TextPrimary),
        ("RadioButtonForegroundDisabled", p => p.TextDisabled),
        ("RadioButtonBackground", _ => Transparent),
        ("RadioButtonBackgroundPointerOver", _ => Transparent),
        ("RadioButtonBackgroundPressed", _ => Transparent),
        ("RadioButtonBackgroundDisabled", _ => Transparent),
        ("RadioButtonBorderBrush", _ => Transparent),
        ("RadioButtonBorderBrushPointerOver", _ => Transparent),
        ("RadioButtonBorderBrushPressed", _ => Transparent),
        ("RadioButtonBorderBrushDisabled", _ => Transparent),
        ("RadioButtonOuterEllipseStroke", p => p.BorderStrong),
        ("RadioButtonOuterEllipseStrokePointerOver", p => p.TextMuted),
        ("RadioButtonOuterEllipseStrokePressed", p => p.TextMuted),
        ("RadioButtonOuterEllipseStrokeDisabled", p => p.BorderSubtle),
        ("RadioButtonOuterEllipseFill", p => p.SurfaceSunken),
        ("RadioButtonOuterEllipseFillPointerOver", p => p.SurfaceSunken),
        ("RadioButtonOuterEllipseFillPressed", p => p.SurfacePressed),
        ("RadioButtonOuterEllipseFillDisabled", p => p.SurfaceSunken),
        ("RadioButtonOuterEllipseCheckedStroke", p => p.Accent),
        ("RadioButtonOuterEllipseCheckedStrokePointerOver", p => p.AccentHover),
        ("RadioButtonOuterEllipseCheckedStrokePressed", p => p.AccentPressed),
        ("RadioButtonOuterEllipseCheckedStrokeDisabled", p => p.TextDisabled),
        ("RadioButtonOuterEllipseCheckedFill", p => p.Accent),
        ("RadioButtonOuterEllipseCheckedFillPointerOver", p => p.AccentHover),
        ("RadioButtonOuterEllipseCheckedFillPressed", p => p.AccentPressed),
        ("RadioButtonOuterEllipseCheckedFillDisabled", p => p.TextDisabled),
        ("RadioButtonCheckGlyphFill", p => p.AccentForeground),
        ("RadioButtonCheckGlyphFillPointerOver", p => p.AccentForeground),
        ("RadioButtonCheckGlyphFillPressed", p => p.AccentForeground),
        ("RadioButtonCheckGlyphFillDisabled", p => p.AccentForeground),
        ("RadioButtonCheckGlyphStroke", _ => Transparent),
        ("RadioButtonCheckGlyphStrokePointerOver", _ => Transparent),
        ("RadioButtonCheckGlyphStrokePressed", _ => Transparent),
        ("RadioButtonCheckGlyphStrokeDisabled", _ => Transparent),

        ("ToggleSwitchContentForeground", p => p.TextPrimary),
        ("ToggleSwitchHeaderForegroundDisabled", p => p.TextDisabled),
        ("ToggleSwitchContainerBackground", _ => Transparent),
        ("ToggleSwitchContainerBackgroundPointerOver", _ => Transparent),
        ("ToggleSwitchContainerBackgroundPressed", _ => Transparent),
        ("ToggleSwitchFillOff", p => p.BorderStrong),
        ("ToggleSwitchFillOffPointerOver", p => ColorMath.Mix(p.BorderStrong, p.TextMuted, 0.3)),
        ("ToggleSwitchFillOffPressed", p => ColorMath.Mix(p.BorderStrong, p.TextMuted, 0.45)),
        ("ToggleSwitchFillOffDisabled", p => p.BorderSubtle),
        ("ToggleSwitchStrokeOff", _ => Transparent),
        ("ToggleSwitchStrokeOffPointerOver", _ => Transparent),
        ("ToggleSwitchStrokeOffPressed", _ => Transparent),
        ("ToggleSwitchStrokeOffDisabled", _ => Transparent),
        ("ToggleSwitchFillOn", p => p.Accent),
        ("ToggleSwitchFillOnPointerOver", p => p.AccentHover),
        ("ToggleSwitchFillOnPressed", p => p.AccentPressed),
        ("ToggleSwitchFillOnDisabled", p => p.TextDisabled),
        ("ToggleSwitchStrokeOn", _ => Transparent),
        ("ToggleSwitchStrokeOnPointerOver", _ => Transparent),
        ("ToggleSwitchStrokeOnPressed", _ => Transparent),
        ("ToggleSwitchStrokeOnDisabled", _ => Transparent),
        ("ToggleSwitchKnobFillOff", _ => Colors.White),
        ("ToggleSwitchKnobFillOffPointerOver", _ => Colors.White),
        ("ToggleSwitchKnobFillOffPressed", _ => Colors.White),
        ("ToggleSwitchKnobFillOffDisabled", p => p.SurfaceSunken),
        ("ToggleSwitchKnobFillOn", _ => Colors.White),
        ("ToggleSwitchKnobFillOnPointerOver", _ => Colors.White),
        ("ToggleSwitchKnobFillOnPressed", _ => Colors.White),
        ("ToggleSwitchKnobFillOnDisabled", p => p.SurfaceSunken),

        ("SliderContainerBackground", _ => Transparent),
        ("SliderContainerBackgroundPointerOver", _ => Transparent),
        ("SliderContainerBackgroundPressed", _ => Transparent),
        ("SliderContainerBackgroundDisabled", _ => Transparent),
        ("SliderTrackFill", p => p.BorderStrong),
        ("SliderTrackFillPointerOver", p => p.BorderStrong),
        ("SliderTrackFillPressed", p => p.BorderStrong),
        ("SliderTrackFillDisabled", p => p.BorderSubtle),
        ("SliderTrackValueFill", p => p.Accent),
        ("SliderTrackValueFillPointerOver", p => p.AccentHover),
        ("SliderTrackValueFillPressed", p => p.AccentPressed),
        ("SliderTrackValueFillDisabled", p => p.TextDisabled),
        ("SliderThumbBackground", p => p.IsDark ? p.TextPrimary : p.Accent),
        ("SliderThumbBackgroundPointerOver", p => p.IsDark ? Colors.White : p.AccentHover),
        ("SliderThumbBackgroundPressed", p => p.IsDark ? p.TextSecondary : p.AccentPressed),
        ("SliderThumbBackgroundDisabled", p => p.TextDisabled),
        ("SliderTickBarFill", p => p.TextMuted),
        ("SliderTickBarFillDisabled", p => p.TextDisabled),
        ("SliderInlineTickBarFill", p => p.Surface),

        ("ScrollBarBackground", _ => Transparent),
        ("ScrollBarBackgroundPointerOver", _ => Transparent),
        ("ScrollBarForeground", p => p.TextMuted),
        ("ScrollBarBorderBrush", _ => Transparent),
        ("ScrollBarTrackFill", _ => Transparent),
        ("ScrollBarTrackFillPointerOver", p => ColorMath.WithAlpha(p.TextPrimary, 0.04)),
        ("ScrollBarTrackStroke", _ => Transparent),
        ("ScrollBarTrackStrokePointerOver", _ => Transparent),
        ("ScrollBarPanningThumbBackground", p => ColorMath.WithAlpha(p.TextMuted, 0.6)),
        ("ScrollBarThumbFillPointerOver", p => p.TextMuted),
        ("ScrollBarThumbFillPressed", p => p.TextSecondary),
        ("ScrollBarThumbFillDisabled", p => p.BorderSubtle),
        ("ScrollBarButtonArrowForeground", p => p.TextMuted),
        ("ScrollBarButtonArrowForegroundPointerOver", p => p.TextPrimary),
        ("ScrollBarButtonArrowForegroundPressed", p => p.TextPrimary),
        ("ScrollBarButtonArrowForegroundDisabled", p => p.TextDisabled),
        ("ScrollBarButtonBackground", _ => Transparent),
        ("ScrollBarButtonBackgroundPointerOver", _ => Transparent),
        ("ScrollBarButtonBackgroundPressed", _ => Transparent),
        ("ScrollBarButtonBackgroundDisabled", _ => Transparent),
        ("ScrollBarButtonBorderBrush", _ => Transparent),
        ("ScrollBarButtonBorderBrushPointerOver", _ => Transparent),
        ("ScrollBarButtonBorderBrushPressed", _ => Transparent),
        ("ScrollBarButtonBorderBrushDisabled", _ => Transparent),
        ("ScrollViewerScrollBarsSeparatorBackground", _ => Transparent),

        ("MenuFlyoutPresenterBackground", p => p.SurfaceRaised),
        ("MenuFlyoutPresenterBorderBrush", p => p.BorderStrong),
        ("MenuFlyoutItemBackground", _ => Transparent),
        ("MenuFlyoutItemBackgroundPointerOver", p => p.SurfaceHover),
        ("MenuFlyoutItemBackgroundPressed", p => p.SurfacePressed),
        ("MenuFlyoutItemBackgroundDisabled", _ => Transparent),
        ("MenuFlyoutItemForeground", p => p.TextPrimary),
        ("MenuFlyoutItemForegroundPointerOver", p => p.TextPrimary),
        ("MenuFlyoutItemForegroundPressed", p => p.TextPrimary),
        ("MenuFlyoutItemForegroundDisabled", p => p.TextDisabled),
        ("MenuFlyoutItemKeyboardAcceleratorTextForeground", p => p.TextMuted),
        ("MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver", p => p.TextSecondary),
        ("MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed", p => p.TextSecondary),
        ("MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled", p => p.TextDisabled),
        ("MenuFlyoutSubItemChevron", p => p.TextMuted),
        ("MenuFlyoutSubItemChevronPointerOver", p => p.TextPrimary),
        ("MenuFlyoutSubItemChevronPressed", p => p.TextPrimary),
        ("MenuFlyoutSubItemChevronSubMenuOpened", p => p.TextPrimary),
        ("MenuFlyoutSubItemChevronDisabled", p => p.TextDisabled),
        ("FlyoutPresenterBackground", p => p.SurfaceRaised),
        ("FlyoutBorderThemeBrush", p => p.BorderStrong),

        ("ExpanderHeaderBackground", p => p.Surface),
        ("ExpanderHeaderBackgroundPointerOver", p => ColorMath.Mix(p.Surface, p.TextPrimary, 0.04)),
        ("ExpanderHeaderBackgroundPressed", p => ColorMath.Mix(p.Surface, p.TextPrimary, 0.07)),
        ("ExpanderHeaderBackgroundDisabled", p => p.Surface),
        ("ExpanderHeaderForeground", p => p.TextPrimary),
        ("ExpanderHeaderForegroundPointerOver", p => p.TextPrimary),
        ("ExpanderHeaderForegroundPressed", p => p.TextPrimary),
        ("ExpanderHeaderForegroundDisabled", p => p.TextDisabled),
        ("ExpanderHeaderBorderBrush", p => p.BorderSubtle),
        ("ExpanderHeaderBorderBrushPointerOver", p => p.BorderSubtle),
        ("ExpanderHeaderBorderBrushPressed", p => p.BorderSubtle),
        ("ExpanderHeaderBorderBrushDisabled", p => p.BorderSubtle),
        ("ExpanderChevronBackground", _ => Transparent),
        ("ExpanderChevronBackgroundPointerOver", _ => Transparent),
        ("ExpanderChevronBackgroundPressed", _ => Transparent),
        ("ExpanderChevronBackgroundDisabled", _ => Transparent),
        ("ExpanderChevronBorderBrush", _ => Transparent),
        ("ExpanderChevronBorderBrushPointerOver", _ => Transparent),
        ("ExpanderChevronBorderBrushPressed", _ => Transparent),
        ("ExpanderChevronBorderBrushDisabled", _ => Transparent),
        ("ExpanderChevronForeground", p => p.TextMuted),
        ("ExpanderChevronForegroundPointerOver", p => p.TextPrimary),
        ("ExpanderChevronForegroundPressed", p => p.TextPrimary),
        ("ExpanderChevronForegroundDisabled", p => p.TextDisabled),
        ("ExpanderContentBackground", p => p.SurfaceSunken),
        ("ExpanderContentBorderBrush", p => p.BorderSubtle),

        ("ToolTipBackground", p => p.IsDark ? ColorMath.Mix(p.SurfaceRaised, Colors.White, 0.06) : ColorMath.Mix(p.TextPrimary, Colors.Black, 0.1)),
        ("ToolTipForeground", p => p.IsDark ? p.TextPrimary : Colors.White),
        ("ToolTipBorderBrush", p => p.IsDark ? p.BorderStrong : ColorMath.Mix(p.TextPrimary, Colors.Black, 0.1)),

        ("ButtonSpinnerForeground", p => p.TextMuted),
        ("SystemControlErrorTextForegroundBrush", p => p.Danger),
        ("SystemControlFocusVisualPrimaryBrush", p => p.Accent),
        ("SystemControlFocusVisualSecondaryBrush", _ => Transparent),
        ("SystemControlHighlightAccentBrush", p => p.Accent),
        ("SystemControlHighlightAltAccentBrush", p => p.Accent),
        ("SystemControlForegroundAccentBrush", p => p.Accent),
        ("SystemControlBackgroundAccentBrush", p => p.Accent),
    ];

    private static readonly (string Key, Func<ThemePalette, Color> Color)[] FluentColors =
    [
        ("ScrollBarThumbBackgroundColor", p => ColorMath.WithAlpha(p.TextMuted, 0.55)),
        ("ScrollBarPanningThumbBackgroundColor", p => ColorMath.WithAlpha(p.TextMuted, 0.45)),
    ];

    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);

    public PaletteResources(ThemePalette palette)
    {
        Palette = palette;
        Apply(palette);
    }

    /// <summary>Gets the dictionary to register as a theme dictionary.</summary>
    public ResourceDictionary Dictionary { get; } = new();

    /// <summary>Gets the palette currently applied.</summary>
    public ThemePalette Palette { get; private set; }

    /// <summary>Applies <paramref name="palette"/>, updating existing brushes in place so dynamic references refresh.</summary>
    public void Apply(ThemePalette palette)
    {
        Palette = palette;

        foreach (var (key, color) in Tokens)
        {
            SetBrush(key + "Brush", color(palette));
            Dictionary[key + "Color"] = color(palette);
        }

        foreach (var (key, color) in FluentBrushes)
        {
            SetBrush(key, color(palette));
        }

        foreach (var (key, color) in FluentColors)
        {
            Dictionary[key] = color(palette);
        }

        var dark = palette.IsDark;
        Dictionary[ThemeKeys.ShadowRaised] = BoxShadows.Parse(dark
            ? "0 1 2 0 #59000000"
            : "0 1 2 0 #0F101828, 0 1 3 0 #0A101828");
        Dictionary[ThemeKeys.ShadowPopup] = BoxShadows.Parse(dark
            ? "0 12 32 0 #8C000000, 0 2 6 0 #66000000"
            : "0 12 32 0 #26101828, 0 2 6 0 #14101828");
    }

    private void SetBrush(string key, Color color)
    {
        if (_brushes.TryGetValue(key, out var brush))
        {
            brush.Color = color;
            return;
        }

        brush = new SolidColorBrush(color);
        _brushes[key] = brush;
        Dictionary[key] = brush;
    }
}
