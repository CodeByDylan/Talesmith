using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Ecs;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Documents;
using Talesmith.Editor.DragAndDrop;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Talesmith.UI.Controls;
using SelectionMode = Talesmith.Editor.Selection.SelectionMode;

namespace Talesmith.Editor.Hierarchy;

/// <summary>The Hierarchy panel's state: the rows of the open scene or of the play session, the selection, renaming, visibility and lock
/// toggles, creating entities and dropping entities and assets.</summary>
public sealed partial class HierarchyViewModel : ObservableObject
{
    private static readonly TimeSpan LiveRefresh = TimeSpan.FromMilliseconds(250);

    private readonly ISceneDocumentService _documents;
    private readonly ISelectionService _selection;
    private readonly IUndoService _undo;
    private readonly IPlayModeService _play;
    private readonly LiveSelection _liveSelection;
    private readonly EntityTemplates _templates;
    private readonly PrefabWorkflow _prefabs;
    private readonly IProjectService _project;
    private readonly SceneAssetDrop _assetDrop;
    private readonly ViewportCamera _camera;
    private readonly EditorCommandRegistry _commands;
    private readonly DispatcherTimer _liveTimer;
    private readonly HashSet<HierarchyRow> _selectedRows = [];
    private SceneDocumentModel? _model;
    private Guid? _anchor;
    private bool _selecting;
    private bool _capturing;
    private IReadOnlyList<LiveEntityInfo> _lastCapture = [];

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private ObservableCollection<HierarchyRow> _rows = [];

    public HierarchyViewModel(ISceneDocumentService documents, ISelectionService selection, IUndoService undo, IPlayModeService play, LiveSelection liveSelection,
        HierarchyTree tree, EntityTemplates templates, PrefabWorkflow prefabs, PrefabLibrary library, ViewportCamera camera, EditorCommandRegistry commands,
        IProjectService project, SceneAssetDrop assetDrop)
    {
        _documents = documents;
        _selection = selection;
        _undo = undo;
        _play = play;
        _liveSelection = liveSelection;
        Tree = tree;
        _templates = templates;
        _prefabs = prefabs;
        _project = project;
        _assetDrop = assetDrop;
        _camera = camera;
        _commands = commands;
        Live = new LiveHierarchy();
        _liveTimer = new DispatcherTimer { Interval = LiveRefresh };
        _liveTimer.Tick += (_, _) => RefreshLive();

        _documents.ActiveChanged += (_, _) => Track();
        _documents.StateChanged += (_, _) => OnPropertyChanged(nameof(SceneTitle));
        _selection.Changed += (_, _) => SyncSelection(reveal: !_selecting);
        _liveSelection.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveSelection.Entity))
                SyncLiveSelection();
        };
        library.Changed += (_, _) => Tree.RefreshInstances();
        _play.StateChanged += (_, _) => OnPlayStateChanged();
        Tree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HierarchyTree.Rows) && !IsLive)
                Rows = Tree.Rows;
        };
        _prefabs.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName == nameof(PrefabWorkflow.ReturnScene) ? nameof(ReturnName) : nameof(IsEditingPrefab));
        Track();
    }

    public HierarchyTree Tree { get; }

    public LiveHierarchy Live { get; }

    /// <summary>Whether the rows show the play session's world.</summary>
    public bool IsLive => _play.IsPlaying;

    public bool IsPaused => _play.IsPaused;

    public bool HasScene => _model is not null;

    public string SceneTitle => _model?.Title ?? "";

    public bool IsEditingPrefab => _prefabs.IsEditingPrefab;

    public string ReturnName => _prefabs.ReturnName;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Raised when a row should scroll into view, with its index.</summary>
    public event EventHandler<int>? ScrollRequested;

    /// <summary>Raised when a row starts renaming, so the view can focus its text box.</summary>
    public event EventHandler<HierarchyRow>? RenameStarted;

    partial void OnFilterChanged(string value)
    {
        Tree.SetFilter(value);
        Live.SetFilter(value);
        if (IsLive)
            ShowLive();
        else
            SyncSelection(reveal: true);
    }

    partial void OnRowsChanged(ObservableCollection<HierarchyRow> value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        value.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Selects a row as a click does: alone, toggled with Ctrl or as a range from the last clicked row with Shift.</summary>
    public void Click(HierarchyRow row, KeyModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Kind == HierarchyRowKind.Live)
        {
            _liveSelection.Entity = row.LiveEntity;
            return;
        }

        _selecting = true;
        try
        {
            if ((modifiers & KeyModifiers.Control) != 0)
            {
                _selection.SelectEntity(row.Id, SelectionMode.Toggle);
                _anchor = row.Id;
            }
            else if ((modifiers & KeyModifiers.Shift) != 0 && _anchor is { } anchor && IndexOf(anchor) is var start and >= 0)
            {
                var end = Rows.IndexOf(row);
                var (from, to) = start <= end ? (start, end) : (end, start);
                var range = Rows.Skip(from).Take(to - from + 1).Where(r => r.Kind != HierarchyRowKind.Live).Select(r => r.Id).ToList();
                if (start > end)
                    range.Reverse();
                _selection.SelectEntities(range);
            }
            else
            {
                _selection.SelectEntity(row.Id);
                _anchor = row.Id;
            }
        }
        finally
        {
            _selecting = false;
        }
    }

    public void ClearSelection()
    {
        if (IsLive)
            _liveSelection.Entity = Entity.Null;
        else
            _selection.SelectEntities([]);
    }

    /// <summary>Leaves the prefab being edited for the scene it was opened from.</summary>
    public void Back() => _ = _prefabs.BackAsync();

    /// <summary>Moves the selection by rows, as the arrow keys do; Shift extends it from the anchor.</summary>
    public void MoveSelection(int delta, bool extend)
    {
        if (Rows.Count == 0)
            return;
        var current = IsLive ? Rows.ToList().FindIndex(r => r.LiveEntity == _liveSelection.Entity) : _selection.Entities.Count > 0 ? IndexOf(_selection.Entities[^1]) : -1;
        var next = Math.Clamp(current < 0 ? (delta > 0 ? 0 : Rows.Count - 1) : current + delta, 0, Rows.Count - 1);
        var row = Rows[next];
        Click(row, extend ? KeyModifiers.Shift : KeyModifiers.None);
        if (!extend)
            _anchor = row.Id;
        ScrollRequested?.Invoke(this, next);
    }

    /// <summary>Collapses the selected row, or selects its parent when it is collapsed already.</summary>
    public void CollapseOrParent()
    {
        if (PrimaryRow() is not { } row)
            return;
        if (row.HasChildren && row.IsExpanded)
        {
            SetExpanded(row, false);
            return;
        }

        var parent = IsLive ? Live.GetParent(row.LiveEntity) : Tree.GetParent(row.Id);
        if (parent is not null)
        {
            Click(parent, KeyModifiers.None);
            ScrollRequested?.Invoke(this, Rows.IndexOf(parent));
        }
    }

    /// <summary>Expands the selected row, or selects its first child when it is expanded already.</summary>
    public void ExpandOrChild()
    {
        if (PrimaryRow() is not { HasChildren: true } row)
            return;
        if (!row.IsExpanded)
        {
            SetExpanded(row, true);
            return;
        }

        var index = Rows.IndexOf(row);
        if (index + 1 < Rows.Count)
            MoveSelection(1, extend: false);
    }

    public void SetExpanded(HierarchyRow row, bool expanded, bool recursive = false)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Kind == HierarchyRowKind.Live)
        {
            Live.SetExpanded(row.LiveEntity, expanded);
            ShowLive();
            return;
        }

        if (recursive)
            Tree.SetExpandedRecursive(row.Id, expanded);
        else
            Tree.SetExpanded(row.Id, expanded);
    }

    [RelayCommand]
    private void ToggleExpanded(HierarchyRow row) => SetExpanded(row, !row.IsExpanded);

    [RelayCommand]
    private void ToggleVisibility(HierarchyRow row)
    {
        if (row.Kind == HierarchyRowKind.Live)
        {
            var entity = row.LiveEntity;
            _play.Dispatch(game => LiveHierarchy.ToggleActive(game, entity));
            row.IsActive = !row.IsActive;
            return;
        }

        if (_model is not { } model || !model.Contains(row.Id))
            return;
        var hidden = !row.IsHidden;
        var targets = row.IsSelected ? _selection.Entities.Where(model.Contains).ToList() : [row.Id];
        using var transaction = targets.Count > 1 ? _undo.BeginTransaction(hidden ? $"Hide {targets.Count} entities" : $"Show {targets.Count} entities") : null;
        foreach (var id in targets)
            model.SetHidden(id, hidden);
    }

    [RelayCommand]
    private void ToggleLock(HierarchyRow row)
    {
        if (_model is not { } model || !model.Contains(row.Id))
            return;
        var locked = !row.IsLocked;
        var targets = row.IsSelected ? _selection.Entities.Where(model.Contains).ToList() : [row.Id];
        using var transaction = targets.Count > 1 ? _undo.BeginTransaction(locked ? $"Lock {targets.Count} entities" : $"Unlock {targets.Count} entities") : null;
        foreach (var id in targets)
            model.SetLocked(id, locked);
    }

    /// <summary>Starts renaming the primary selection, or the given row.</summary>
    public void BeginRename(HierarchyRow? row = null)
    {
        row ??= PrimaryRow();
        if (row is not { IsEditable: true } || _model is null)
            return;
        Tree.Reveal(row.Id);
        foreach (var other in Rows.Where(r => r.IsRenaming))
            other.IsRenaming = false;
        row.IsRenaming = true;
        ScrollRequested?.Invoke(this, Rows.IndexOf(row));
        RenameStarted?.Invoke(this, row);
    }

    public void CommitRename(HierarchyRow row, string? name)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.IsRenaming)
            return;
        row.IsRenaming = false;
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || _model is not { } model || !model.Contains(row.Id))
            return;
        model.Rename(row.Id, name);
        _undo.Seal();
    }

    public static void CancelRename(HierarchyRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.IsRenaming = false;
    }

    /// <summary>Creates an entity from a template, or an empty one, as a child of <paramref name="parent"/> or at the root; selects and returns it.</summary>
    public Guid? Create(string? template, Guid? parent, int siblingIndex = -1, string? name = null)
    {
        if (_model is not { } model)
            return null;
        if (parent is { } p && !model.Contains(p))
            parent = Tree.GetParent(p) is { IsEditable: true } stored ? stored.Id : null;
        var definition = template is null ? null : _templates.Find(template);
        var components = new List<ComponentDocument> { EntityTemplates.Transform(parent is null ? _camera.Position : Vector2.Zero) };
        if (definition is not null)
            components.AddRange(definition.Components());
        var entity = model.CreateEntity(SceneAssetDrop.UniqueName(model, name ?? definition?.Name ?? "Entity", parent), parent, components, siblingIndex);
        _selection.SelectEntity(entity.Id);
        return entity.Id;
    }

    /// <summary>Selects the children of the selected entities.</summary>
    public void SelectChildren()
    {
        var children = _selection.Entities.SelectMany(id => Tree.GetChildren(id)).Select(r => r.Id).Distinct().ToList();
        if (children.Count == 0)
            return;
        foreach (var id in _selection.Entities)
        {
            if (Tree.Find(id) is { } row)
                Tree.SetExpanded(row.Id, true);
        }

        _selection.SelectEntities(children);
    }

    /// <summary>Reads dragged entities or assets of the project.</summary>
    public bool TryReadDrag(IDataTransfer? transfer, out EditorDragData data) => EditorDragData.TryGet(transfer, _project.Catalog, out data);

    /// <summary>Whether data dragged over a row could drop at a position; <paramref name="target"/> null means empty space below the rows.</summary>
    public bool CanDrop(EditorDragData data, HierarchyRow? target, DropPosition position)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_model is not { } model || IsLive)
            return false;
        if (target is not null && target.Kind != HierarchyRowKind.Entity)
            return false;
        if (data.HasEntities)
        {
            var dragged = data.Entities.Where(model.Contains).ToList();
            if (dragged.Count == 0)
                return false;
            if (target is null)
                return true;
            var parent = position == DropPosition.Inside ? target.Id : model.Find(target.Id)?.Parent;
            return parent is not { } p || dragged.All(d => !model.IsSelfOrDescendant(p, d)) && (position == DropPosition.Inside || !dragged.Contains(target.Id));
        }

        return data.HasAssets && data.Assets.Any(_assetDrop.CanCreate);
    }

    /// <summary>Moves dragged entities, or creates entities from dragged assets, at a drop position; returns whether anything happened.</summary>
    public bool Drop(EditorDragData data, HierarchyRow? target, DropPosition position)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!CanDrop(data, target, position) || _model is not { } model)
            return false;
        Guid? parent = target is null ? null : position == DropPosition.Inside ? target.Id : model.Find(target.Id)!.Parent;
        if (data.HasEntities)
        {
            var dragged = model.GetTopLevel(data.Entities);
            using var transaction = _undo.BeginTransaction(dragged.Count == 1 ? $"Move {model.Get(dragged[0]).Name}" : $"Move {dragged.Count} entities");
            Guid? previous = null;
            foreach (var id in dragged)
            {
                if (target is null || position == DropPosition.Inside)
                    model.MoveEntity(id, parent, -1);
                else if (previous is { } after)
                    model.MoveEntity(id, parent, IndexAround(model, id, after, after: true));
                else
                    model.MoveEntity(id, parent, IndexAround(model, id, target.Id, after: position == DropPosition.After));
                previous = id;
            }

            if (parent is { } p)
                Tree.SetExpanded(p, true);
            _selection.SelectEntities(dragged);
            return true;
        }

        var index = target is null || position == DropPosition.Inside ? -1 : model.GetSiblingIndex(target.Id) + (position == DropPosition.After ? 1 : 0);
        var created = _assetDrop.Drop(data.Assets, parent, index, parent is null ? _camera.Position : Vector2.Zero);
        if (parent is { } expanded)
            Tree.SetExpanded(expanded, true);
        return created.Count > 0;
    }


    /// <summary>The menu for a row, or for empty space when <paramref name="row"/> is null.</summary>
    public IReadOnlyList<MenuItemViewModel> BuildMenu(HierarchyRow? row)
    {
        var items = new List<MenuItemViewModel>();
        if (IsLive || _model is null)
            return items;
        Guid? parent = row is { IsEditable: true } ? row.Id : null;
        items.Add(Item("Create Empty", Icons.Box, () => Create(null, null), "Ctrl+Shift+N"));
        if (row is not null)
            items.Add(Item("Create Empty Child", Icons.ListTree, () => Create(null, parent ?? row.Id), "Alt+Shift+N"));
        items.Add(ObjectsMenu(parent));
        if (row is null)
        {
            items.Add(MenuItemViewModel.Separator);
            items.Add(Command("entity.paste"));
            return items;
        }

        items.Add(MenuItemViewModel.Separator);
        items.Add(Command("entity.copy"));
        items.Add(Command("entity.paste"));
        items.Add(Command("edit.duplicate"));
        items.Add(Command("entity.rename"));
        items.Add(Command("edit.delete"));
        items.Add(MenuItemViewModel.Separator);
        items.Add(Command("prefab.create"));
        if (row.IsPrefab)
        {
            items.Add(Command("prefab.open"));
            if (row.IsPrefabInstance)
            {
                items.Add(Command("prefab.apply"));
                items.Add(Command("prefab.revert"));
            }
        }

        items.Add(MenuItemViewModel.Separator);
        items.Add(Command("entity.selectChildren"));
        items.Add(Item("Expand All", Icons.ChevronsDown, () => SetExpanded(row, true, recursive: true)));
        items.Add(Item("Collapse All", Icons.ChevronsUp, () => SetExpanded(row, false, recursive: true)));
        items.Add(Command("view.frameSelection"));
        return [.. items.Where(i => i is not null)];
    }

    /// <summary>The 2D Object submenu, creating under <paramref name="parent"/>.</summary>
    public MenuItemViewModel ObjectsMenu(Guid? parent)
    {
        var menu = new MenuItemViewModel { Header = "2D Object", Icon = Icons.Find("shapes") ?? Icons.Box, Items = [] };
        var groups = new Dictionary<string, MenuItemViewModel>(StringComparer.Ordinal);
        foreach (var template in _templates.All)
        {
            var item = Item(template.Name, template.Icon, () => Create(template.Id, parent), toolTip: template.Description);
            if (template.Group is null)
            {
                menu.Items.Add(item);
                continue;
            }

            if (!groups.TryGetValue(template.Group, out var group))
            {
                groups[template.Group] = group = new MenuItemViewModel { Header = template.Group, Icon = template.Icon, Items = [] };
                menu.Items.Add(group);
            }

            group.Items.Add(item);
        }

        return menu;
    }

    private MenuItemViewModel Command(string id)
    {
        if (_commands.Find(id) is not { } command)
            return MenuItemViewModel.Separator;
        return new MenuItemViewModel { Header = command.Title, Command = command.Command, Gesture = command.Gesture, Icon = command.Icon, ToolTip = command.Description };
    }

    private static MenuItemViewModel Item(string header, global::Avalonia.Media.Geometry? icon, Action action, string? gesture = null, string? toolTip = null) => new()
    {
        Header = header,
        Icon = icon,
        Command = new RelayCommand(action),
        Gesture = gesture is null ? null : KeyGesture.Parse(gesture),
        ToolTip = toolTip
    };

    /// <summary>The sibling index to move <paramref name="id"/> to so it lands right before or after <paramref name="anchor"/>.</summary>
    private static int IndexAround(SceneDocumentModel model, Guid id, Guid anchor, bool after)
    {
        var anchorIndex = model.GetSiblingIndex(anchor);
        var entity = model.Get(id);
        if (entity.Parent == model.Get(anchor).Parent && model.GetSiblingIndex(id) < anchorIndex)
            anchorIndex--;
        return anchorIndex + (after ? 1 : 0);
    }

    private HierarchyRow? PrimaryRow()
    {
        if (IsLive)
            return Live.Find(_liveSelection.Entity);
        return _selection.Entities.Count > 0 ? Tree.Find(_selection.Entities[^1]) : null;
    }

    private int IndexOf(Guid id)
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].Id == id)
                return i;
        }

        return -1;
    }


    private void Track()
    {
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = _documents.Active;
        if (_model is not null)
            _model.Changed += OnSceneChanged;
        _selectedRows.Clear();
        Tree.Load(_model);
        if (!IsLive)
            Rows = Tree.Rows;
        OnPropertyChanged(nameof(HasScene));
        OnPropertyChanged(nameof(SceneTitle));
        OnPropertyChanged(nameof(IsEditingPrefab));
        SyncSelection(reveal: true);
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        Tree.Apply(e.Change);
        if (e.Change.Kind is SceneChangeKind.EntityAdded or SceneChangeKind.EntityMoved or SceneChangeKind.EntityReplaced or SceneChangeKind.Reloaded)
            SyncSelection(reveal: false);
    }

    private void SyncSelection(bool reveal)
    {
        if (IsLive)
            return;
        foreach (var row in _selectedRows)
            row.IsSelected = false;
        _selectedRows.Clear();
        foreach (var id in _selection.Entities)
        {
            if (reveal && !Tree.IsFiltering)
                Tree.Reveal(id);
            if (Tree.Find(id) is { } row)
            {
                row.IsSelected = true;
                _selectedRows.Add(row);
            }
        }

        if (reveal && _selection.Entities.Count > 0 && IndexOf(_selection.Entities[^1]) is var index and >= 0)
            ScrollRequested?.Invoke(this, index);
    }

    private void SyncLiveSelection()
    {
        foreach (var previous in _selectedRows)
            previous.IsSelected = false;
        _selectedRows.Clear();
        if (!IsLive || Live.Find(_liveSelection.Entity) is not { } row)
            return;
        row.IsSelected = true;
        _selectedRows.Add(row);
    }

    private void OnPlayStateChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsPaused));
        if (IsLive)
        {
            if (!_liveTimer.IsEnabled)
            {
                Live.Clear();
                _lastCapture = [];
                _liveTimer.Start();
                RefreshLive();
            }
        }
        else
        {
            _liveTimer.Stop();
            Live.Clear();
            _lastCapture = [];
            Rows = Tree.Rows;
            SyncSelection(reveal: false);
        }
    }

    private async void RefreshLive()
    {
        if (!IsLive || _capturing)
            return;
        _capturing = true;
        try
        {
            var captured = await _play.TryInvokeAsync(LiveHierarchy.Capture, null);
            if (!IsLive || captured is null)
                return;
            _lastCapture = captured;
            ShowLive(revealSelection: _liveSelection.Entity.IsNull);
            if (_liveSelection.Entity.IsNull)
                await _liveSelection.SyncFromSelectionAsync();
        }
        finally
        {
            _capturing = false;
        }
    }

    private void ShowLive(bool revealSelection = false)
    {
        if (revealSelection && !_liveSelection.Entity.IsNull)
            Live.Reveal(_liveSelection.Entity);
        if (Live.Refresh(_lastCapture, id => Tree.Find(id)?.Icon) || !ReferenceEquals(Rows, Live.Rows))
        {
            Rows = Live.Rows;
            SyncLiveSelection();
        }
    }
}
