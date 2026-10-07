using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Documents;
using Talesmith.Editor.TileMaps.Dialogs;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>The Tile Map panel: which map of the scene is edited and whether it is saved, its layers, tileset palette and terrains, and the
/// properties of the map and the selected object.</summary>
public sealed partial class TileMapPanelViewModel : ObservableObject
{
    private readonly TileMapEditor _editor;
    private readonly TileMapDocuments _maps;
    private readonly TileMapDialogs _dialogs;
    private readonly DispatcherTimer _refreshTargets;
    private bool _syncing;

    [ObservableProperty]
    private MapTarget? _selectedTarget;

    public TileMapPanelViewModel(TileMapEditor editor, TileMapDocuments maps, TileMapDialogs dialogs, IDialogService dialogService, ToolManager tools,
        ISceneDocumentService documents, IEditWorld world)
    {
        _editor = editor;
        _maps = maps;
        _dialogs = dialogs;
        Layers = new LayersViewModel(editor, dialogService);
        Tilesets = new TilesetsViewModel(editor, dialogs, dialogService, tools);
        MapProperties = new MapPropertiesViewModel(editor);
        ObjectProperties = new ObjectPropertiesViewModel(editor);
        _refreshTargets = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshTargets.Tick += (_, _) => RefreshTargets();
        editor.PropertyChanged += OnEditorChanged;
        maps.StateChanged += (_, _) => OnPropertyChanged(nameof(IsDirty));
        documents.ActiveChanged += (_, _) => QueueRefresh();
        world.Changed += (_, _) => QueueRefresh();
        RefreshTargets();
    }

    public TileMapEditor Editor => _editor;

    public LayersViewModel Layers { get; }

    public TilesetsViewModel Tilesets { get; }

    public MapPropertiesViewModel MapProperties { get; }

    public ObjectPropertiesViewModel ObjectProperties { get; }

    /// <summary>The maps shown by entities of the open scene.</summary>
    public ObservableCollection<MapTarget> Targets { get; } = [];

    public bool HasMap => _editor.Map is not null;

    public bool HasTargets => Targets.Count > 0;

    public bool IsDirty => _editor.Map is { } map && _maps.IsDirty(map);

    public GridKind GridKind => _editor.Map?.Layout.Kind ?? GridKind.HexPointyTop;

    public Geometry GridIcon => GridKind == GridKind.Square ? Icons.Square : Icons.Hexagon;

    public string GridText => _editor.Map is not { } map ? ""
        : string.Create(CultureInfo.CurrentCulture, $"{GridName(map.Layout.Kind)} · {map.Layout.CellSize.X:0.#} × {map.Layout.CellSize.Y:0.#}");

    public string FileText => _editor.Map?.Path ?? "";

    public string EmptyHint => HasTargets
        ? "Select an entity with a Tile Map Renderer, or pick its map above, to paint tiles."
        : "This scene shows no tile maps yet. Create one to start painting.";

    [RelayCommand]
    private Task SaveAsync() => _editor.Map is { } map ? _maps.SaveAsync(map) : Task.CompletedTask;

    [RelayCommand]
    private Task NewMapAsync() => _dialogs.NewMapAsync();

    [RelayCommand]
    private void CloseMap() => _editor.Close();

    private static string GridName(GridKind kind) => kind switch
    {
        GridKind.HexFlatTop => "Flat hex",
        GridKind.Square => "Rectangle",
        _ => "Pointy hex"
    };

    partial void OnSelectedTargetChanged(MapTarget? value)
    {
        if (!_syncing && value is not null)
            _editor.Edit(value.EntityId);
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TileMapEditor.Target))
            return;
        RefreshTargets();
        OnPropertyChanged(nameof(HasMap));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(GridKind));
        OnPropertyChanged(nameof(GridIcon));
        OnPropertyChanged(nameof(GridText));
        OnPropertyChanged(nameof(FileText));
    }

    private void QueueRefresh()
    {
        _refreshTargets.Stop();
        _refreshTargets.Start();
    }

    private void RefreshTargets()
    {
        _refreshTargets.Stop();
        var targets = _editor.FindTargets();
        _syncing = true;
        if (!targets.SequenceEqual(Targets))
        {
            Targets.Clear();
            foreach (var target in targets)
                Targets.Add(target);
        }

        SelectedTarget = Targets.FirstOrDefault(t => t.EntityId == _editor.Target?.EntityId);
        _syncing = false;
        OnPropertyChanged(nameof(HasTargets));
        OnPropertyChanged(nameof(EmptyHint));
    }
}
