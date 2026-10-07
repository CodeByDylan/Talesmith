using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps;

/// <summary>How the tile selection tool picks cells.</summary>
public enum CellSelectionShape
{
    Rectangle,
    Lasso,

    /// <summary>Connected cells matching the clicked tile, like a magic wand.</summary>
    Wand
}

/// <summary>What the object tool does on a click.</summary>
public enum ObjectToolMode
{
    /// <summary>Selects, moves and deletes objects.</summary>
    Select,
    Point,
    Polygon,

    /// <summary>Places the brush tile as an image object.</summary>
    Tile
}

/// <summary>What the tile tools paint with: the tiles and their orientation, brush size and shape, shape, fill and terrain options and the
/// stamp, shared by every tool like Hexy's brush.</summary>
public sealed partial class TileBrushSettings : ObservableObject
{
    private TileBrush? _brush;
    private int _brushSteps;

    /// <summary>The tiles to paint, without orientation; several are picked per cell by weight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryTile))]
    private IReadOnlyList<TileChoice> _tiles = [];

    /// <summary>The clockwise rotation in grid steps applied to every painted tile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryTile))]
    private int _rotation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryTile))]
    private bool _flipX;

    /// <summary>The brush size in cells; 1 paints a single cell.</summary>
    [ObservableProperty]
    private int _size = 1;

    [ObservableProperty]
    private BrushShape _shape = BrushShape.Range;

    /// <summary>Whether shape tools draw filled shapes instead of outlines.</summary>
    [ObservableProperty]
    private bool _isShapeFilled = true;

    /// <summary>Whether the circle tool draws a range of grid steps, a hexagon on hex grids and a square on square grids, instead of a round circle.</summary>
    [ObservableProperty]
    private bool _isRangeShape;

    /// <summary>Whether the fill tool fills the connected region; otherwise every matching cell of the layer.</summary>
    [ObservableProperty]
    private bool _isContiguousFill = true;

    [ObservableProperty]
    private FillMatch _fillMatch = FillMatch.SameTile;

    /// <summary>The most cells a fill or magic wand collects.</summary>
    [ObservableProperty]
    private int _fillLimit = TileFill.DefaultLimit;

    /// <summary>The terrain the terrain tool paints.</summary>
    [ObservableProperty]
    private Terrain? _terrain;

    /// <summary>The pattern the stamp tool places: copied tiles or a rectangle picked in the palette.</summary>
    [ObservableProperty]
    private TileStamp? _stamp;

    /// <summary>The seed of random picks, so painting with several tiles can be varied.</summary>
    [ObservableProperty]
    private int _seed;

    /// <summary>How the selection tool picks cells.</summary>
    [ObservableProperty]
    private CellSelectionShape _selectionShape;

    /// <summary>What the object tool does on a click.</summary>
    [ObservableProperty]
    private ObjectToolMode _objectMode;

    /// <summary>The first tile with the brush orientation, or empty; <paramref name="rotationSteps"/> is the map's.</summary>
    public TileCell PrimaryTileFor(int rotationSteps) => Tiles.Count == 0 ? TileCell.Empty : Tiles[0].Tile.WithTransform(Rotation, FlipX, rotationSteps);

    /// <summary>The first tile without orientation, or empty.</summary>
    public TileCell PrimaryTile => Tiles.Count == 0 ? TileCell.Empty : Tiles[0].Tile;

    /// <summary>The brush to paint with on a grid with <paramref name="rotationSteps"/> steps per turn; cached until the settings change.</summary>
    public TileBrush CreateBrush(int rotationSteps)
    {
        if (_brush is not null && _brushSteps == rotationSteps)
            return _brush;
        _brushSteps = rotationSteps;
        _brush = Tiles.Count == 0 ? TileBrush.Eraser : TileBrush.Random(Tiles, Seed).WithTransform(Rotation, FlipX, rotationSteps);
        return _brush;
    }

    /// <summary>Paints one tile, keeping its orientation as the brush orientation, as picking does.</summary>
    public void Pick(TileCell tile)
    {
        if (tile.IsEmpty)
            return;
        Tiles = [new TileChoice(tile.WithoutTransform)];
        Rotation = tile.Rotation;
        FlipX = tile.FlipX;
    }

    /// <summary>Sets the tiles to paint, without changing the orientation.</summary>
    public void Select(IReadOnlyList<TileChoice> tiles) => Tiles = tiles;

    public void RotateClockwise(int rotationSteps) => Orient(TileSample(rotationSteps).Rotate(1, rotationSteps));

    public void RotateCounterClockwise(int rotationSteps) => Orient(TileSample(rotationSteps).Rotate(-1, rotationSteps));

    public void FlipHorizontal(int rotationSteps) => Orient(TileSample(rotationSteps).FlipHorizontal(rotationSteps));

    public void FlipVertical(int rotationSteps) => Orient(TileSample(rotationSteps).FlipVertical(rotationSteps));

    public void ResetOrientation()
    {
        Rotation = 0;
        FlipX = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Tiles) or nameof(Rotation) or nameof(FlipX) or nameof(Seed))
            _brush = null;
        base.OnPropertyChanged(e);
    }

    partial void OnSizeChanged(int value)
    {
        if (value is < 1 or > GridBrush.MaxSize)
            Size = Math.Clamp(value, 1, GridBrush.MaxSize);
    }

    partial void OnFillLimitChanged(int value)
    {
        if (value < 1)
            FillLimit = 1;
    }

    private TileCell TileSample(int rotationSteps) => new TileCell(1, 0).WithTransform(Rotation, FlipX, rotationSteps);

    private void Orient(TileCell sample)
    {
        Rotation = sample.Rotation;
        FlipX = sample.FlipX;
    }
}
