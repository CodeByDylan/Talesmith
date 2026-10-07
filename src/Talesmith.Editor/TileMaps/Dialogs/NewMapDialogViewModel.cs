using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Projects;
using Talesmith.Grids;
using Talesmith.Rendering;
using MathColor = Talesmith.Mathematics.Color;

namespace Talesmith.Editor.TileMaps.Dialogs;

/// <summary>What a new map is made of.</summary>
/// <param name="AssetPath">Where the .hexy file is written, relative to the asset root.</param>
/// <param name="ChunkShift">Log2 of the chunk size.</param>
/// <param name="IncludePalette">Whether the map starts with Hexy's terrain color palette.</param>
public sealed record NewMapOptions(string AssetPath, GridKind Kind, float CellWidth, float CellHeight, int ChunkShift, bool IncludePalette,
    int RenderLayer = RenderLayers.Terrain);

/// <summary>Hexy's terrain color palette, which new maps can start with.</summary>
public static class StarterPalette
{
    public static IReadOnlyList<(string Name, MathColor Color)> Colors { get; } =
    [
        ("Deep Water", new(0x1E, 0x3A, 0x8A)),
        ("Water", new(0x25, 0x63, 0xEB)),
        ("Shallows", new(0x38, 0xBD, 0xF8)),
        ("Sand", new(0xFD, 0xE6, 0x8A)),
        ("Grass", new(0x4A, 0xDE, 0x80)),
        ("Meadow", new(0x84, 0xCC, 0x16)),
        ("Forest", new(0x15, 0x80, 0x3D)),
        ("Jungle", new(0x06, 0x5F, 0x46)),
        ("Hills", new(0xA1, 0x62, 0x07)),
        ("Mountain", new(0x78, 0x71, 0x6C)),
        ("Peak", new(0xE7, 0xE5, 0xE4)),
        ("Snow", new(0xF8, 0xFA, 0xFC)),
        ("Desert", new(0xF5, 0x9E, 0x0B)),
        ("Swamp", new(0x4D, 0x7C, 0x0F)),
        ("Lava", new(0xDC, 0x26, 0x26)),
        ("Road", new(0x57, 0x53, 0x4E))
    ];
}

/// <summary>Collects the grid, cell size, storage and file of a new map, like Hexy's New map dialog.</summary>
public sealed partial class NewMapDialogViewModel : TileMapDialogViewModel
{
    private const double MinimumSize = 4;
    private const double MaximumSize = 1024;

    private readonly IProjectService _project;

    /// <summary>0 for pointy-top hexagons, 1 for flat-top hexagons and 2 for squares and rectangles.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind), nameof(SizeLabel), nameof(RegularLabel), nameof(SizeSummary))]
    private int _gridIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeSummary))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private double _cellWidth = 128;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeSummary))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private double _cellHeight;

    [ObservableProperty]
    private bool _isRegular = true;

    [ObservableProperty]
    private int _chunkSize = 32;

    [ObservableProperty]
    private bool _includePalette = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssetPath), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AssetPath), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _folder = "maps";

    public NewMapDialogViewModel(IProjectService project)
    {
        _project = project;
        _name = UniqueName("new-map");
        UpdateRegularHeight();
    }

    public IReadOnlyList<int> ChunkSizes { get; } = [16, 32, 64, 128];

    public GridKind Kind => GridIndex switch
    {
        1 => GridKind.HexFlatTop,
        2 => GridKind.Square,
        _ => GridKind.HexPointyTop
    };

    public string SizeLabel => Kind == GridKind.Square ? "Tile size" : "Hex size";

    public string RegularLabel => Kind == GridKind.Square ? "Square" : "Regular hexagon";

    public string SizeSummary => $"{CellWidth:0.##} × {CellHeight:0.##} px per {(Kind == GridKind.Square ? "tile" : "hex")}";

    /// <summary>The map's file relative to the asset root.</summary>
    public string AssetPath => Talesmith.Assets.AssetPath.Combine(Folder.Trim().Trim('/'), Name.Trim() + ".hexy");

    public string? ValidationMessage
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name) || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "Enter a file name for the map.";
            if (CellWidth is < MinimumSize or > MaximumSize || CellHeight is < MinimumSize or > MaximumSize)
                return $"Cells must be between {MinimumSize} and {MaximumSize} pixels.";
            return File.Exists(_project.Project.ToAbsolutePath(AssetPath)) ? $"{AssetPath} already exists." : null;
        }
    }

    partial void OnGridIndexChanged(int value) => UpdateRegularHeight();

    partial void OnCellWidthChanged(double value) => UpdateRegularHeight();

    partial void OnIsRegularChanged(bool value) => UpdateRegularHeight();

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() =>
        Close(new NewMapOptions(AssetPath, Kind, (float)CellWidth, (float)CellHeight, BitOperations.Log2((uint)ChunkSize), IncludePalette));

    [RelayCommand]
    private void Cancel() => Close(null);

    private bool CanCreate() => ValidationMessage is null;

    private void UpdateRegularHeight()
    {
        if (!IsRegular)
            return;
        var ratio = Kind switch
        {
            GridKind.HexPointyTop => 2 / Math.Sqrt(3),
            GridKind.HexFlatTop => Math.Sqrt(3) / 2,
            _ => 1.0
        };
        CellHeight = Math.Round(CellWidth * ratio, 1);
    }

    private string UniqueName(string baseName)
    {
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? baseName : $"{baseName}-{i}";
            if (!File.Exists(_project.Project.ToAbsolutePath(Talesmith.Assets.AssetPath.Combine("maps", name + ".hexy"))))
                return name;
        }
    }
}
