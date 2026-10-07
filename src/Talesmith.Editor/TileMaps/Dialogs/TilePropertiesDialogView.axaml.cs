using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Talesmith.Editor.TileMaps.Dialogs;

public partial class TilePropertiesDialogView : UserControl
{
    public TilePropertiesDialogView() => InitializeComponent();
}

/// <summary>Converters for the tile map dialogs.</summary>
public static class DialogConverters
{
    public static IValueConverter IsZero { get; } = new FuncValueConverter<int, bool>(value => value == 0);

    public static IValueConverter IsOne { get; } = new FuncValueConverter<int, bool>(value => value == 1);
}
