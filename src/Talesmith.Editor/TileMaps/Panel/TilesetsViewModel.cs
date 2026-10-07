using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Controls;
using Talesmith.Editor.TileMaps.Dialogs;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.Runtime.Maps;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>A tileset in the tileset picker.</summary>
public sealed record TilesetItem(Tileset Tileset)
{
    public string Name => Tileset.Name;

    public string Summary => Tileset.IsColorTileset
        ? $"{Tileset.TileCount} {(Tileset.TileCount == 1 ? "color" : "colors")}"
        : $"{Tileset.TileCount:N0} tiles · {Tileset.TileWidth}×{Tileset.TileHeight}";
}

/// <summary>The edited map's tilesets and terrains: the palette that fills the brush, Hexy's tileset operations and the terrains of the selected
/// tileset.</summary>
public sealed partial class TilesetsViewModel : ObservableObject
{
    private readonly TileMapEditor _editor;
    private readonly TileMapDialogs _dialogs;
    private readonly IDialogService _confirm;
    private readonly ToolManager _tools;
    private TileMap? _map;
    private bool _syncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tileset), nameof(IsColorTileset), nameof(HasSelectedTileset))]
    [NotifyCanExecuteChangedFor(nameof(RemoveTilesetCommand), nameof(AddColorTileCommand), nameof(AddTerrainCommand), nameof(EditTileCommand))]
    private TilesetItem? _selectedTileset;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditTerrainCommand), nameof(RemoveTerrainCommand))]
    private Terrain? _selectedTerrain;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private double _tileSize = 44;

    [ObservableProperty]
    private IReadOnlyCollection<int> _selectedIds = [];

    [ObservableProperty]
    private long _revision;

    /// <summary>0 shows the tiles, 1 the terrains.</summary>
    [ObservableProperty]
    private int _tabIndex;

    public TilesetsViewModel(TileMapEditor editor, TileMapDialogs dialogs, IDialogService confirm, ToolManager tools)
    {
        _editor = editor;
        _dialogs = dialogs;
        _confirm = confirm;
        _tools = tools;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TileMapEditor.Target) && !ReferenceEquals(editor.Map, _map))
                Rebuild();
        };
        editor.MapChanged += OnMapChanged;
        editor.Brush.PropertyChanged += OnBrushChanged;
        dialogs.TilesetImported += (_, tileset) => Select(tileset);
        Rebuild();
    }

    public ObservableCollection<TilesetItem> Tilesets { get; } = [];

    /// <summary>The terrains of the selected tileset.</summary>
    public ObservableCollection<Terrain> Terrains { get; } = [];

    public Tileset? Tileset => SelectedTileset?.Tileset;

    public bool HasTilesets => Tilesets.Count > 0;

    public bool HasSelectedTileset => SelectedTileset is not null;

    public bool HasTerrains => Terrains.Count > 0;

    public bool IsColorTileset => Tileset?.IsColorTileset == true;

    public GridKind GridKind => _editor.Map?.Layout.Kind ?? GridKind.HexPointyTop;

    /// <summary>Applies tiles picked in the palette to the brush and chooses a tool that paints them.</summary>
    public void Pick(TilesPickedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (Tileset is not { } tileset || _editor.Map is not { } map)
            return;
        var brush = _editor.Brush;
        _syncing = true;
        if (e.IsRectangle && e.Ids.Count > 1)
        {
            brush.Stamp = RectangleStamp(tileset, map.Layout.Topology, e.Ids, e.Columns);
            brush.Select([new TileChoice(new TileCell(tileset.Id, e.Ids.First(id => id >= 0)))]);
            SelectTool(StampTool.ToolId);
        }
        else if (e.IsToggle)
        {
            var tile = new TileCell(tileset.Id, e.Ids[0]);
            var tiles = brush.Tiles.Where(t => t.Tile.TilesetId == tileset.Id).ToList();
            if (tiles.RemoveAll(t => t.Tile == tile) == 0)
                tiles.Add(new TileChoice(tile));
            if (tiles.Count > 0)
                brush.Select(tiles);
            brush.Stamp = null;
            if (tiles.Count > 1 && _tools.ActiveTool is not (BrushTool or RandomBrushTool or ShapeTool or FillTool))
                SelectTool("tile.random");
        }
        else
        {
            brush.Select([new TileChoice(new TileCell(tileset.Id, e.Ids[0]))]);
            brush.Stamp = null;
            if (_tools.ActiveTool is not TileTool { Options: var options } || (options & (TileToolOptions.Tile | TileToolOptions.Weights)) == 0)
                SelectTool("tile.brush");
        }

        _syncing = false;
        SyncSelection();
    }

    /// <summary>Edits a tile of the selected tileset, as double-clicking it in the palette does.</summary>
    public Task EditTileAsync(int tileId) => Tileset is { } tileset ? _dialogs.EditTileAsync(tileset, tileId) : Task.CompletedTask;

    [RelayCommand]
    private Task ImportTilesetAsync() => _dialogs.ImportTilesetAsync();

    [RelayCommand]
    private void AddColorTileset()
    {
        if (_editor.Map is not { } map)
            return;
        var tileset = Tileset.FromColors(UniqueName(map, "Colors"), [("Color 1", new Mathematics.Color(0x63, 0x66, 0xF1))],
            (int)Math.Round(map.Layout.CellSize.X), (int)Math.Round(map.Layout.CellSize.Y));
        _editor.Execute($"Add tileset \"{tileset.Name}\"", MapEdits.InsertTileset(tileset, map.Tilesets.Count));
        Select(tileset);
    }

    [RelayCommand(CanExecute = nameof(CanAddColorTile))]
    private void AddColorTile()
    {
        if (Tileset is not { IsColorTileset: true } tileset)
            return;
        var id = tileset.TileCount;
        var info = new TileInfo(id, $"Color {id + 1}", TileGeometry.FallbackColor(id), [], PropertySet.Empty);
        var settings = TilesetSettings.Of(tileset) with { TileCount = id + 1, Columns = Math.Max(1, Math.Min(id + 1, 8)) };
        _editor.Execute("Add color tile", new CompositeEdit([MapEdits.ChangeTileset(tileset, settings), MapEdits.ChangeTile(tileset, id, info)]));
        _editor.Brush.Select([new TileChoice(new TileCell(tileset.Id, id))]);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTileset))]
    private async Task EditTileAsync()
    {
        if (Tileset is { } tileset && _editor.Brush.PrimaryTile is { IsEmpty: false } tile && tile.TilesetId == tileset.Id)
            await _dialogs.EditTileAsync(tileset, tile.TileId);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTileset))]
    private async Task RemoveTilesetAsync()
    {
        if (Tileset is not { } tileset || _editor.Map is not { } map)
            return;
        if (!await _confirm.ConfirmAsync("Remove tileset?",
                $"\"{tileset.Name}\", its terrains and every placed tile that uses it will be removed. You can undo this.", "Remove", isDestructive: true))
            return;
        _editor.Execute($"Remove tileset \"{tileset.Name}\"", MapEdits.RemoveTileset(map, tileset));
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTileset))]
    private async Task AddTerrainAsync()
    {
        if (Tileset is not { } tileset)
            return;
        if (await _dialogs.EditTerrainAsync(tileset, null) is { } terrain)
            SelectedTerrain = Terrains.FirstOrDefault(t => t.Id == terrain.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTerrain))]
    private async Task EditTerrainAsync()
    {
        if (SelectedTerrain is not { } terrain || _editor.Map?.FindTileset(terrain.TilesetId) is not { } tileset)
            return;
        if (await _dialogs.EditTerrainAsync(tileset, terrain) is { } updated)
            SelectedTerrain = Terrains.FirstOrDefault(t => t.Id == updated.Id);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTerrain))]
    private void RemoveTerrain()
    {
        if (SelectedTerrain is { } terrain)
            _editor.Execute($"Remove terrain \"{terrain.Name}\"", MapEdits.RemoveTerrain(terrain));
    }

    private bool HasSelectedTerrain() => SelectedTerrain is not null;

    private bool CanAddColorTile() => IsColorTileset;

    partial void OnSelectedTilesetChanged(TilesetItem? value)
    {
        RebuildTerrains();
        SyncSelection();
    }

    partial void OnSelectedTerrainChanged(Terrain? value)
    {
        if (_syncing || value is null)
            return;
        _editor.Brush.Terrain = value;
        SelectTool("tile.terrain");
    }

    private void Select(Tileset tileset) => SelectedTileset = Tilesets.FirstOrDefault(t => ReferenceEquals(t.Tileset, tileset)) ?? SelectedTileset;

    private void SelectTool(string id)
    {
        if (_tools.ActiveTool.Id != id)
            _tools.Select(id);
    }

    private void Rebuild()
    {
        _map = _editor.Map;
        var previous = Tileset;
        Tilesets.Clear();
        foreach (var tileset in _map?.Tilesets ?? [])
            Tilesets.Add(new TilesetItem(tileset));
        var brushTileset = _editor.Brush.PrimaryTile is { IsEmpty: false } tile ? _map?.FindTileset(tile.TilesetId) : null;
        SelectedTileset = Tilesets.FirstOrDefault(t => ReferenceEquals(t.Tileset, previous))
                          ?? Tilesets.FirstOrDefault(t => ReferenceEquals(t.Tileset, brushTileset))
                          ?? Tilesets.FirstOrDefault();
        OnPropertyChanged(nameof(HasTilesets));
        OnPropertyChanged(nameof(GridKind));
        RebuildTerrains();
        Revision++;
    }

    private void RebuildTerrains()
    {
        _syncing = true;
        var selected = SelectedTerrain;
        Terrains.Clear();
        foreach (var terrain in _map?.Terrains ?? [])
        {
            if (terrain.TilesetId == Tileset?.Id)
                Terrains.Add(terrain);
        }

        SelectedTerrain = Terrains.FirstOrDefault(t => t.Id == (selected ?? _editor.Brush.Terrain)?.Id);
        _syncing = false;
        OnPropertyChanged(nameof(HasTerrains));
    }

    private void OnMapChanged(object? sender, MapChange change)
    {
        switch (change.Kind)
        {
            case MapChangeKind.TilesetAdded or MapChangeKind.TilesetRemoved:
                Rebuild();
                break;
            case MapChangeKind.TilesetChanged:
                if (Tilesets.FirstOrDefault(t => ReferenceEquals(t.Tileset, change.Tileset)) is { } item)
                    Tilesets[Tilesets.IndexOf(item)] = new TilesetItem(item.Tileset);
                if (ReferenceEquals(change.Tileset, Tileset) && SelectedTileset?.Tileset != change.Tileset)
                    Select(change.Tileset!);
                Revision++;
                break;
            case MapChangeKind.Terrains:
                RebuildTerrains();
                break;
        }
    }

    private void OnBrushChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TileBrushSettings.Tiles) when !_syncing:
                if (_editor.Brush.PrimaryTile is { IsEmpty: false } tile && Tileset?.Id != tile.TilesetId && _map?.FindTileset(tile.TilesetId) is { } tileset)
                    Select(tileset);
                SyncSelection();
                break;
            case nameof(TileBrushSettings.Terrain) when !_syncing:
                _syncing = true;
                SelectedTerrain = Terrains.FirstOrDefault(t => ReferenceEquals(t, _editor.Brush.Terrain));
                _syncing = false;
                break;
        }
    }

    private void SyncSelection()
    {
        var id = Tileset?.Id;
        SelectedIds = id is null ? [] : _editor.Brush.Tiles.Where(t => t.Tile.TilesetId == id).Select(t => t.Tile.TileId).ToHashSet();
    }

    /// <summary>A stamp of an atlas rectangle, laid out in the map's screen-aligned rows and columns around its center.</summary>
    private static TileStamp RectangleStamp(Tileset tileset, GridTopology topology, IReadOnlyList<int> ids, int columns)
    {
        var rows = ids.Count / columns;
        var originColumn = columns / 2;
        var originRow = rows / 2 & ~1;
        var cells = new List<PlacedTile>(ids.Count);
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] < 0)
                continue;
            var offset = topology.FromOffset(new GridCoord(i % columns - originColumn, i / columns - originRow));
            cells.Add(new PlacedTile(offset, new TileCell(tileset.Id, ids[i])));
        }

        return new TileStamp(cells);
    }

    private static string UniqueName(TileMap map, string baseName)
    {
        var names = map.Tilesets.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;
        for (var i = 2; ; i++)
        {
            if (!names.Contains($"{baseName} {i}"))
                return $"{baseName} {i}";
        }
    }
}
