using Talesmith.Grids;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>A tile a brush may paint and how likely it is to be picked.</summary>
public readonly record struct TileChoice(TileCell Tile, double Weight = 1.0);

/// <summary>The tiles a painting tool writes: one tile, or several picked at random per cell with weights.</summary>
/// <remarks>
/// The pick depends only on the cell and the seed, so previews match what is painted and repainting a cell gives the same tile.
/// An empty brush erases. Immutable.
/// </remarks>
public sealed class TileBrush
{
    private readonly TileChoice[] _choices;
    private readonly double _totalWeight;

    private TileBrush(TileChoice[] choices, int seed)
    {
        _choices = choices;
        Seed = seed;
        foreach (var choice in choices)
            _totalWeight += Math.Max(0.0, choice.Weight);
    }

    /// <summary>A brush that erases.</summary>
    public static TileBrush Eraser { get; } = new([], 0);

    public IReadOnlyList<TileChoice> Choices => _choices;

    public int Seed { get; }

    public bool IsEraser => _choices.Length == 0;

    /// <summary>The first tile, as shown in brush previews; empty for the eraser.</summary>
    public TileCell PrimaryTile => _choices.Length == 0 ? TileCell.Empty : _choices[0].Tile;

    public static TileBrush Single(TileCell tile) => tile.IsEmpty ? Eraser : new TileBrush([new TileChoice(tile)], 0);

    /// <summary>Creates a random brush; choices with a weight of zero or less are never picked unless all are.</summary>
    public static TileBrush Random(IEnumerable<TileChoice> choices, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return new TileBrush([.. choices], seed);
    }

    /// <summary>Gets the tile to paint at a cell.</summary>
    public TileCell TileFor(GridCoord cell)
    {
        if (_choices.Length <= 1 || _totalWeight <= 0)
            return _choices.Length == 0 ? TileCell.Empty : _choices[0].Tile;

        var target = Sample(cell, Seed) * _totalWeight;
        foreach (var choice in _choices)
        {
            target -= Math.Max(0.0, choice.Weight);
            if (target < 0)
                return choice.Tile;
        }

        return _choices[^1].Tile;
    }

    /// <summary>Gets a copy whose tiles all have the given orientation, as the brush's rotate and flip buttons set it.</summary>
    public TileBrush WithTransform(int rotation, bool flipX, int rotationSteps)
    {
        var choices = new TileChoice[_choices.Length];
        for (var i = 0; i < choices.Length; i++)
            choices[i] = _choices[i] with { Tile = _choices[i].Tile.WithTransform(rotation, flipX, rotationSteps) };
        return new TileBrush(choices, Seed);
    }

    /// <summary>Gets a copy with every tile rotated clockwise by whole steps.</summary>
    public TileBrush Rotate(int clockwiseSteps, int rotationSteps) => Map(t => t.Rotate(clockwiseSteps, rotationSteps));

    public TileBrush FlipHorizontal(int rotationSteps) => Map(t => t.FlipHorizontal(rotationSteps));

    public TileBrush FlipVertical(int rotationSteps) => Map(t => t.FlipVertical(rotationSteps));

    public TileBrush WithSeed(int seed) => new(_choices, seed);

    /// <summary>A uniform sample between 0 and 1 that depends only on the cell and the seed.</summary>
    public static double Sample(GridCoord cell, int seed)
    {
        var h = (uint)(cell.X * 73856093) ^ (uint)(cell.Y * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }

    private TileBrush Map(Func<TileCell, TileCell> transform)
    {
        var choices = new TileChoice[_choices.Length];
        for (var i = 0; i < choices.Length; i++)
            choices[i] = _choices[i] with { Tile = transform(_choices[i].Tile) };
        return new TileBrush(choices, Seed);
    }
}
