using Avalonia.Media;
using Avalonia.Styling;

namespace Talesmith.UI.Theming;

/// <summary>Controls the application's theme variant and accent color at runtime.</summary>
public interface IThemeManager
{
    /// <summary>Gets or sets the requested theme mode.</summary>
    ThemeMode Mode { get; set; }

    /// <summary>Gets or sets the accent color shared by the light and dark variants.</summary>
    Color Accent { get; set; }

    /// <summary>Gets the variant currently in effect after resolving <see cref="ThemeMode.System"/>.</summary>
    ThemeVariant ActualVariant { get; }

    /// <summary>Raised when the mode, accent or effective variant changes.</summary>
    event EventHandler? Changed;
}
