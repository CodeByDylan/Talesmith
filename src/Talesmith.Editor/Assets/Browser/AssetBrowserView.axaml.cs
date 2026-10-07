using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Talesmith.Assets;
using Talesmith.Editor.Assets.Creation;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Assets.Browser;

/// <summary>The Assets panel's view: pointer and keyboard selection, inline renaming, menus, and dragging assets out, between folders and
/// files in from the system.</summary>
public partial class AssetBrowserView : UserControl
{
    private const double DragThreshold = 5;

    /// <summary>The panel's width below which the folder tree is left out, so that the contents keep room.</summary>
    private const double FoldersMinimumWidth = 420;

    private AssetBrowserViewModel? _viewModel;
    private PointerPressedEventArgs? _press;
    private Point _pressPoint;
    private AssetItemViewModel? _pressItem;
    private bool _selectOnRelease;
    private bool _dragging;
    private object? _dropHighlight;
    private GridLength _foldersWidth = new(200);

    public AssetBrowserView()
    {
        InitializeComponent();
        ContentArea.AddHandler(PointerPressedEvent, OnContentPressed, RoutingStrategies.Tunnel);
        ContentArea.AddHandler(PointerMovedEvent, OnContentMoved, RoutingStrategies.Tunnel);
        ContentArea.AddHandler(PointerReleasedEvent, OnContentReleased, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnRenameKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble);
        Folders.AddHandler(PointerPressedEvent, OnFoldersPressed, RoutingStrategies.Tunnel);
        GridScroll.SizeChanged += (_, _) => UpdateColumns();
        CreateButton.Click += (_, _) => OpenMenu(CreateMenu(), CreateButton);
        SortButton.Click += (_, _) => OpenMenu(SortMenu(), SortButton);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ScrollRequested -= OnScrollRequested;
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }

        _viewModel = DataContext as AssetBrowserViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollRequested += OnScrollRequested;
            _viewModel.PropertyChanged += OnViewModelChanged;
            UpdateColumns();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ShowFolders(e.NewSize.Width >= FoldersMinimumWidth);
    }

    /// <summary>Shows or hides the folder tree, keeping the width it was given; without it, the path and the up button lead through the folders.</summary>
    private void ShowFolders(bool show)
    {
        if (Folders.IsVisible == show)
            return;
        var column = Body.ColumnDefinitions[0];
        if (!show)
            _foldersWidth = column.Width;
        column.Width = show ? _foldersWidth : new GridLength(0);
        Folders.IsVisible = show;
        FolderSplitter.IsVisible = show;
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AssetBrowserViewModel.ThumbnailSize) or nameof(AssetBrowserViewModel.IsGrid))
            UpdateColumns();
    }

    private void UpdateColumns()
    {
        if (_viewModel is null)
            return;
        var width = GridScroll.Bounds.Width - GridScroll.Padding.Left - GridScroll.Padding.Right - 10;
        if (width > 0)
            _viewModel.Columns = (int)Math.Max(1, Math.Floor((width + 4) / (_viewModel.TileWidth + 4)));
    }

    private void OnScrollRequested(object? sender, AssetItemViewModel item)
    {
        if (_viewModel is null)
            return;
        var index = _viewModel.Items.IndexOf(item);
        if (index < 0)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel.IsGrid)
                GridItems.ScrollIntoView(index / Math.Max(1, _viewModel.Columns));
            else
                ListItems.ScrollIntoView(index);
            if (item.IsRenaming)
                Dispatcher.UIThread.Post(() => FocusRenameBox(item), DispatcherPriority.Background);
        }, DispatcherPriority.Loaded);
    }

    private void FocusRenameBox(AssetItemViewModel item)
    {
        var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Classes.Contains("rename") && t.DataContext == item && t.IsEffectivelyVisible);
        if (box is null)
            return;
        box.Focus();
        box.SelectAll();
    }

    // Pointer

    private static AssetItemViewModel? ItemAt(object? source) =>
        (source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("tile") || b.Classes.Contains("row"))?.DataContext as AssetItemViewModel;

    private void OnContentPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || (e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not null
            || (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is not null || (e.Source as Visual)?.FindAncestorOfType<global::Avalonia.Controls.Primitives.ScrollBar>() is not null)
            return;
        Focus();
        var point = e.GetCurrentPoint(this);
        var item = ItemAt(e.Source);
        var toggle = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var extend = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        if (point.Properties.IsRightButtonPressed)
        {
            if (item is not null && !item.IsSelected)
                _viewModel.Click(item, toggle: false, extend: false);
            else if (item is null)
                _viewModel.ClearSelection();
            OpenMenu(item is null ? BackgroundMenu() : ItemMenu(), e.Source as Control ?? this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (item is null)
        {
            if (!toggle && !extend)
                _viewModel.ClearSelection();
            return;
        }

        if (e.ClickCount == 2)
        {
            _press = null;
            _ = _viewModel.OpenItemAsync(item);
            e.Handled = true;
            return;
        }

        _press = e;
        _pressPoint = point.Position;
        _pressItem = item;
        _selectOnRelease = item.IsSelected && !toggle && !extend;
        if (!_selectOnRelease)
            _viewModel.Click(item, toggle, extend);
        e.Handled = true;
    }

    private async void OnContentMoved(object? sender, PointerEventArgs e)
    {
        if (_press is not { } press || _pressItem is null || _viewModel is null || _dragging)
            return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _pressPoint.X) < DragThreshold && Math.Abs(position.Y - _pressPoint.Y) < DragThreshold)
            return;
        _dragging = true;
        _selectOnRelease = false;
        _press = null;
        try
        {
            var guids = _viewModel.SelectedItems.Select(i => i.Guid).ToList();
            if (guids.Count == 0)
                guids.Add(_pressItem.Guid);
            await DragDrop.DoDragDropAsync(press, AssetDragData.Create(guids), DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            _dragging = false;
            ClearDropHighlight();
        }
    }

    private void OnContentReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_selectOnRelease && _pressItem is not null && _viewModel is not null)
            _viewModel.Click(_pressItem, toggle: false, extend: false);
        _selectOnRelease = false;
        _press = null;
        _pressItem = null;
    }

    private void OnFoldersPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || _viewModel is null)
            return;
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("node"))?.DataContext is FolderNodeViewModel { IsFavorites: false } node)
        {
            _viewModel.SelectedFolderNode = node;
            OpenMenu(BackgroundMenu(), e.Source as Control ?? Folders);
            e.Handled = true;
        }
    }

    // Keyboard

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_viewModel is null || e.Handled || e.Source is TextBox)
            return;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (e.Key)
        {
            case Key.Left when _viewModel.IsGrid:
                _viewModel.MoveSelection(-1, 0, shift);
                break;
            case Key.Right when _viewModel.IsGrid:
                _viewModel.MoveSelection(1, 0, shift);
                break;
            case Key.Up:
                _viewModel.MoveSelection(0, -1, shift);
                break;
            case Key.Down:
                _viewModel.MoveSelection(0, 1, shift);
                break;
            case Key.Enter:
                _ = _viewModel.OpenItemAsync(null);
                break;
            case Key.Back:
                _viewModel.GoUpCommand.Execute(null);
                break;
            case Key.F2:
                _viewModel.BeginRename(null);
                break;
            case Key.Delete:
                _ = _viewModel.DeleteAsync();
                break;
            case Key.D when ctrl:
                _ = _viewModel.DuplicateAsync();
                break;
            case Key.A when ctrl:
                _viewModel.SelectAll();
                break;
            case Key.F when ctrl:
                SearchField.Focus();
                break;
            case Key.Escape:
                _viewModel.ClearSelection();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: AssetItemViewModel item } box || !box.Classes.Contains("rename") || _viewModel is null)
            return;
        if (e.Key == Key.Enter)
        {
            _ = _viewModel.CommitRenameAsync(item);
            Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AssetBrowserViewModel.CancelRename(item);
            Focus();
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox { DataContext: AssetItemViewModel { IsRenaming: true } item } box && box.Classes.Contains("rename") && _viewModel is not null)
            _ = _viewModel.CommitRenameAsync(item);
    }

    // Drag and drop

    /// <summary>The folder under the pointer of a drag: a folder tile or row, a tree node, a breadcrumb, or the shown folder.</summary>
    private (string Folder, object? Highlight)? DropTarget(object? source)
    {
        if (_viewModel is null)
            return null;
        foreach (var visual in (source as Visual)?.GetSelfAndVisualAncestors() ?? [])
        {
            switch (visual)
            {
                case Border { DataContext: AssetItemViewModel { IsFolder: true } item } border when border.Classes.Contains("tile") || border.Classes.Contains("row"):
                    return (item.Path, item);
                case Border { DataContext: FolderNodeViewModel node } border when border.Classes.Contains("node"):
                    return node.IsFavorites ? null : (node.Path, node);
                case Border { Tag: string path } border when border.Classes.Contains("crumb-drop"):
                    return (path, border);
                case Panel panel when ReferenceEquals(panel, ContentArea):
                    return _viewModel.IsSearching || _viewModel.ShowingFavorites ? null : (_viewModel.CurrentFolder, null);
            }
        }

        return null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.None;
        var target = DropTarget(e.Source);
        var assets = AssetDragData.Read(e.DataTransfer);
        if (target is { } t && _viewModel is not null)
        {
            if (assets.Count > 0 && _viewModel.CanMoveInto(assets, t.Folder))
                e.DragEffects = DragDropEffects.Move;
            else if (assets.Count == 0 && e.DataTransfer.Contains(DataFormat.File))
            {
                e.DragEffects = DragDropEffects.Copy;
                DropText.Text = t.Folder.Length == 0 ? "Drop to import into assets" : $"Drop to import into {t.Folder}";
            }
        }

        SetDropHighlight(e.DragEffects == DragDropEffects.None ? null : target?.Highlight);
        DropZone.Classes.Set("active", e.DragEffects == DragDropEffects.Copy && target?.Highlight is null);
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ClearDropHighlight();
        DropZone.Classes.Set("active", false);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var target = DropTarget(e.Source);
        ClearDropHighlight();
        DropZone.Classes.Set("active", false);
        if (target is not { } t || _viewModel is null)
            return;
        e.Handled = true;
        var assets = AssetDragData.Read(e.DataTransfer);
        if (assets.Count > 0)
        {
            if (_viewModel.CanMoveInto(assets, t.Folder))
                await _viewModel.MoveAsync(assets, t.Folder);
            return;
        }

        var files = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (files is { Count: > 0 })
            await _viewModel.ImportFilesAsync(files, t.Folder);
    }

    private void SetDropHighlight(object? highlight)
    {
        if (ReferenceEquals(highlight, _dropHighlight))
            return;
        ClearDropHighlight();
        _dropHighlight = highlight;
        switch (highlight)
        {
            case AssetItemViewModel item:
                item.IsDropTarget = true;
                break;
            case FolderNodeViewModel node:
                node.IsDropTarget = true;
                break;
            case Border border:
                border.Classes.Set("drop", true);
                break;
        }
    }

    private void ClearDropHighlight()
    {
        switch (_dropHighlight)
        {
            case AssetItemViewModel item:
                item.IsDropTarget = false;
                break;
            case FolderNodeViewModel node:
                node.IsDropTarget = false;
                break;
            case Border border:
                border.Classes.Set("drop", false);
                break;
        }

        _dropHighlight = null;
    }

    // Menus

    private static void OpenMenu(ContextMenu menu, Control target)
    {
        menu.PlacementTarget = target;
        menu.Open(target);
    }

    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var item in CreateItems())
            menu.Items.Add(item);
        return menu;
    }

    private List<Control> CreateItems()
    {
        var items = new List<Control>();
        if (_viewModel is null)
            return items;
        items.Add(Item("Folder", Icons.FolderPlus, () => _ = _viewModel.CreateFolderAsync(), iconBrush: AssetKindStyle.Of(AssetKind.Folder).Brush));
        string? group = null;
        var submenus = new Dictionary<string, MenuItem>(StringComparer.Ordinal);
        foreach (var factory in _viewModel.Factories)
        {
            if (factory.Group != group)
            {
                items.Add(new Separator());
                group = factory.Group;
            }

            var entry = Item(factory.Title, factory.Icon(), () => _ = _viewModel.CreateAsync(factory), iconBrush: AssetKindStyle.Of(factory.Kind).Brush);
            if (factory.Submenu is { } name)
            {
                if (!submenus.TryGetValue(name, out var submenu))
                {
                    submenu = new MenuItem { Header = name, Icon = Icon(factory.Icon(), AssetKindStyle.Of(factory.Kind).Brush) };
                    submenus[name] = submenu;
                    items.Add(submenu);
                }

                submenu.Items.Add(entry);
            }
            else
            {
                items.Add(entry);
            }
        }

        return items;
    }

    private ContextMenu SortMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight };
        if (_viewModel is null)
            return menu;
        foreach (var key in Enum.GetValues<AssetSortKey>())
        {
            var selected = _viewModel.SortBy == key;
            menu.Items.Add(Item($"Sort by {key.ToString().ToLowerInvariant()}", selected ? (_viewModel.SortDescending ? Icons.ArrowDown : Icons.ArrowUp) : null,
                () => _viewModel.SetSortCommand.Execute(key)));
        }

        return menu;
    }

    private ContextMenu ItemMenu()
    {
        var menu = new ContextMenu();
        if (_viewModel is not { } vm)
            return menu;
        var selected = vm.SelectedItems;
        var single = selected.Count == 1 ? selected[0] : null;
        menu.Items.Add(Item(single?.IsFolder == true ? "Open folder" : "Open", Icons.FolderOpen, () => _ = vm.OpenItemAsync(single), "Enter", single is not null));
        menu.Items.Add(Item("Show in file manager", Icons.ArrowRight, () => vm.RevealCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Rename", Icons.PenLine, () => vm.BeginRename(single), "F2", single is not null));
        menu.Items.Add(Item("Duplicate", Icons.CopyPlus, () => _ = vm.DuplicateAsync(), "Ctrl+D"));
        menu.Items.Add(Item("Delete", Icons.Trash, () => _ = vm.DeleteAsync(), "Delete"));
        menu.Items.Add(new Separator());
        var favorite = selected.All(i => i.IsFavorite);
        menu.Items.Add(Item(favorite ? "Remove from favorites" : "Add to favorites", Icons.Star, vm.ToggleFavorite, iconBrush: new SolidColorBrush(Color.Parse("#F59E0B"))));
        menu.Items.Add(Item("Copy path", Icons.Copy, () => vm.CopyPathCommand.Execute(null)));
        menu.Items.Add(Item("Copy guid", Icons.Copy, () => vm.CopyGuidCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Reimport", Icons.Refresh, () => vm.ReimportCommand.Execute(null), enabled: selected.Any(i => !i.IsFolder)));
        var create = new MenuItem { Header = "Create", Icon = Icon(Icons.Plus) };
        foreach (var item in CreateItems())
            create.Items.Add(item);
        menu.Items.Add(new Separator());
        menu.Items.Add(create);
        return menu;
    }

    private ContextMenu BackgroundMenu()
    {
        var menu = new ContextMenu();
        if (_viewModel is not { } vm)
            return menu;
        var create = new MenuItem { Header = "Create", Icon = Icon(Icons.Plus) };
        foreach (var item in CreateItems())
            create.Items.Add(item);
        menu.Items.Add(create);
        menu.Items.Add(Item("Import files…", Icons.Import, () => vm.ImportCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Show in file manager", Icons.FolderOpen, () => vm.RevealCommand.Execute(null)));
        menu.Items.Add(Item("Rescan assets", Icons.Refresh, () => vm.RescanCommand.Execute(null)));
        menu.Items.Add(Item("Asset health…", Icons.Activity, () => vm.ShowHealthCommand.Execute(null)));
        return menu;
    }

    private static MenuItem Item(string header, Geometry? icon, Action action, string? gesture = null, bool enabled = true, IBrush? iconBrush = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, Icon = icon is null ? null : Icon(icon, iconBrush) };
        if (gesture is not null)
            item.InputGesture = KeyGesture.Parse(gesture);
        item.Click += (_, _) => action();
        return item;
    }

    private static SymbolIcon Icon(Geometry icon, IBrush? brush = null)
    {
        var symbol = new SymbolIcon { Data = icon, Size = 14 };
        if (brush is not null)
            symbol.Foreground = brush;
        return symbol;
    }
}
