using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Talesmith.UI.Theming;

/// <summary>Applies theme mode and accent changes to an application that includes <see cref="ToolkitTheme"/>.</summary>
/// <remarks>It listens to the application's theme, so the application keeps it and its listeners alive until it is disposed.</remarks>
public sealed class ThemeManager : IThemeManager, IDisposable
{
    private readonly Application _application;
    private readonly ToolkitTheme _theme;
    private ThemeMode _mode;

    public ThemeManager(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        _application = application;
        _theme = application.Styles.OfType<ToolkitTheme>().FirstOrDefault()
            ?? throw new InvalidOperationException($"The application styles must include {nameof(ToolkitTheme)}.");
        _mode = FromVariant(application.RequestedThemeVariant);
        _application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    /// <inheritdoc />
    public ThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            _application.RequestedThemeVariant = ToVariant(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public Color Accent
    {
        get => _theme.LightPalette.Accent;
        set
        {
            if (Accent == value)
            {
                return;
            }

            _theme.SetAccent(value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public ThemeVariant ActualVariant => _application.ActualThemeVariant;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Stops following the application's theme.</summary>
    public void Dispose() => _application.ActualThemeVariantChanged -= OnActualThemeVariantChanged;

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private static ThemeMode FromVariant(ThemeVariant? variant)
    {
        if (variant == ThemeVariant.Light)
        {
            return ThemeMode.Light;
        }

        return variant == ThemeVariant.Dark ? ThemeMode.Dark : ThemeMode.System;
    }
}
