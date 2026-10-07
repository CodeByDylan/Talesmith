using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Talesmith.Editor.CommandPalette;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        ResultList.AddHandler(DoubleTappedEvent, OnResultDoubleTapped);
    }

    private CommandPaletteViewModel? ViewModel => DataContext as CommandPaletteViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
        switch (e.Key)
        {
            case Key.Down:
                viewModel.MoveSelection(1);
                break;
            case Key.Up:
                viewModel.MoveSelection(-1);
                break;
            case Key.Enter:
                viewModel.ExecuteCommand.Execute(null);
                break;
            default:
                return;
        }

        if (viewModel.SelectedItem is { } selected)
            ResultList.ScrollIntoView(selected);
        e.Handled = true;
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e) => ViewModel?.ExecuteCommand.Execute(null);
}

/// <summary>Converters for the command palette's rows.</summary>
public static class PaletteConverters
{
    public static FuncValueConverter<bool, double> EnabledOpacity { get; } = new(enabled => enabled ? 1 : 0.45);
}
