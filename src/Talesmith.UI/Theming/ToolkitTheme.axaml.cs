using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Talesmith.UI.Theming;

/// <summary>The toolkit's root style set: Fluent control templates re-skinned with the toolkit palettes, metrics and control themes.</summary>
public sealed class ToolkitTheme : Styles
{
    private readonly PaletteResources _light = new(ThemePalette.Light);
    private readonly PaletteResources _dark = new(ThemePalette.Dark);
    private readonly FluentTheme _fluent;

    public ToolkitTheme()
    {
        AvaloniaXamlLoader.Load(this);
        Controls.InputFilter.EnableForNumericFields();
        Controls.FieldValidation.Enable();

        _fluent = this.OfType<FluentTheme>().First();
        Resources.ThemeDictionaries[ThemeVariant.Light] = _light.Dictionary;
        Resources.ThemeDictionaries[ThemeVariant.Dark] = _dark.Dictionary;
        _fluent.Palettes[ThemeVariant.Light] = FluentPalette.Create(_light.Palette);
        _fluent.Palettes[ThemeVariant.Dark] = FluentPalette.Create(_dark.Palette);
    }

    /// <summary>Gets or sets the palette used by the light variant.</summary>
    public ThemePalette LightPalette
    {
        get => _light.Palette;
        set => SetPalette(ThemeVariant.Light, value);
    }

    /// <summary>Gets or sets the palette used by the dark variant.</summary>
    public ThemePalette DarkPalette
    {
        get => _dark.Palette;
        set => SetPalette(ThemeVariant.Dark, value);
    }

    /// <summary>Replaces the palette of <paramref name="variant"/> and refreshes every consumer in place.</summary>
    public void SetPalette(ThemeVariant variant, ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        var target = variant == ThemeVariant.Dark ? _dark : _light;
        target.Apply(palette);
        FluentPalette.Apply(_fluent.Palettes[variant == ThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light], palette);
    }

    /// <summary>Applies <paramref name="accent"/> to both the light and dark palettes.</summary>
    public void SetAccent(Color accent)
    {
        SetPalette(ThemeVariant.Light, _light.Palette.WithAccent(accent));
        SetPalette(ThemeVariant.Dark, _dark.Palette.WithAccent(accent));
    }
}
