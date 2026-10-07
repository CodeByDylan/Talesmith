using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Talesmith.Editor.TileMaps.Dialogs;

public partial class TilesetImportDialogView : UserControl
{
    public TilesetImportDialogView() => InitializeComponent();

    private async void OnPasteForgeSettings(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TilesetImportDialogViewModel viewModel || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        viewModel.ApplyForgeSettings(await clipboard.TryGetTextAsync());
    }
}
