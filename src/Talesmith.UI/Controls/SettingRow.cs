using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A settings card row showing an icon, title and description beside an editing control.</summary>
public class SettingRow : ContentControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SettingRow, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>Gets or sets the leading icon.</summary>
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Gets or sets the setting name.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the explanatory text under the title.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
