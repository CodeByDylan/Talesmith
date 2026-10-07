using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Talesmith.Editor.DragAndDrop;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Hierarchy;

public partial class HierarchyView : UserControl
{
    private const double DragThreshold = 4;
    private const double ScrollZone = 28;
    private static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(550);

    private readonly DispatcherTimer _autoScroll;
    private PointerPressedEventArgs? _press;
    private HierarchyRow? _pressRow;
    private Point _pressPoint;
    private bool _deferredSelect;
    private Border? _dropTarget;
    private HierarchyRow? _hoverRow;
    private readonly Stopwatch _hoverClock = new();
    private double _dragY = double.NaN;

    public HierarchyView()
    {
        InitializeComponent();
        _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _autoScroll.Tick += (_, _) => AutoScroll();

        Host.AddHandler(PointerPressedEvent, OnRowPressed, RoutingStrategies.Tunnel);
        Host.AddHandler(PointerMovedEvent, OnHostMoved, RoutingStrategies.Tunnel);
        Host.AddHandler(PointerReleasedEvent, OnRowReleased, RoutingStrategies.Tunnel);
        Host.KeyDown += OnHostKeyDown;
        DragDrop.AddDragOverHandler(Host, OnDragOver);
        DragDrop.AddDragLeaveHandler(Host, OnDragLeave);
        DragDrop.AddDropHandler(Host, OnDrop);
        CreateButton.Click += (_, _) => OpenMenu(CreateButton, ViewModel?.BuildMenu(null).Where(i => !i.IsSeparator && i.Command is not null || i.Items.Count > 0));
    }

    private HierarchyViewModel? ViewModel => DataContext as HierarchyViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is not { } viewModel)
            return;
        viewModel.ScrollRequested += (_, index) => Dispatcher.UIThread.Post(() => ScrollTo(index), DispatcherPriority.Loaded);
        viewModel.RenameStarted += (_, row) => Dispatcher.UIThread.Post(() => FocusRename(row), DispatcherPriority.Loaded);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(HierarchyViewModel.IsEmpty) or nameof(HierarchyViewModel.HasScene) or nameof(HierarchyViewModel.Filter)
                or nameof(HierarchyViewModel.IsLive) or nameof(HierarchyViewModel.Rows))
                UpdateEmptyState();
        };
        UpdateEmptyState();
    }

    /// <summary>Focuses the filter box, as Ctrl+F does.</summary>
    public void FocusSearch() => Search.FocusInput();

    private void UpdateEmptyState()
    {
        if (ViewModel is not { } viewModel)
            return;
        Empty.IsVisible = viewModel.IsEmpty;
        if (!viewModel.HasScene)
        {
            Empty.Title = "No scene open";
            Empty.Hint = "Open a scene from File › Open or the scene picker.";
            Empty.ActionText = null;
        }
        else if (!string.IsNullOrWhiteSpace(viewModel.Filter))
        {
            Empty.Title = "No matches";
            Empty.Hint = $"No entity's name contains \"{viewModel.Filter.Trim()}\".";
            Empty.ActionText = null;
        }
        else if (viewModel.IsLive)
        {
            Empty.Title = "Loading the play world";
            Empty.Hint = "Entities appear once the scene has started.";
            Empty.ActionText = null;
        }
        else
        {
            Empty.Title = "Empty scene";
            Empty.Hint = "Create an entity, or drag sprites, prefabs and maps here from the assets.";
            Empty.ActionText = "Create entity";
            Empty.ActionCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => viewModel.Create(null, null));
        }
    }

    private void ScrollTo(int index)
    {
        if (index >= 0 && index < (ViewModel?.Rows.Count ?? 0))
            Tree.ScrollIntoView(index);
    }

    /// <summary>Puts a text box over the row's name to rename it.</summary>
    private void FocusRename(HierarchyRow row)
    {
        if (Tree.ContainerFromItem(row) is not { } container || container.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Classes.Contains("label")) is not { } label)
            return;
        if (label.Children.OfType<TextBox>().FirstOrDefault() is not { } box)
        {
            box = new TextBox
            {
                Classes = { "rename" },
                Padding = new Thickness(4, 0),
                MinHeight = 22,
                Height = 22,
                Margin = new Thickness(row.Depth * HierarchyRow.IndentStep + 38, 0, 48, 0),
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                VerticalContentAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
                DataContext = row
            };
            box.KeyDown += OnRenameKeyDown;
            box.LostFocus += OnRenameLostFocus;
            label.Children.Add(box);
        }

        box.DataContext = row;
        box.Text = row.Name;
        box.Focus();
        box.SelectAll();
    }

    private void EndRename(TextBox box)
    {
        box.KeyDown -= OnRenameKeyDown;
        box.LostFocus -= OnRenameLostFocus;
        (box.Parent as Panel)?.Children.Remove(box);
    }

    private static HierarchyRow? RowOf(object? source) => (source as Visual)?.FindAncestorOfType<Border>(includeSelf: true) is { } border
        ? RowBorder(border)?.DataContext as HierarchyRow
        : null;

    private static Border? RowBorder(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Border border && border.Classes.Contains("hrow"))
                return border;
        }

        return null;
    }

    private static bool IsInteractive(object? source) =>
        source is Visual visual && (visual.FindAncestorOfType<Button>(includeSelf: true) is not null || visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null);

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } viewModel || IsInteractive(e.Source))
            return;
        if (e.Source is HierarchyRowContent { DataContext: HierarchyRow target } content && e.GetCurrentPoint(content).Properties.IsLeftButtonPressed
            && RunZone(viewModel, target, content.ZoneAt(e.GetPosition(content))))
        {
            e.Handled = true;
            return;
        }

        Host.Focus(NavigationMethod.Pointer);
        var point = e.GetCurrentPoint(Host);
        var row = RowOf(e.Source);
        if (row is null)
        {
            if (point.Properties.IsLeftButtonPressed && e.KeyModifiers == KeyModifiers.None)
                viewModel.ClearSelection();
            return;
        }

        if (point.Properties.IsRightButtonPressed)
        {
            if (!row.IsSelected)
                viewModel.Click(row, KeyModifiers.None);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount == 2 && row.IsEditable)
        {
            viewModel.BeginRename(row);
            e.Handled = true;
            return;
        }

        _press = e;
        _pressRow = row;
        _pressPoint = point.Position;
        _deferredSelect = row.IsSelected && e.KeyModifiers == KeyModifiers.None;
        if (!_deferredSelect)
            viewModel.Click(row, e.KeyModifiers);
        e.Handled = true;
    }

    private static bool RunZone(HierarchyViewModel viewModel, HierarchyRow row, HierarchyRowZone zone)
    {
        switch (zone)
        {
            case HierarchyRowZone.Chevron:
                viewModel.SetExpanded(row, !row.IsExpanded);
                return true;
            case HierarchyRowZone.Visibility:
                viewModel.ToggleVisibilityCommand.Execute(row);
                return true;
            case HierarchyRowZone.Lock:
                viewModel.ToggleLockCommand.Execute(row);
                return true;
            default:
                return false;
        }
    }

    private async void OnHostMoved(object? sender, PointerEventArgs e)
    {
        if (_press is not { } press || _pressRow is not { } row || ViewModel is not { } viewModel)
            return;
        if (Point.Distance(e.GetPosition(Host), _pressPoint) < DragThreshold)
            return;
        _press = null;
        if (viewModel.IsLive || !row.IsEditable)
            return;
        if (_deferredSelect)
            _deferredSelect = false;
        var selected = viewModel.Rows.Where(r => r.IsSelected && r.IsEditable).Select(r => r.Id).ToList();
        if (!selected.Contains(row.Id))
            selected = [row.Id];
        var data = EditorDragData.ForEntities(selected);
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        }
        finally
        {
            EndDrag();
        }
    }

    private void OnRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
            return;
        if (e.InitialPressMouseButton == MouseButton.Right && !IsInteractive(e.Source))
        {
            var row = RowOf(e.Source);
            var anchor = row is null ? (Control)Host : RowBorder(e.Source as Visual) ?? (Control)Host;
            OpenMenu(anchor, viewModel.BuildMenu(row), pointer: true);
            e.Handled = true;
            return;
        }

        if (_deferredSelect && _pressRow is { } pressed)
            viewModel.Click(pressed, KeyModifiers.None);
        _deferredSelect = false;
        _press = null;
        _pressRow = null;
    }

    private static void OpenMenu(Control anchor, IEnumerable<Commands.MenuItemViewModel>? items, bool pointer = false)
    {
        var list = items?.ToList();
        if (list is not { Count: > 0 })
            return;
        var menu = new ContextMenu { ItemsSource = MenuBuilder.Build(list), Placement = pointer ? PlacementMode.Pointer : PlacementMode.BottomEdgeAlignedLeft };
        menu.Open(anchor);
    }

    private void OnHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel || e.Source is TextBox)
            return;
        var extend = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (e.Key)
        {
            case Key.Up when e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift:
                viewModel.MoveSelection(-1, extend);
                break;
            case Key.Down when e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift:
                viewModel.MoveSelection(1, extend);
                break;
            case Key.Home:
                viewModel.MoveSelection(-viewModel.Rows.Count, extend);
                break;
            case Key.End:
                viewModel.MoveSelection(viewModel.Rows.Count, extend);
                break;
            case Key.PageUp:
                viewModel.MoveSelection(-PageSize(), extend);
                break;
            case Key.PageDown:
                viewModel.MoveSelection(PageSize(), extend);
                break;
            case Key.Left:
                viewModel.CollapseOrParent();
                break;
            case Key.Right:
                viewModel.ExpandOrChild();
                break;
            case Key.Enter:
                viewModel.BeginRename();
                break;
            case Key.F when (e.KeyModifiers & KeyModifiers.Control) != 0:
                FocusSearch();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private int PageSize() => Math.Max(1, (int)(Scroller.Viewport.Height / 26) - 1);

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: HierarchyRow row } box || ViewModel is not { } viewModel)
            return;
        if (e.Key == Key.Enter)
        {
            viewModel.CommitRename(row, box.Text);
            EndRename(box);
            Host.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HierarchyViewModel.CancelRename(row);
            EndRename(box);
            Host.Focus();
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: HierarchyRow row } box)
            return;
        ViewModel?.CommitRename(row, box.Text);
        EndRename(box);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) => ViewModel?.Back();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        if (ViewModel is not { } viewModel || !viewModel.TryReadDrag(e.DataTransfer, out var data))
        {
            HideIndicator();
            return;
        }

        _dragY = e.GetPosition(Scroller).Y;
        if (!_autoScroll.IsEnabled)
            _autoScroll.Start();

        var border = RowBorder(e.Source as Visual);
        var row = border?.DataContext as HierarchyRow;
        var position = DropPosition.After;
        if (border is not null && row is not null)
        {
            position = DropIndicator.PositionAt(border, e.GetPosition(border), canDropInside: true);
            if (!viewModel.CanDrop(data, row, position) && position == DropPosition.Inside)
                position = e.GetPosition(border).Y < border.Bounds.Height / 2 ? DropPosition.Before : DropPosition.After;
            if (!viewModel.CanDrop(data, row, position))
            {
                HideIndicator();
                return;
            }

            ExpandOnHover(row, position);
            if (!ReferenceEquals(_dropTarget, border))
                HideIndicator();
            _dropTarget = border;
            DropIndicator.Show(border, position, row.Depth * HierarchyRow.IndentStep + 22);
        }
        else
        {
            HideIndicator();
            if (!viewModel.CanDrop(data, null, DropPosition.After))
                return;
        }

        e.DragEffects = data.HasEntities ? DragDropEffects.Move : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void ExpandOnHover(HierarchyRow row, DropPosition position)
    {
        if (!ReferenceEquals(row, _hoverRow) || position != DropPosition.Inside)
        {
            _hoverRow = position == DropPosition.Inside ? row : null;
            _hoverClock.Restart();
            return;
        }

        if (row is { HasChildren: true, IsExpanded: false } && _hoverClock.Elapsed > ExpandDelay)
            ViewModel?.SetExpanded(row, true);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (e.Source is Visual visual && Host.IsVisualAncestorOf(visual) && Host.Bounds.Contains(e.GetPosition(Host)))
            return;
        EndDrag();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel is not { } viewModel || !viewModel.TryReadDrag(e.DataTransfer, out var data))
        {
            EndDrag();
            return;
        }

        var border = RowBorder(e.Source as Visual);
        var row = border?.DataContext as HierarchyRow;
        var position = DropPosition.After;
        if (border is not null && row is not null)
        {
            position = DropIndicator.PositionAt(border, e.GetPosition(border), canDropInside: true);
            if (!viewModel.CanDrop(data, row, position) && position == DropPosition.Inside)
                position = e.GetPosition(border).Y < border.Bounds.Height / 2 ? DropPosition.Before : DropPosition.After;
        }

        EndDrag();
        if (viewModel.Drop(data, row, position))
        {
            e.DragEffects = data.HasEntities ? DragDropEffects.Move : DragDropEffects.Copy;
            Host.Focus();
        }

        e.Handled = true;
    }

    private void EndDrag()
    {
        HideIndicator();
        _autoScroll.Stop();
        _hoverRow = null;
        _dragY = double.NaN;
        _press = null;
        _pressRow = null;
        _deferredSelect = false;
    }

    private void HideIndicator()
    {
        if (_dropTarget is null)
            return;
        DropIndicator.Hide(_dropTarget);
        _dropTarget = null;
    }

    private void AutoScroll()
    {
        if (double.IsNaN(_dragY))
            return;
        var height = Scroller.Bounds.Height;
        var delta = _dragY < ScrollZone ? -(ScrollZone - _dragY) : _dragY > height - ScrollZone ? _dragY - (height - ScrollZone) : 0;
        if (delta == 0)
            return;
        Scroller.Offset = new Vector(Scroller.Offset.X, Math.Clamp(Scroller.Offset.Y + delta * 0.6, 0, Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height)));
    }
}
