using System.Collections.ObjectModel;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Creation;
using Talesmith.Editor.Assets.Health;
using Talesmith.Editor.Assets.Opening;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Assets.Thumbnails;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Assets.Browser;

/// <summary>What the asset listing is sorted by; folders always come first.</summary>
public enum AssetSortKey
{
    Name,
    Kind,
    Size,
    Modified
}

/// <summary>The Assets panel: the folder tree, the breadcrumbs and the contents of a folder or of a search, with selection, renaming,
/// drag and drop and the operations of the context menu.</summary>
/// <remarks>Searching (see <see cref="AssetSearchQuery"/>) and kind filters look through the whole project; otherwise the panel shows one
/// folder. Changes of the asset database arrive on thread pool threads and are applied on the UI thread in one pass per batch.</remarks>
public sealed partial class AssetBrowserViewModel : ObservableObject, IDisposable
{
    private const string StateKey = "assets.browser";

    private readonly IProjectService _project;
    private readonly ISelectionService _selection;
    private readonly ThumbnailService _thumbnails;
    private readonly AssetOpener _opener;
    private readonly IFileDialogService _files;
    private readonly WindowHost _window;
    private readonly AssetHealthLauncher _health;
    private readonly ProjectState _state;
    private readonly Dictionary<AssetGuid, AssetItemViewModel> _cache = [];
    private readonly HashSet<AssetGuid> _favorites = [];
    private readonly HashSet<string> _expanded = new(AssetPath.Comparer);
    private AssetDatabase? _database;
    private bool _syncingSelection;
    private bool _quiet;
    private bool _refreshQueued;
    private bool _treeDirty = true;
    private int _anchor = -1;
    private int _focusIndex = -1;
    private int _columns = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoUp))]
    private string _currentFolder = "";

    [ObservableProperty]
    private bool _showingFavorites;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearch))]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList))]
    private bool _isGrid = true;

    [ObservableProperty]
    private double _thumbnailSize = 72;

    [ObservableProperty]
    private AssetSortKey _sortBy;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private double _scanFraction;

    [ObservableProperty]
    private string _scanText = "";

    [ObservableProperty]
    private FolderNodeViewModel? _selectedFolderNode;

    [ObservableProperty]
    private ObservableCollection<AssetItemViewModel> _items = [];

    [ObservableProperty]
    private ObservableCollection<AssetRowViewModel> _rows = [];

    [ObservableProperty]
    private string _statusText = "";

    public AssetBrowserViewModel(IProjectService project, ISelectionService selection, AssetOperations operations, ThumbnailService thumbnails,
        AssetOpener opener, IEnumerable<IAssetFactory> factories, ProjectState state, IDialogService dialogs, IFileDialogService files,
        WindowHost window, AssetHealthLauncher health, EditorPluginGuard plugins)
    {
        _project = project;
        _selection = selection;
        _thumbnails = thumbnails;
        _opener = opener;
        _files = files;
        _window = window;
        _health = health;
        _state = state;
        Operations = operations;
        Dialogs = dialogs;
        Factories = [.. factories.Select(f => PluginAssetFactory.Isolate(f, plugins)).OfType<IAssetFactory>().OrderBy(f => GroupOrder(f.Group)).ThenBy(f => f.Order)];
        LoadState();
        _selection.Changed += OnSelectionChanged;
        _project.StatusChanged += OnProjectStatusChanged;
        OnProjectStatusChanged(null, EventArgs.Empty);
    }

    public AssetOperations Operations { get; }

    public IDialogService Dialogs { get; }

    /// <summary>The entries of the Create menu, grouped and ordered.</summary>
    public IReadOnlyList<IAssetFactory> Factories { get; }

    public ObservableCollection<FolderNodeViewModel> FolderRoots { get; } = [];

    public ObservableCollection<BreadcrumbViewModel> Breadcrumbs { get; } = [];

    public ObservableCollection<KindFilterViewModel> KindFilters { get; } = [];

    public bool IsList => !IsGrid;

    public bool HasSearch => Search.Length > 0;

    /// <summary>Whether the panel shows search or filter results from the whole project rather than one folder.</summary>
    public bool IsSearching => HasSearch || KindFilters.Any(k => k.IsChecked);

    public bool HasKindFilter => KindFilters.Any(k => k.IsChecked);

    public bool CanGoUp => !ShowingFavorites && CurrentFolder.Length > 0;

    public bool IsReady => _database is { IsScanned: true };

    public bool IsEmpty => IsReady && Items.Count == 0;

    public string EmptyTitle => IsSearching ? "No matching assets" : ShowingFavorites ? "No favorites yet" : "This folder is empty";

    public string EmptyHint => IsSearching
        ? "Try another name, or search with kind:texture, tag:enemy or a guid."
        : ShowingFavorites
            ? "Star assets from their context menu to keep them here."
            : "Drop files here to import them, or use Create.";

    public IReadOnlyList<AssetItemViewModel> SelectedItems => [.. Items.Where(i => i.IsSelected)];

    public IEnumerable<AssetRecord> SelectedRecords => Items.Where(i => i.IsSelected).Select(i => i.Record);

    public bool HasSelection => Items.Any(i => i.IsSelected);

    /// <summary>Raised when an item should be brought into view, such as after keyboard navigation or revealing an asset.</summary>
    public event EventHandler<AssetItemViewModel>? ScrollRequested;

    /// <summary>The number of tiles per grid row; the view sets it from its width.</summary>
    public int Columns
    {
        get => _columns;
        set
        {
            value = Math.Max(1, value);
            if (value == _columns)
                return;
            _columns = value;
            OnPropertyChanged();
            RebuildRows();
        }
    }

    /// <summary>The width of a grid tile for the current thumbnail size.</summary>
    public double TileWidth => ThumbnailSize + 18;

    public bool IsFavorite(AssetGuid guid) => _favorites.Contains(guid);

    partial void OnSearchChanged(string value) => Refresh();

    partial void OnIsGridChanged(bool value) => SaveState();

    partial void OnThumbnailSizeChanged(double value)
    {
        OnPropertyChanged(nameof(TileWidth));
        SaveState();
    }

    partial void OnSortByChanged(AssetSortKey value)
    {
        Refresh();
        SaveState();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        Refresh();
        SaveState();
    }

    partial void OnSelectedFolderNodeChanged(FolderNodeViewModel? value)
    {
        if (value is null)
            return;
        if (value.IsFavorites)
        {
            if (!ShowingFavorites)
            {
                ShowingFavorites = true;
                Search = "";
                Refresh();
            }
        }
        else if (ShowingFavorites || !AssetPath.Comparer.Equals(CurrentFolder, value.Path))
        {
            OpenFolder(value.Path);
        }
    }

    partial void OnCurrentFolderChanged(string value)
    {
        UpdateBreadcrumbs();
        SaveState();
    }

    /// <summary>Shows a folder's contents, leaving search and favorites.</summary>
    [RelayCommand]
    public void OpenFolder(string? path)
    {
        path = AssetPath.Normalize(path ?? "");
        if (_database is { } database && path.Length > 0 && !(database.TryGetAsset(path, out var record) && record.IsFolder))
            path = "";
        ShowingFavorites = false;
        _anchor = -1;
        _quiet = true;
        Search = "";
        foreach (var filter in KindFilters)
            filter.IsChecked = false;
        _quiet = false;
        CurrentFolder = path;
        for (var folder = AssetPath.GetDirectory(path); folder.Length > 0; folder = AssetPath.GetDirectory(folder))
            Expand(folder);
        SelectedFolderNode = Root?.Find(path);
        RefreshNow();
    }

    [RelayCommand]
    private void GoUp()
    {
        if (CanGoUp)
            OpenFolder(AssetPath.GetDirectory(CurrentFolder));
    }

    /// <summary>Opens a folder, or opens an asset with its <see cref="IAssetOpenHandler"/>.</summary>
    [RelayCommand]
    public async Task OpenItemAsync(AssetItemViewModel? item)
    {
        item ??= Items.LastOrDefault(i => i.IsSelected);
        if (item is null)
            return;
        if (item.IsFolder)
            OpenFolder(item.Path);
        else
            await _opener.OpenAsync(item.Record);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        Search = "";
        foreach (var filter in KindFilters)
            filter.IsChecked = false;
        Refresh();
    }

    [RelayCommand]
    private void SetSort(AssetSortKey key)
    {
        if (SortBy == key)
            SortDescending = !SortDescending;
        else
        {
            SortDescending = false;
            SortBy = key;
        }
    }

    [RelayCommand]
    private void ShowGrid() => IsGrid = true;

    [RelayCommand]
    private void ShowList() => IsGrid = false;

    [RelayCommand]
    private Task RescanAsync() => _project.RescanAsync();

    [RelayCommand]
    private Task ShowHealthAsync() => _health.ShowAsync();

    // Creating and importing

    /// <summary>The folder new and imported assets go to: the shown folder, or the asset root while searching.</summary>
    public string TargetFolder => ShowingFavorites || IsSearching ? "" : CurrentFolder;

    [RelayCommand]
    public async Task CreateFolderAsync()
    {
        if (await Operations.CreateFolderAsync(TargetFolder) is { } record)
            RevealCreated(record, rename: true);
    }

    [RelayCommand]
    public async Task CreateAsync(IAssetFactory? factory)
    {
        if (factory is null || !IsReady)
            return;
        var context = new AssetCreationContext(TargetFolder, [.. SelectedRecords], Operations, Dialogs, _project);
        if (await factory.CreateAsync(context) is { } record)
            RevealCreated(record, rename: !factory.AsksForName);
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var files = await _files.PickFilesToOpenAsync("Import assets", [new FileFilter("All files", ["*"])]);
        await ImportFilesAsync(files, TargetFolder);
    }

    /// <summary>Copies files from outside the project into a folder and selects them.</summary>
    public async Task ImportFilesAsync(IReadOnlyList<string> files, string folder)
    {
        var imported = await Operations.ImportAsync(files, folder);
        if (imported.Count == 0)
            return;
        if (!AssetPath.Comparer.Equals(folder, CurrentFolder) || IsSearching || ShowingFavorites)
            OpenFolder(folder);
        RefreshNow();
        SelectGuids(imported.Select(r => r.Guid));
    }

    // Item operations

    [RelayCommand]
    public void BeginRename(AssetItemViewModel? item)
    {
        item ??= Items.LastOrDefault(i => i.IsSelected);
        if (item is null)
            return;
        foreach (var other in Items.Where(i => i.IsRenaming && i != item))
            other.IsRenaming = false;
        item.RenameText = item.IsFolder ? item.Name : Path.GetFileNameWithoutExtension(item.Name);
        item.IsRenaming = true;
        ScrollRequested?.Invoke(this, item);
    }

    public async Task CommitRenameAsync(AssetItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsRenaming)
            return;
        item.IsRenaming = false;
        var text = item.RenameText.Trim();
        if (text.Length == 0)
            return;
        if (await Operations.RenameAsync(item.Record, text) is { } record)
        {
            RefreshNow();
            SelectGuids([record.Guid]);
        }
    }

    public static void CancelRename(AssetItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.IsRenaming = false;
    }

    [RelayCommand]
    public async Task DuplicateAsync()
    {
        var copies = await Operations.DuplicateAsync([.. SelectedRecords]);
        if (copies.Count == 0)
            return;
        RefreshNow();
        SelectGuids(copies.Select(c => c.Guid));
    }

    [RelayCommand]
    public async Task DeleteAsync()
    {
        var selected = SelectedRecords.ToList();
        if (selected.Count == 0)
            return;
        var index = Items.IndexOf(SelectedItems[0]);
        if (await Operations.DeleteAsync(selected))
        {
            RefreshNow();
            if (Items.Count > 0)
                SelectIndex(Math.Clamp(index, 0, Items.Count - 1), extend: false, toggle: false);
        }
    }

    /// <summary>Moves assets into a folder, such as when they are dropped on it.</summary>
    public async Task MoveAsync(IReadOnlyList<AssetGuid> assets, string folder)
    {
        if (_database is not { } database)
            return;
        var records = assets.Select(g => database.TryGetAsset(g, out var r) ? r : null).OfType<AssetRecord>().ToList();
        if (await Operations.MoveAsync(records, folder))
            RefreshNow();
    }

    /// <summary>Whether dragged assets may be dropped into a folder: not into themselves and not where they already are.</summary>
    public bool CanMoveInto(IReadOnlyList<AssetGuid> assets, string folder)
    {
        if (_database is not { } database || assets.Count == 0)
            return false;
        foreach (var guid in assets)
        {
            if (!database.TryGetAsset(guid, out var record))
                return false;
            if (record.IsFolder && (AssetPath.Comparer.Equals(record.Path, folder) || AssetPath.IsWithin(folder, record.Path)))
                return false;
        }

        return assets.Any(g => database.TryGetAsset(g, out var r) && !AssetPath.Comparer.Equals(AssetPath.GetDirectory(r.Path), folder));
    }

    [RelayCommand]
    private Task CopyPathAsync() => CopyAsync(string.Join(Environment.NewLine, SelectedRecords.Select(r => "assets/" + r.Path)));

    [RelayCommand]
    private Task CopyGuidAsync() => CopyAsync(string.Join(Environment.NewLine, SelectedRecords.Select(r => r.Guid.ToString())));

    [RelayCommand]
    private void Reveal() => Operations.RevealInFileManager(SelectedRecords.LastOrDefault() ?? (_database?.TryGetAsset(CurrentFolder, out var folder) == true ? folder : null));

    [RelayCommand]
    private Task ReimportAsync() => Operations.ReimportAsync([.. SelectedRecords]);

    [RelayCommand]
    public void ToggleFavorite()
    {
        var selected = SelectedItems;
        if (selected.Count == 0)
            return;
        var favorite = !selected.All(i => i.IsFavorite);
        foreach (var item in selected)
        {
            item.IsFavorite = favorite;
            if (favorite)
                _favorites.Add(item.Guid);
            else
                _favorites.Remove(item.Guid);
        }

        SaveState();
        if (ShowingFavorites)
            Refresh();
    }

    // Selection

    /// <summary>Selects an item as a click does: alone, toggled with Ctrl, or the range from the last clicked item with Shift.</summary>
    public void Click(AssetItemViewModel item, bool toggle, bool extend)
    {
        ArgumentNullException.ThrowIfNull(item);
        var index = Items.IndexOf(item);
        if (index < 0)
            return;
        SelectIndex(index, extend, toggle);
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var item in Items)
            item.IsSelected = true;
        PublishSelection();
    }

    public void ClearSelection()
    {
        foreach (var item in Items)
            item.IsSelected = false;
        _anchor = -1;
        PublishSelection();
    }

    /// <summary>Moves the selection with the arrow keys: by one item sideways, or by a row in the grid.</summary>
    public void MoveSelection(int dx, int dy, bool extend)
    {
        if (Items.Count == 0)
            return;
        var current = Items.Any(i => i.IsSelected) ? _focusIndex : -1;
        var step = dx + dy * (IsGrid ? Columns : 1);
        var next = current < 0 ? 0 : Math.Clamp(current + step, 0, Items.Count - 1);
        SelectIndex(next, extend, toggle: false);
        ScrollRequested?.Invoke(this, Items[next]);
    }

    private void SelectIndex(int index, bool extend, bool toggle)
    {
        if (extend && _anchor >= 0 && _anchor < Items.Count)
        {
            var (from, to) = _anchor <= index ? (_anchor, index) : (index, _anchor);
            for (var i = 0; i < Items.Count; i++)
                Items[i].IsSelected = (i >= from && i <= to) || (toggle && Items[i].IsSelected);
        }
        else if (toggle)
        {
            Items[index].IsSelected = !Items[index].IsSelected;
            _anchor = index;
        }
        else
        {
            for (var i = 0; i < Items.Count; i++)
                Items[i].IsSelected = i == index;
            _anchor = index;
        }

        _focusIndex = index;
        PublishSelection();
    }

    private void SelectGuids(IEnumerable<AssetGuid> guids)
    {
        var set = guids.ToHashSet();
        AssetItemViewModel? last = null;
        foreach (var item in Items)
        {
            item.IsSelected = set.Contains(item.Guid);
            if (item.IsSelected)
                last = item;
        }

        if (last is not null)
        {
            _anchor = _focusIndex = Items.IndexOf(last);
            ScrollRequested?.Invoke(this, last);
        }

        PublishSelection();
    }

    private void PublishSelection()
    {
        _syncingSelection = true;
        try
        {
            _selection.SelectAssets([.. Items.Where(i => i.IsSelected).Select(i => i.Guid)]);
        }
        finally
        {
            _syncingSelection = false;
        }

        OnPropertyChanged(nameof(HasSelection));
        UpdateStatus();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || (e.Kinds & SelectionKinds.Assets) == 0)
            return;
        Reveal(_selection.Assets);
    }

    /// <summary>Shows assets selected elsewhere, such as from the command palette: opens their folder when they are not in view and selects them.</summary>
    public void Reveal(IReadOnlyList<AssetGuid> assets)
    {
        if (assets.Count == 0)
        {
            foreach (var item in Items)
                item.IsSelected = false;
            OnPropertyChanged(nameof(HasSelection));
            UpdateStatus();
            return;
        }

        if (_database is not { } database)
            return;
        var primary = assets[^1];
        if (!Items.Any(i => i.Guid == primary) && database.TryGetAsset(primary, out var record))
            OpenFolder(AssetPath.GetDirectory(record.Path));
        var set = assets.ToHashSet();
        foreach (var item in Items)
            item.IsSelected = set.Contains(item.Guid);
        if (Items.FirstOrDefault(i => i.Guid == primary) is { } shown)
        {
            _anchor = _focusIndex = Items.IndexOf(shown);
            ScrollRequested?.Invoke(this, shown);
        }

        OnPropertyChanged(nameof(HasSelection));
        UpdateStatus();
    }

    // Database and refreshing

    private void OnProjectStatusChanged(object? sender, EventArgs e)
    {
        if (_database is null && _project.Database is { } database)
        {
            _database = database;
            database.AssetsChanged += OnAssetsChanged;
        }

        if (_project.ScanProgress is { } progress)
        {
            IsScanning = true;
            ScanFraction = progress.Fraction;
            ScanText = progress.Total == 0 ? "Scanning assets…" : $"Scanning assets… {progress.Processed:N0} of {progress.Total:N0}";
        }
        else
        {
            IsScanning = false;
        }

        if (IsReady && FolderRoots.Count == 0)
        {
            _treeDirty = true;
            Refresh();
        }

        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnAssetsChanged(object? sender, AssetChangesEventArgs e)
    {
        var changes = e.Changes;
        Dispatcher.UIThread.Post(() => Apply(changes));
    }

    private void Apply(IReadOnlyList<AssetChange> changes)
    {
        foreach (var change in changes)
        {
            if (change.Kind == AssetKind.Folder)
                _treeDirty = true;
            switch (change.Change)
            {
                case AssetChangeKind.Changed:
                    _thumbnails.Invalidate(change.Guid);
                    break;
                case AssetChangeKind.Deleted:
                    _thumbnails.Invalidate(change.Guid);
                    _thumbnails.Cache.Remove(change.Guid);
                    _cache.Remove(change.Guid);
                    break;
            }
        }

        if (!AssetPath.Comparer.Equals(CurrentFolder, "") && _database is { } database && !database.TryGetAsset(CurrentFolder, out _))
        {
            var moved = changes.FirstOrDefault(c => c.Change == AssetChangeKind.Moved && c.OldPath is { } old && AssetPath.Comparer.Equals(old, CurrentFolder));
            CurrentFolder = moved?.Path ?? "";
        }

        Refresh();
    }

    /// <summary>Rebuilds the listing on the next pass of the UI thread; several requests in a row rebuild once.</summary>
    public void Refresh()
    {
        if (_refreshQueued || _quiet)
            return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(RefreshNow, DispatcherPriority.Background);
    }

    /// <summary>Rebuilds the folder tree when folders changed and the listing, keeping the selection.</summary>
    public void RefreshNow()
    {
        _refreshQueued = false;
        if (_database is not { IsScanned: true } database)
            return;
        var records = database.Assets;
        if (_treeDirty)
            RebuildTree(records);
        UpdateKindFilters(records);

        var query = AssetSearchQuery.Parse(Search);
        var kinds = KindFilters.Where(k => k.IsChecked).Select(k => k.Kind).ToHashSet();
        IEnumerable<AssetRecord> listing;
        if (ShowingFavorites)
            listing = records.Where(r => _favorites.Contains(r.Guid) && query.Matches(r, true));
        else if (IsSearching)
            listing = records.Where(r => r.Path.Length > 0 && !r.IsFolder && (kinds.Count == 0 || kinds.Contains(r.Kind)) && query.Matches(r, _favorites.Contains(r.Guid)));
        else
            listing = records.Where(r => r.Path.Length > 0 && AssetPath.Comparer.Equals(AssetPath.GetDirectory(r.Path), CurrentFolder));

        var selected = Items.Where(i => i.IsSelected).Select(i => i.Guid).ToHashSet();
        var renaming = Items.FirstOrDefault(i => i.IsRenaming);
        var items = new List<AssetItemViewModel>();
        foreach (var record in Sort(listing))
        {
            if (!_cache.TryGetValue(record.Guid, out var item))
            {
                item = new AssetItemViewModel(record, _thumbnails);
                _cache[record.Guid] = item;
            }
            else if (!ReferenceEquals(item.Record, record))
            {
                item.Update(record);
            }

            item.IsFavorite = _favorites.Contains(record.Guid);
            item.IsSelected = selected.Contains(record.Guid);
            item.IsRenaming = ReferenceEquals(item, renaming);
            items.Add(item);
        }

        Items = new ObservableCollection<AssetItemViewModel>(items);
        _anchor = Math.Min(_anchor, Items.Count - 1);
        RebuildRows();
        UpdateStatus();
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(HasKindFilter));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(TargetFolder));
    }

    private IEnumerable<AssetRecord> Sort(IEnumerable<AssetRecord> records)
    {
        var folders = records.Where(r => r.IsFolder).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        var files = records.Where(r => !r.IsFolder);
        IOrderedEnumerable<AssetRecord> sorted = SortBy switch
        {
            AssetSortKey.Kind => Order(files, r => r.Kind.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            AssetSortKey.Size => Order(files, r => r.Size, Comparer<long>.Default).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            AssetSortKey.Modified => Order(files, r => r.LastWriteTimeUtc, Comparer<DateTime>.Default).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => Order(files, r => r.Name, NaturalComparer.Instance)
        };
        return folders.Concat(sorted);
    }

    private IOrderedEnumerable<AssetRecord> Order<TKey>(IEnumerable<AssetRecord> records, Func<AssetRecord, TKey> key, IComparer<TKey> comparer) =>
        SortDescending ? records.OrderByDescending(key, comparer) : records.OrderBy(key, comparer);

    private void RebuildRows()
    {
        var rows = new List<AssetRowViewModel>((Items.Count + Columns - 1) / Columns);
        for (var i = 0; i < Items.Count; i += Columns)
            rows.Add(new AssetRowViewModel([.. Items.Skip(i).Take(Columns)]));
        Rows = new ObservableCollection<AssetRowViewModel>(rows);
    }

    private FolderNodeViewModel? Root => FolderRoots.FirstOrDefault(n => n.IsRoot);

    private void RebuildTree(IReadOnlyList<AssetRecord> records)
    {
        _treeDirty = false;
        var root = new FolderNodeViewModel("", "assets", Icons.FolderOpen) { IsExpanded = true };
        var nodes = new Dictionary<string, FolderNodeViewModel>(AssetPath.Comparer) { [""] = root };
        foreach (var record in records.Where(r => r.IsFolder && r.Path.Length > 0).OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            var node = new FolderNodeViewModel(record.Path, record.Name, Icons.Folder) { IsExpanded = _expanded.Contains(record.Path) };
            node.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FolderNodeViewModel.IsExpanded))
                    OnFolderExpanded(node);
            };
            nodes[record.Path] = node;
            if (nodes.TryGetValue(AssetPath.GetDirectory(record.Path), out var parent))
                parent.Children.Add(node);
        }

        FolderRoots.Clear();
        FolderRoots.Add(new FolderNodeViewModel("", "Favorites", Icons.Star, isFavorites: true));
        FolderRoots.Add(root);
        SelectedFolderNode = ShowingFavorites ? FolderRoots[0] : root.Find(CurrentFolder) ?? root;
    }

    private void OnFolderExpanded(FolderNodeViewModel node)
    {
        if (node.IsExpanded)
            _expanded.Add(node.Path);
        else
            _expanded.Remove(node.Path);
        SaveState();
    }

    private void Expand(string folder)
    {
        _expanded.Add(folder);
        if (Root?.Find(folder) is { } node)
            node.IsExpanded = true;
    }

    private void UpdateKindFilters(IReadOnlyList<AssetRecord> records)
    {
        var counts = records.Where(r => !r.IsFolder && r.Path.Length > 0).GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count());
        foreach (var kind in counts.Keys.Where(k => KindFilters.All(f => f.Kind != k)).OrderBy(k => k.DisplayName, StringComparer.Ordinal))
        {
            var filter = new KindFilterViewModel(kind);
            filter.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(KindFilterViewModel.IsChecked))
                    Refresh();
            };
            KindFilters.Add(filter);
        }
        foreach (var filter in KindFilters)
            filter.Count = counts.GetValueOrDefault(filter.Kind);
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new BreadcrumbViewModel("assets", "", CurrentFolder.Length == 0));
        if (CurrentFolder.Length == 0)
            return;
        var parts = CurrentFolder.Split('/');
        for (var i = 0; i < parts.Length; i++)
            Breadcrumbs.Add(new BreadcrumbViewModel(parts[i], string.Join('/', parts.Take(i + 1)), i == parts.Length - 1));
    }

    private void UpdateStatus()
    {
        var selected = Items.Count(i => i.IsSelected);
        var count = Items.Count;
        var text = count == 1 ? "1 item" : $"{count:N0} items";
        if (selected > 0)
        {
            var size = Items.Where(i => i.IsSelected && !i.IsFolder).Sum(i => i.Record.Size);
            text += $"  ·  {selected:N0} selected";
            if (size > 0)
                text += $" ({AssetItemViewModel.FormatSize(size)})";
        }

        StatusText = text;
    }

    private void RevealCreated(AssetRecord record, bool rename)
    {
        if (!IsSearching && !ShowingFavorites && !AssetPath.Comparer.Equals(AssetPath.GetDirectory(record.Path), CurrentFolder))
            OpenFolder(AssetPath.GetDirectory(record.Path));
        if (record.IsFolder)
            _treeDirty = true;
        RefreshNow();
        SelectGuids([record.Guid]);
        if (rename && Items.FirstOrDefault(i => i.Guid == record.Guid) is { } item)
            BeginRename(item);
    }

    private async Task CopyAsync(string text)
    {
        if (text.Length > 0 && _window.TopLevel?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private static int GroupOrder(string group) => group switch
    {
        "documents" => 0,
        "code" => 1,
        "rendering" => 2,
        "effects" => 3,
        "data" => 4,
        _ => 5
    };

    private void LoadState()
    {
        if (_state.Get<BrowserState>(StateKey) is not { } saved)
            return;
        _quiet = true;
        CurrentFolder = saved.Folder ?? "";
        IsGrid = saved.Grid;
        ThumbnailSize = Math.Clamp(saved.ThumbnailSize, 48, 128);
        SortBy = saved.Sort;
        SortDescending = saved.Descending;
        _quiet = false;
        foreach (var favorite in saved.Favorites ?? [])
        {
            if (AssetGuid.TryParse(favorite, null, out var guid))
                _favorites.Add(guid);
        }

        foreach (var folder in saved.Expanded ?? [])
            _expanded.Add(folder);
        UpdateBreadcrumbs();
    }

    private void SaveState()
    {
        if (!_quiet)
            _state.Set(StateKey, new BrowserState(CurrentFolder, IsGrid, ThumbnailSize, SortBy, SortDescending, [.. _favorites.Select(f => f.ToString())], [.. _expanded]));
    }

    public void Dispose()
    {
        _selection.Changed -= OnSelectionChanged;
        _project.StatusChanged -= OnProjectStatusChanged;
        if (_database is not null)
            _database.AssetsChanged -= OnAssetsChanged;
    }

    private sealed record BrowserState(string? Folder, bool Grid, double ThumbnailSize, AssetSortKey Sort, bool Descending, List<string>? Favorites, List<string>? Expanded);
}
