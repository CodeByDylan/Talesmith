using Avalonia.Controls;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>Creates the flyout that hosts a <see cref="ColorPicker"/> for color fields.</summary>
internal static class ColorFlyout
{
    /// <summary>Creates a flyout editing the color read from <paramref name="get"/> and written through <paramref name="set"/>.</summary>
    public static Flyout Create(Func<Color> get, Action<Color> set, Func<bool> alphaEnabled, Func<IList<Color>?> presets)
    {
        var picker = new ColorPicker();
        picker.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorPicker.ColorProperty)
                set(picker.Color);
        };

        var swatches = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        var content = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(4), Children = { picker } };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };

        flyout.Opening += (_, _) =>
        {
            picker.IsAlphaEnabled = alphaEnabled();
            picker.OriginalColor = get();
            picker.Color = get();

            swatches.Children.Clear();
            foreach (var color in presets() ?? [])
            {
                var button = new Button { Classes = { "swatch-button" }, Content = new ColorSwatch { Color = color } };
                ToolTip.SetTip(button, Hex(color));
                button.Click += (_, _) => picker.Color = color;
                swatches.Children.Add(button);
            }

            if (swatches.Children.Count > 0 && !content.Children.Contains(swatches))
                content.Children.Add(swatches);
        };

        return flyout;
    }

    public static string Hex(Color color) => color.A == 255
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
}
