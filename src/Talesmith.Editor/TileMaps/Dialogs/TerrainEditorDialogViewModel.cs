using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Grids;
using Talesmith.UI;

namespace Talesmith.Editor.TileMaps.Dialogs;

/// <summary>A tile offered for selection in the tile map dialogs.</summary>
public sealed class TileOption(Tileset tileset, int id)
{
    public int Id { get; } = id;

    public string Name { get; } = tileset.Find(id)?.Name is { Length: > 0 } name ? name : $"Tile {id}";

    public IImage? Image { get; } = TileArt.Image(tileset, id);

    /// <summary>The flat color shown when the tile has no artwork.</summary>
    public IBrush Brush { get; } = TileArt.ColorBrush(tileset, id);

    public bool HasImage => Image is not null;

    public static IReadOnlyList<TileOption> All(Tileset tileset) => [.. Enumerable.Range(0, tileset.TileCount).Select(id => new TileOption(tileset, id))];
}

/// <summary>An output tile of a rule and its relative probability.</summary>
public sealed partial class WeightedTileOption(TileOption tile, double weight) : ObservableObject
{
    public TileOption Tile { get; } = tile;

    [ObservableProperty]
    private double _weight = weight;
}

/// <summary>An editable <see cref="AutoTileRule"/>.</summary>
public sealed partial class TerrainRuleViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _pattern;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private bool _matchRotations;

    public TerrainRuleViewModel(string pattern)
    {
        _pattern = pattern;
        Tiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Summary));
    }

    public ObservableCollection<WeightedTileOption> Tiles { get; } = [];

    /// <summary>A compact description such as "+-****  ↻  ·  2 tiles".</summary>
    public string Summary => $"{Pattern}{(MatchRotations ? "  ↻" : "")}  ·  {(Tiles.Count == 1 ? "1 tile" : $"{Tiles.Count} tiles")}";

    public TerrainRuleViewModel Clone()
    {
        var copy = new TerrainRuleViewModel(Pattern) { MatchRotations = MatchRotations };
        foreach (var tile in Tiles)
            copy.Tiles.Add(new WeightedTileOption(tile.Tile, tile.Weight));
        return copy;
    }

    [RelayCommand]
    private void RemoveTile(WeightedTileOption tile) => Tiles.Remove(tile);
}

/// <summary>Edits a terrain's name, base tile and ordered auto-tile rules, with a neighbor pattern editor for the map's grid, like Hexy's terrain
/// editor.</summary>
public sealed partial class TerrainEditorDialogViewModel : TileMapDialogViewModel
{
    private readonly Tileset _tileset;
    private readonly Terrain? _existing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private TileOption? _baseTile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRule))]
    [NotifyCanExecuteChangedFor(nameof(RemoveRuleCommand), nameof(DuplicateRuleCommand), nameof(MoveRuleUpCommand), nameof(MoveRuleDownCommand))]
    private TerrainRuleViewModel? _selectedRule;

    /// <summary>0 when clicking a tile sets the base tile, 1 when it adds an output to the selected rule.</summary>
    [ObservableProperty]
    private int _pickModeIndex;

    public TerrainEditorDialogViewModel(Tileset tileset, Terrain? existing, IGridLayout layout)
    {
        _tileset = tileset;
        _existing = existing;
        Layout = layout;
        Tiles = TileOption.All(tileset);
        _name = existing?.Name ?? "New terrain";
        _baseTile = Tiles.FirstOrDefault(t => t.Id == (existing?.BaseTileId ?? 0));
        foreach (var rule in existing?.Rules ?? [])
        {
            var model = new TerrainRuleViewModel(rule.Pattern) { MatchRotations = rule.MatchRotations };
            foreach (var tile in rule.Tiles)
            {
                if (Tiles.FirstOrDefault(t => t.Id == tile.TileId) is { } option)
                    model.Tiles.Add(new WeightedTileOption(option, tile.Weight));
            }

            Rules.Add(Track(model));
        }

        Rules.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasRules));
            SaveCommand.NotifyCanExecuteChanged();
        };
        _selectedRule = Rules.FirstOrDefault();
    }

    /// <summary>The map's grid, which decides the cell shape and the number of neighbors in a pattern.</summary>
    public IGridLayout Layout { get; }

    public Geometry CellIcon => Layout.Kind == GridKind.Square ? Icons.Square : Icons.Hexagon;

    public string TilesetName => _tileset.Name;

    public string GridName => Layout.Kind switch
    {
        GridKind.HexFlatTop => "Flat-top hexagons · 6 neighbors",
        GridKind.Square => "Squares · 4 neighbors",
        _ => "Pointy-top hexagons · 6 neighbors"
    };

    public IReadOnlyList<TileOption> Tiles { get; }

    public ObservableCollection<TerrainRuleViewModel> Rules { get; } = [];

    public bool HasRules => Rules.Count > 0;

    public bool HasSelectedRule => SelectedRule is not null;

    public string? ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
                return "Enter a terrain name.";
            if (BaseTile is null)
                return "Choose a base tile.";
            var index = Rules.ToList().FindIndex(r => r.Tiles.Count == 0);
            return index >= 0 ? $"Rule {index + 1} needs at least one output tile." : null;
        }
    }

    [RelayCommand]
    private void PickTile(TileOption tile)
    {
        if (PickModeIndex == 0 || SelectedRule is null)
        {
            BaseTile = tile;
            return;
        }

        if (SelectedRule.Tiles.All(t => t.Tile != tile))
            SelectedRule.Tiles.Add(new WeightedTileOption(tile, 1));
    }

    [RelayCommand]
    private void AddRule()
    {
        var rule = Track(new TerrainRuleViewModel(new string('*', Layout.Topology.NeighborCount)));
        Rules.Add(rule);
        SelectedRule = rule;
        PickModeIndex = 1;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void RemoveRule()
    {
        var index = Rules.IndexOf(SelectedRule!);
        Rules.RemoveAt(index);
        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Min(index, Rules.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void DuplicateRule()
    {
        var copy = Track(SelectedRule!.Clone());
        Rules.Insert(Rules.IndexOf(SelectedRule) + 1, copy);
        SelectedRule = copy;
    }

    [RelayCommand(CanExecute = nameof(CanMoveRuleUp))]
    private void MoveRuleUp() => MoveSelected(-1);

    [RelayCommand(CanExecute = nameof(CanMoveRuleDown))]
    private void MoveRuleDown() => MoveSelected(1);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var rules = Rules.Select(r => new AutoTileRule(r.Pattern, [.. r.Tiles.Select(t => new WeightedTile(t.Tile.Id, Math.Max(0, t.Weight)))], r.MatchRotations))
            .ToList();
        var terrain = _existing is null
            ? new Terrain(Name.Trim(), _tileset.Id, BaseTile!.Id, rules)
            : new Terrain(Name.Trim(), _tileset.Id, BaseTile!.Id, rules, _existing.Id) { FormatData = _existing.FormatData };
        Close(terrain);
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    private bool CanSave() => ValidationMessage is null;

    private bool CanMoveRuleUp() => SelectedRule is not null && Rules.IndexOf(SelectedRule) > 0;

    private bool CanMoveRuleDown() => SelectedRule is not null && Rules.IndexOf(SelectedRule) < Rules.Count - 1;

    private TerrainRuleViewModel Track(TerrainRuleViewModel rule)
    {
        rule.Tiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ValidationMessage));
            SaveCommand.NotifyCanExecuteChanged();
        };
        return rule;
    }

    private void MoveSelected(int delta)
    {
        var rule = SelectedRule!;
        var index = Rules.IndexOf(rule);
        Rules.Move(index, index + delta);
        SelectedRule = rule;
        MoveRuleUpCommand.NotifyCanExecuteChanged();
        MoveRuleDownCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedRuleChanged(TerrainRuleViewModel? value)
    {
        MoveRuleUpCommand.NotifyCanExecuteChanged();
        MoveRuleDownCommand.NotifyCanExecuteChanged();
    }
}
