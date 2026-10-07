using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.Undo;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>A layer role with its name and icon, for the role picker.</summary>
public sealed record LayerRoleOption(LayerRole Role, string Name, Geometry Icon, string Description)
{
    public static IReadOnlyList<LayerRoleOption> All { get; } =
    [
        new(LayerRole.Ground, "Ground", Icons.Grid, "Terrain drawn at the back"),
        new(LayerRole.Decoration, "Decoration", Icons.Sparkles, "Details drawn over the ground"),
        new(LayerRole.Collision, "Collision", Icons.Shield, "Non-empty cells block movement"),
        new(LayerRole.Trigger, "Trigger", Icons.Flag, "Cells or areas that report entering and leaving"),
        new(LayerRole.Object, "Object", Icons.MapPin, "Points, areas and placed images"),
        new(LayerRole.Navigation, "Navigation", Icons.Compass, "Walkability and costs for pathfinding"),
        new(LayerRole.Custom, "Custom", Icons.Puzzle, "Defined by a plugin")
    ];

    public static LayerRoleOption Of(LayerRole role) => All.FirstOrDefault(o => o.Role == role) ?? All[0];
}

/// <summary>The layers of the edited map, top to bottom as drawn over each other, with Hexy's layer operations: add tile and object layers,
/// rename, reorder by dragging, duplicate, delete, show, lock, opacity and role.</summary>
public sealed partial class LayersViewModel : ObservableObject
{
    private readonly TileMapEditor _editor;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _summaries;
    private TileMap? _map;
    private bool _syncing;

    [ObservableProperty]
    private LayerItemViewModel? _selectedItem;

    public LayersViewModel(TileMapEditor editor, IDialogService dialogs)
    {
        _editor = editor;
        _dialogs = dialogs;
        _summaries = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _summaries.Tick += (_, _) => RefreshSummaries();
        editor.PropertyChanged += OnEditorChanged;
        editor.MapChanged += OnMapChanged;
        Rebuild();
    }

    /// <summary>Layers ordered top to bottom.</summary>
    public ObservableCollection<LayerItemViewModel> Items { get; } = [];

    public bool HasLayers => Items.Count > 0;

    [RelayCommand]
    private void AddTileLayer()
    {
        if (_editor.Map is { } map)
            Add(map.CreateTileLayer(map.UniqueLayerName("Tiles")));
    }

    [RelayCommand]
    private void AddObjectLayer()
    {
        if (_editor.Map is { } map)
            Add(new ObjectLayer(map.UniqueLayerName("Objects")));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (_editor.Map is not { } map || _editor.ActiveLayer is not { } layer)
            return;
        var name = map.UniqueLayerName(layer.Name + " copy");
        MapLayer copy = layer switch
        {
            TileLayer tiles => tiles.Clone(name),
            ObjectLayer objects => Renumbered(map, objects.Clone(name)),
            _ => throw new InvalidOperationException()
        };
        Add(copy);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (_editor.ActiveLayer is not { } layer)
            return;
        var hasContent = layer switch
        {
            TileLayer tiles => tiles.Chunks.Count > 0,
            ObjectLayer objects => objects.Objects.Count > 0,
            _ => false
        };
        if (hasContent && !await _dialogs.ConfirmAsync("Delete layer?", $"\"{layer.Name}\" and its content will be removed. You can undo this.", "Delete",
                isDestructive: true))
            return;
        _editor.Execute($"Delete layer \"{layer.Name}\"", MapEdits.RemoveLayer(layer));
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => MoveBy(1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => MoveBy(-1);

    [RelayCommand]
    private static void BeginRename(LayerItemViewModel? item)
    {
        if (item is not null)
            item.IsRenaming = true;
    }

    /// <summary>Moves a layer to a row of the list, as dropping a dragged row does; rows count from the top.</summary>
    public void MoveToRow(LayerItemViewModel item, int row)
    {
        if (_map is not { } map)
            return;
        var from = map.IndexOf(item.Layer);
        var to = Math.Clamp(map.Layers.Count - 1 - row, 0, map.Layers.Count - 1);
        if (from < 0 || from == to)
            return;
        _editor.Execute($"Move layer \"{item.Layer.Name}\"", MapEdits.MoveLayer(item.Layer, to));
        _editor.ActiveLayer = item.Layer;
    }

    /// <summary>Selects the layer above or below the active one.</summary>
    public void SelectAdjacent(int direction)
    {
        if (Items.Count == 0)
            return;
        var index = SelectedItem is { } selected ? Items.IndexOf(selected) : 0;
        SelectedItem = Items[Math.Clamp(index - direction, 0, Items.Count - 1)];
    }

    private bool HasSelection() => _editor.ActiveLayer is not null;

    private bool CanMoveUp() => _map is { } map && _editor.ActiveLayer is { } layer && map.IndexOf(layer) < map.Layers.Count - 1;

    private bool CanMoveDown() => _map is { } map && _editor.ActiveLayer is { } layer && map.IndexOf(layer) > 0;

    private void Add(MapLayer layer)
    {
        var map = _editor.Map!;
        var index = _editor.ActiveLayer is { } active ? map.IndexOf(active) + 1 : map.Layers.Count;
        _editor.Execute($"Add layer \"{layer.Name}\"", MapEdits.InsertLayer(layer, index));
        _editor.ActiveLayer = layer;
    }

    private void MoveBy(int direction)
    {
        if (_map is not { } map || _editor.ActiveLayer is not { } layer)
            return;
        var to = map.IndexOf(layer) + direction;
        if (to < 0 || to >= map.Layers.Count)
            return;
        _editor.Execute($"Move layer \"{layer.Name}\"", MapEdits.MoveLayer(layer, to));
        _editor.ActiveLayer = layer;
    }

    private static ObjectLayer Renumbered(TileMap map, ObjectLayer layer)
    {
        var next = map.NextObjectId;
        var objects = layer.Objects.Select(o => o with { Id = next++ }).ToList();
        var copy = new ObjectLayer(layer.Name, objects) { Color = layer.Color };
        copy.Role = layer.Role;
        copy.IsVisible = layer.IsVisible;
        copy.IsLocked = layer.IsLocked;
        copy.Opacity = layer.Opacity;
        copy.Properties = layer.Properties;
        return copy;
    }

    private void Rebuild()
    {
        _map = _editor.Map;
        _syncing = true;
        Items.Clear();
        if (_map is { } map)
        {
            for (var i = map.Layers.Count - 1; i >= 0; i--)
                Items.Add(new LayerItemViewModel(map.Layers[i], _editor));
        }

        SelectedItem = Items.FirstOrDefault(i => ReferenceEquals(i.Layer, _editor.ActiveLayer));
        _syncing = false;
        OnPropertyChanged(nameof(HasLayers));
        NotifyCommands();
        _summaries.Stop();
        _summaries.Start();
    }

    partial void OnSelectedItemChanged(LayerItemViewModel? value)
    {
        if (!_syncing && value is not null)
            _editor.ActiveLayer = value.Layer;
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TileMapEditor.Target) when !ReferenceEquals(_editor.Map, _map):
                Rebuild();
                break;
            case nameof(TileMapEditor.ActiveLayer):
                _syncing = true;
                SelectedItem = Items.FirstOrDefault(i => ReferenceEquals(i.Layer, _editor.ActiveLayer));
                _syncing = false;
                NotifyCommands();
                break;
        }
    }

    private void OnMapChanged(object? sender, MapChange change)
    {
        switch (change.Kind)
        {
            case MapChangeKind.LayerAdded or MapChangeKind.LayerRemoved or MapChangeKind.LayerMoved:
                Rebuild();
                break;
            case MapChangeKind.LayerChanged:
                Items.FirstOrDefault(i => ReferenceEquals(i.Layer, change.Layer))?.Refresh();
                break;
            case MapChangeKind.Cells or MapChangeKind.Objects:
                Items.FirstOrDefault(i => ReferenceEquals(i.Layer, change.Layer))?.MarkContentChanged();
                if (!_summaries.IsEnabled)
                    _summaries.Start();
                break;
        }
    }

    private void RefreshSummaries()
    {
        _summaries.Stop();
        foreach (var item in Items)
            item.RefreshSummary();
    }

    private void NotifyCommands()
    {
        DuplicateCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>A row of the layer list.</summary>
public sealed partial class LayerItemViewModel(MapLayer layer, TileMapEditor editor) : ObservableObject
{
    /// <summary>Layers with more chunks show their chunk count, so huge maps are not decoded just to count tiles.</summary>
    private const int MaxCountedChunks = 512;

    private UndoTransaction? _opacityEdit;
    private bool _contentChanged = true;
    private string _summary = "";

    [ObservableProperty]
    private bool _isRenaming;

    public MapLayer Layer { get; } = layer;

    public bool IsObjectLayer => Layer is ObjectLayer;

    public Geometry KindIcon => Layer is ObjectLayer ? Icons.MapPin : Icons.Layers;

    public LayerRoleOption Role => LayerRoleOption.Of(Layer.Role);

    public string Summary => _summary;

    public string Name
    {
        get => Layer.Name;
        set
        {
            var trimmed = value?.Trim() ?? "";
            if (trimmed.Length == 0 || trimmed == Layer.Name)
                return;
            Change($"Rename layer to \"{trimmed}\"", LayerSettings.Of(Layer) with { Name = trimmed });
        }
    }

    public bool IsVisible
    {
        get => Layer.IsVisible;
        set => Change(value ? $"Show \"{Layer.Name}\"" : $"Hide \"{Layer.Name}\"", LayerSettings.Of(Layer) with { IsVisible = value });
    }

    public bool IsLocked
    {
        get => Layer.IsLocked;
        set => Change(value ? $"Lock \"{Layer.Name}\"" : $"Unlock \"{Layer.Name}\"", LayerSettings.Of(Layer) with { IsLocked = value });
    }

    /// <summary>The opacity in percent; a drag of the slider is one undo step.</summary>
    public double OpacityPercent
    {
        get => Math.Round(Layer.Opacity * 100);
        set
        {
            var opacity = (float)Math.Clamp(value / 100, 0, 1);
            if (Math.Abs(opacity - Layer.Opacity) >= 0.001f)
                Change($"Set \"{Layer.Name}\" opacity", LayerSettings.Of(Layer) with { Opacity = opacity });
        }
    }

    [RelayCommand]
    private void ToggleVisibility() => IsVisible = !IsVisible;

    [RelayCommand]
    private void ToggleLock() => IsLocked = !IsLocked;

    [RelayCommand]
    private void SetRole(LayerRoleOption option)
    {
        if (option.Role != Layer.Role)
            Change($"Make \"{Layer.Name}\" a {option.Name.ToLowerInvariant()} layer", LayerSettings.Of(Layer) with { Role = option.Role });
    }

    /// <summary>Starts a slider drag, so its opacity changes become one undo step.</summary>
    public void BeginOpacityEdit() => _opacityEdit ??= editor.BeginTransaction($"Set \"{Layer.Name}\" opacity");

    public void EndOpacityEdit()
    {
        _opacityEdit?.Dispose();
        _opacityEdit = null;
    }

    internal void Refresh() => OnPropertyChanged(string.Empty);

    internal void MarkContentChanged() => _contentChanged = true;

    internal void RefreshSummary()
    {
        if (!_contentChanged)
            return;
        _contentChanged = false;
        _summary = Layer switch
        {
            TileLayer { Chunks.Count: > MaxCountedChunks } tiles => $"{tiles.Chunks.Count:N0} chunks",
            TileLayer tiles => TileMapEditor.Tiles(Count(tiles)),
            ObjectLayer objects => objects.Objects.Count == 1 ? "1 object" : $"{objects.Objects.Count:N0} objects",
            _ => ""
        };
        OnPropertyChanged(nameof(Summary));
    }

    private static int Count(TileLayer layer)
    {
        var count = 0;
        foreach (var chunk in layer.ChunkValues)
        {
            foreach (var cell in chunk.EnsureDecoded())
            {
                if (!cell.IsEmpty)
                    count++;
            }
        }

        return count;
    }

    private void Change(string description, LayerSettings settings)
    {
        if (Layer.Map is null)
            return;
        editor.Execute(description, MapEdits.ChangeLayer(Layer, settings), Layer.Map);
    }
}
