using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Grids;
using Talesmith.UI;

namespace Talesmith.Editor.TileMaps.Tools;

/// <summary>The toolbar options of a tile tool, like Hexy's tool options bar: the brush tile and its orientation, size, shape, fill, terrain,
/// stamp, weights, selection and object options.</summary>
public sealed partial class TileToolOptionsViewModel : ObservableObject
{
    private readonly TileMapEditor _editor;
    private bool _syncingWeights;

    public TileToolOptionsViewModel(TileMapEditor editor, TileTool tool)
    {
        _editor = editor;
        Tool = tool;
        Brush.PropertyChanged += OnBrushChanged;
        editor.PropertyChanged += OnEditorChanged;
        editor.SelectionChanged += (_, _) => OnPropertyChanged(nameof(SelectionText));
        editor.MapChanged += (_, change) =>
        {
            if (change.Kind is MapChangeKind.TilesetChanged or MapChangeKind.TilesetAdded or MapChangeKind.TilesetRemoved)
                RefreshTile();
            else if (change.Kind is MapChangeKind.LayerChanged or MapChangeKind.LayerAdded or MapChangeKind.LayerRemoved)
                OnPropertyChanged(nameof(CollisionText));
        };
        RebuildWeights();
    }

    public TileTool Tool { get; }

    public TileBrushSettings Brush => _editor.Brush;

    public bool ShowsTile => Has(TileToolOptions.Tile);

    public bool ShowsSize => Has(TileToolOptions.Size);

    public bool ShowsOrientation => Has(TileToolOptions.Orientation);

    public bool ShowsShapeFill => Has(TileToolOptions.ShapeFill);

    public bool ShowsRange => Has(TileToolOptions.Range);

    public bool ShowsFill => Has(TileToolOptions.Fill);

    public bool ShowsTerrain => Has(TileToolOptions.Terrain);

    public bool ShowsStamp => Has(TileToolOptions.Stamp);

    public bool ShowsWeights => Has(TileToolOptions.Weights);

    public bool ShowsSelection => Has(TileToolOptions.Selection);

    public bool ShowsObjects => Has(TileToolOptions.Objects);

    public bool ShowsCollision => Has(TileToolOptions.Collision);

    public GridKind GridKind => _editor.Map?.Layout.Kind ?? GridKind.HexPointyTop;

    public string TileName
    {
        get
        {
            if (Tileset is not { } tileset)
                return "No tile selected";
            if (Brush.Tiles.Count > 1)
                return $"{Brush.Tiles.Count} tiles at random";
            var id = Brush.PrimaryTile.TileId;
            return tileset.Find(id)?.Name is { Length: > 0 } name ? name : $"Tile {id}";
        }
    }

    public IImage? TileImage => Tileset is { } tileset ? TileArt.Image(tileset, Brush.PrimaryTile.TileId) : null;

    public IBrush TileFill => Tileset is { } tileset ? TileArt.ColorBrush(tileset, Brush.PrimaryTile.TileId) : Brushes.Transparent;

    public double TileAngle => Brush.Rotation * 360.0 / _editor.RotationSteps;

    public double TileScaleX => Brush.FlipX ? -1 : 1;

    public bool IsOriented => Brush.Rotation != 0 || Brush.FlipX;

    public string OrientationText => $"{TileAngle:0}°{(Brush.FlipX ? " · flipped" : "")}";

    /// <summary>The icon of a range of grid steps on the edited grid.</summary>
    public Geometry RangeIcon => GridKind == GridKind.Square ? Icons.Square : Icons.Hexagon;

    /// <summary>0 for a range of grid steps, 1 for a round brush.</summary>
    public int BrushShapeIndex
    {
        get => Brush.Shape == BrushShape.Circle ? 1 : 0;
        set => Brush.Shape = value == 1 ? BrushShape.Circle : BrushShape.Range;
    }

    public string RangeShapeName => GridKind == GridKind.Square ? "Square" : "Hexagon";

    /// <summary>0 for a round circle, 1 for a hexagon or square range.</summary>
    public int CircleShapeIndex
    {
        get => Brush.IsRangeShape ? 1 : 0;
        set => Brush.IsRangeShape = value == 1;
    }

    /// <summary>0 for a contiguous fill, 1 for every matching tile.</summary>
    public int FillScopeIndex
    {
        get => Brush.IsContiguousFill ? 0 : 1;
        set => Brush.IsContiguousFill = value == 0;
    }

    public int FillMatchIndex
    {
        get => (int)Brush.FillMatch;
        set => Brush.FillMatch = (FillMatch)Math.Clamp(value, 0, 2);
    }

    public string TerrainName => Brush.Terrain?.Name ?? "No terrain selected";

    public bool HasTerrain => Brush.Terrain is not null;

    public string StampText => Brush.Stamp is { IsEmpty: false } stamp ? $"{TileMapEditor.Tiles(stamp.Count)} to place" : "Nothing copied";

    public ObservableCollection<WeightedTileViewModel> Weights { get; } = [];

    public int SelectionShapeIndex
    {
        get => (int)Brush.SelectionShape;
        set => Brush.SelectionShape = (CellSelectionShape)Math.Clamp(value, 0, 2);
    }

    public string SelectionText => _editor.Selection.IsEmpty ? "Nothing selected" : $"{_editor.Selection.Count:N0} {(_editor.Selection.Count == 1 ? "cell" : "cells")} selected";

    public int ObjectModeIndex
    {
        get => (int)Brush.ObjectMode;
        set => Brush.ObjectMode = (ObjectToolMode)Math.Clamp(value, 0, 3);
    }

    public string ObjectLayerText => _editor.ActiveObjectLayer is { } layer ? $"On \"{layer.Name}\"" : "Select an object layer";

    public string CollisionText => _editor.ActiveTileLayer is { Role: LayerRole.Collision } active ? $"Painting \"{active.Name}\""
        : _editor.Map?.TileLayers.LastOrDefault(l => l.Role == LayerRole.Collision) is { } layer ? $"Painting \"{layer.Name}\""
        : "A collision layer is added on the first stroke";

    private Tileset? Tileset => Brush.PrimaryTile is { IsEmpty: false } tile ? _editor.Map?.FindTileset(tile.TilesetId) : null;

    [RelayCommand]
    private void RotateClockwise() => Brush.RotateClockwise(_editor.RotationSteps);

    [RelayCommand]
    private void RotateCounterClockwise() => Brush.RotateCounterClockwise(_editor.RotationSteps);

    [RelayCommand]
    private void FlipHorizontal() => Brush.FlipHorizontal(_editor.RotationSteps);

    [RelayCommand]
    private void FlipVertical() => Brush.FlipVertical(_editor.RotationSteps);

    [RelayCommand]
    private void ResetOrientation() => Brush.ResetOrientation();

    [RelayCommand]
    private void RotateStamp(string direction)
    {
        if (Brush.Stamp is { } stamp && _editor.Map is { } map)
            Brush.Stamp = stamp.Rotate(direction == "ccw" ? -1 : 1, map.Layout.Topology);
    }

    [RelayCommand]
    private void FlipStamp(string axis)
    {
        if (Brush.Stamp is { } stamp && _editor.Map is { } map)
            Brush.Stamp = axis == "v" ? stamp.FlipVertical(map.Layout.Topology) : stamp.FlipHorizontal(map.Layout.Topology);
    }

    [RelayCommand]
    private void Reshuffle() => Brush.Seed++;

    [RelayCommand]
    private void Copy() => _editor.Copy();

    [RelayCommand]
    private void Cut() => _editor.Cut();

    [RelayCommand]
    private void DeleteSelection() => _editor.DeleteSelection();

    [RelayCommand]
    private void FillSelection() => _editor.FillSelection();

    [RelayCommand]
    private void SelectAll() => _editor.SelectAll();

    [RelayCommand]
    private void Deselect() => _editor.ClearSelection();

    private bool Has(TileToolOptions option) => (Tool.Options & option) != 0;

    private void OnBrushChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TileBrushSettings.Tiles):
                RefreshTile();
                if (!_syncingWeights)
                    RebuildWeights();
                break;
            case nameof(TileBrushSettings.Rotation) or nameof(TileBrushSettings.FlipX):
                OnPropertyChanged(nameof(TileAngle));
                OnPropertyChanged(nameof(TileScaleX));
                OnPropertyChanged(nameof(OrientationText));
                OnPropertyChanged(nameof(IsOriented));
                break;
            case nameof(TileBrushSettings.Shape):
                OnPropertyChanged(nameof(BrushShapeIndex));
                break;
            case nameof(TileBrushSettings.IsRangeShape):
                OnPropertyChanged(nameof(CircleShapeIndex));
                break;
            case nameof(TileBrushSettings.IsContiguousFill):
                OnPropertyChanged(nameof(FillScopeIndex));
                break;
            case nameof(TileBrushSettings.FillMatch):
                OnPropertyChanged(nameof(FillMatchIndex));
                break;
            case nameof(TileBrushSettings.Terrain):
                OnPropertyChanged(nameof(TerrainName));
                OnPropertyChanged(nameof(HasTerrain));
                break;
            case nameof(TileBrushSettings.Stamp):
                OnPropertyChanged(nameof(StampText));
                break;
            case nameof(TileBrushSettings.SelectionShape):
                OnPropertyChanged(nameof(SelectionShapeIndex));
                break;
            case nameof(TileBrushSettings.ObjectMode):
                OnPropertyChanged(nameof(ObjectModeIndex));
                break;
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TileMapEditor.Target):
                OnPropertyChanged(nameof(GridKind));
                OnPropertyChanged(nameof(RangeShapeName));
                OnPropertyChanged(nameof(RangeIcon));
                RefreshTile();
                RebuildWeights();
                break;
            case nameof(TileMapEditor.ActiveLayer):
                OnPropertyChanged(nameof(ObjectLayerText));
                OnPropertyChanged(nameof(CollisionText));
                break;
        }
    }

    private void RefreshTile()
    {
        OnPropertyChanged(nameof(TileName));
        OnPropertyChanged(nameof(TileImage));
        OnPropertyChanged(nameof(TileFill));
    }

    private void RebuildWeights()
    {
        Weights.Clear();
        if (_editor.Map is not { } map)
            return;
        foreach (var choice in Brush.Tiles)
        {
            if (map.FindTileset(choice.Tile.TilesetId) is { } tileset)
                Weights.Add(new WeightedTileViewModel(this, choice, tileset));
        }
    }

    internal void UpdateWeights()
    {
        _syncingWeights = true;
        Brush.Tiles = [.. Weights.Select(w => new TileChoice(w.Tile, w.Weight))];
        _syncingWeights = false;
    }

    internal void Remove(WeightedTileViewModel item)
    {
        Weights.Remove(item);
        UpdateWeights();
    }
}

/// <summary>A tile of the random brush with its weight.</summary>
public sealed partial class WeightedTileViewModel(TileToolOptionsViewModel owner, TileChoice choice, Tileset tileset) : ObservableObject
{
    private double _weight = choice.Weight;

    public TileCell Tile { get; } = choice.Tile;

    public IImage? Image { get; } = TileArt.Image(tileset, choice.Tile.TileId);

    public IBrush Fill { get; } = TileArt.ColorBrush(tileset, choice.Tile.TileId);

    public string Name { get; } = tileset.Find(choice.Tile.TileId)?.Name is { Length: > 0 } name ? name : $"Tile {choice.Tile.TileId}";

    public double Weight
    {
        get => _weight;
        set
        {
            if (!SetProperty(ref _weight, Math.Max(0, value)))
                return;
            owner.UpdateWeights();
        }
    }

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}
