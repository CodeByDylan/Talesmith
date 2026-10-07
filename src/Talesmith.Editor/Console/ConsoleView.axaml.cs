using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Talesmith.Editor.Console;

public partial class ConsoleView : UserControl
{
    private ConsoleViewModel? _viewModel;

    public ConsoleView()
    {
        InitializeComponent();
        Log.DoubleTapped += OnDoubleTapped;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.RowsAppended -= OnRowsAppended;
        _viewModel = DataContext as ConsoleViewModel;
        if (_viewModel is not null)
            _viewModel.RowsAppended += OnRowsAppended;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel is not null && (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is ConsoleRow row)
            _viewModel.OpenCommand.Execute(row);
    }

    private void OnRowsAppended(object? sender, EventArgs e)
    {
        if (_viewModel is not { Rows.Count: > 0 } viewModel || Log.Scroll is not { } scroll || !Log.IsEffectivelyVisible || Log.Bounds.Height <= 0)
            return;
        var atBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 40;
        if (atBottom)
            Dispatcher.UIThread.Post(() => (Log.Scroll as ScrollViewer)?.ScrollToEnd(), DispatcherPriority.Background);
    }
}
