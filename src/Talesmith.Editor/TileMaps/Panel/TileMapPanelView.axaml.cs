using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>The Tile Map panel's view: layers above the palette in tall docks, side by side in wide ones like the bottom dock.</summary>
/// <remarks>The body fills the panel, with the palette scrolling on its own; in a panel too short for the layers and some of the palette, the body
/// scrolls as a whole.</remarks>
public partial class TileMapPanelView : UserControl
{
    private const double WideMinimum = 640;
    private const double TilesetsMinimum = 320;

    private bool? _wide;
    private LayerItemViewModel? _dragging;
    private Point _dragStart;
    private Control? _dropTarget;

    public TileMapPanelView()
    {
        InitializeComponent();
        Palette.TilesPicked += (_, e) => (DataContext as TileMapPanelViewModel)?.Tilesets.Pick(e);
        Palette.TileActivated += (_, id) => _ = (DataContext as TileMapPanelViewModel)?.Tilesets.EditTileAsync(id);
        OpacitySlider.AddHandler(Thumb.DragStartedEvent, (_, _) => Selected?.BeginOpacityEdit(), RoutingStrategies.Bubble);
        OpacitySlider.AddHandler(Thumb.DragCompletedEvent, (_, _) => Selected?.EndOpacityEdit(), RoutingStrategies.Bubble);
        LayerList.KeyDown += OnLayerListKeyDown;
        BodyScroll.SizeChanged += (_, _) => FitBody();
        LayersPart.SizeChanged += (_, _) => FitBody();
        ApplyLayout(false);
    }

    private LayerItemViewModel? Selected => (DataContext as TileMapPanelViewModel)?.Layers.SelectedItem;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var size = e.NewSize;
        ApplyLayout(size.Width >= WideMinimum && size.Width > size.Height * 1.5);
    }

    private void ApplyLayout(bool wide)
    {
        if (_wide == wide)
            return;
        _wide = wide;
        Body.RowDefinitions.Clear();
        Body.ColumnDefinitions.Clear();
        if (wide)
        {
            Body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(280)));
            Body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(TilesetsPart, 0);
            Grid.SetColumn(TilesetsPart, 1);
            LayersPart.BorderThickness = new Thickness(0, 0, 1, 0);
        }
        else
        {
            Body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Body.RowDefinitions.Add(new RowDefinition(GridLength.Star) { MinHeight = TilesetsMinimum });
            Grid.SetColumn(TilesetsPart, 0);
            Grid.SetRow(TilesetsPart, 1);
            LayersPart.BorderThickness = new Thickness(0, 0, 0, 1);
        }

        LayersPart.BorderBrush = this.TryFindResource("BorderSubtleBrush", ActualThemeVariant, out var brush) ? brush as global::Avalonia.Media.IBrush : null;
        FitBody();
    }

    /// <summary>Makes the body as tall as the panel, or as tall as the layers and the least of the palette when the panel is shorter.</summary>
    private void FitBody()
    {
        var room = BodyScroll.Bounds.Height;
        if (room <= 0)
            return;
        var minimum = _wide == true ? TilesetsMinimum : LayersPart.DesiredSize.Height + TilesetsMinimum;
        Body.Height = Math.Max(room, minimum);
    }

    private void OnLayerListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || e.KeyModifiers != KeyModifiers.None || Selected is not { } item)
            return;
        item.IsRenaming = true;
        e.Handled = true;
    }

    private void OnLayerDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: LayerItemViewModel item } && e.Source is not Button)
            item.IsRenaming = true;
    }

    private void OnRenameBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty || sender is not TextBox { IsVisible: true } box || box.DataContext is not LayerItemViewModel item)
            return;
        box.Text = item.Name;
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        });
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: LayerItemViewModel item } box)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                item.Name = box.Text ?? "";
                item.IsRenaming = false;
                e.Handled = true;
                break;
            case Key.Escape:
                item.IsRenaming = false;
                e.Handled = true;
                break;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: LayerItemViewModel { IsRenaming: true } item } box)
            return;
        item.Name = box.Text ?? "";
        item.IsRenaming = false;
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LayerItemViewModel item } grip || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
            return;
        _dragging = item;
        _dragStart = e.GetPosition(LayerList);
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null)
            return;
        var position = e.GetPosition(LayerList);
        if (Math.Abs(position.Y - _dragStart.Y) < 4 && _dropTarget is null)
            return;
        var (container, dropPosition) = DropAt(position);
        if (_dropTarget is not null && !ReferenceEquals(_dropTarget, container))
            DropIndicator.Hide(_dropTarget);
        _dropTarget = container;
        if (container is not null)
            DropIndicator.Show(container, dropPosition);
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is not { } item)
            return;
        e.Pointer.Capture(null);
        if (_dropTarget is not null)
        {
            DropIndicator.Hide(_dropTarget);
            var (container, position) = DropAt(e.GetPosition(LayerList));
            if (container is { DataContext: LayerItemViewModel target } && DataContext is TileMapPanelViewModel viewModel)
            {
                var items = viewModel.Layers.Items;
                var from = items.IndexOf(item);
                var row = items.IndexOf(target) + (position == DropPosition.After ? 1 : 0);
                if (row > from)
                    row--;
                viewModel.Layers.MoveToRow(item, row);
            }
        }

        _dragging = null;
        _dropTarget = null;
        e.Handled = true;
    }

    private (Control? Container, DropPosition Position) DropAt(Point position)
    {
        foreach (var container in LayerList.GetRealizedContainers())
        {
            var bounds = container.Bounds;
            var top = container.TranslatePoint(default, LayerList)?.Y ?? bounds.Y;
            if (position.Y >= top && position.Y < top + bounds.Height)
                return (container, DropIndicator.PositionFromOffset(position.Y - top, bounds.Height, canDropInside: false));
        }

        return (null, DropPosition.After);
    }

    private void OnTerrainDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TileMapPanelViewModel viewModel && viewModel.Tilesets.EditTerrainCommand.CanExecute(null))
            viewModel.Tilesets.EditTerrainCommand.Execute(null);
    }
}
