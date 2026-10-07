namespace Talesmith.Assets.Maps;

/// <summary>A tile candidate of an <see cref="AutoTileRule"/>, chosen with probability proportional to <see cref="Weight"/>.</summary>
public readonly record struct WeightedTile(int TileId, double Weight = 1.0);

/// <summary>Chooses a tile for a terrain cell from which of its edge neighbors share the terrain, as in the Hexy editor.</summary>
/// <remarks>
/// A pattern has one character per neighbor in <see cref="Grids.GridTopology.Directions"/> order: six on hex grids (E, NE, NW, W,
/// SW, SE) and four on square grids (E, N, W, S). <c>+</c> means the neighbor has the same terrain, <c>-</c> that it does not, and
/// <c>*</c> that it does not matter. Immutable.
/// </remarks>
public sealed class AutoTileRule
{
    private const int MaxNeighbors = 8;

    /// <exception cref="FormatException">The pattern is empty, too long or contains other characters.</exception>
    /// <exception cref="ArgumentException">There are no tiles.</exception>
    public AutoTileRule(string pattern, IReadOnlyList<WeightedTile> tiles, bool matchRotations = false)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count == 0)
            throw new ArgumentException("A rule needs at least one output tile.", nameof(tiles));

        (RequiredMask, ForbiddenMask) = ParsePattern(pattern);
        NeighborCount = pattern.Length;
        Tiles = tiles;
        MatchRotations = matchRotations;
        foreach (var tile in tiles)
            TotalWeight += Math.Max(0.0, tile.Weight);
    }

    /// <summary>The number of neighbors the pattern describes, which must match the grid it is used on.</summary>
    public int NeighborCount { get; }

    /// <summary>Neighbors that must share the terrain, one bit per direction.</summary>
    public int RequiredMask { get; }

    /// <summary>Neighbors that must not share the terrain, one bit per direction.</summary>
    public int ForbiddenMask { get; }

    public IReadOnlyList<WeightedTile> Tiles { get; }

    /// <summary>Whether the pattern is also tried in every rotation, rotating the output tile to match.</summary>
    public bool MatchRotations { get; }

    public double TotalWeight { get; }

    /// <summary>The pattern in its text form, one character per neighbor.</summary>
    public string Pattern => string.Create(NeighborCount, this, static (chars, rule) =>
    {
        for (var d = 0; d < chars.Length; d++)
        {
            var bit = 1 << d;
            chars[d] = (rule.RequiredMask & bit) != 0 ? '+' : (rule.ForbiddenMask & bit) != 0 ? '-' : '*';
        }
    });

    /// <summary>Tests the rule against the mask of neighbors sharing the terrain and returns the clockwise rotation, in steps, that matched.</summary>
    public bool TryMatch(int sameMask, out int rotation)
    {
        var attempts = MatchRotations ? NeighborCount : 1;
        for (rotation = 0; rotation < attempts; rotation++)
        {
            var required = RotateClockwise(RequiredMask, rotation);
            var forbidden = RotateClockwise(ForbiddenMask, rotation);
            if ((sameMask & required) == required && (sameMask & forbidden) == 0)
                return true;
        }

        rotation = 0;
        return false;
    }

    /// <summary>Picks an output tile deterministically from a sample between 0 and 1.</summary>
    public int PickTile(double sample)
    {
        if (Tiles.Count == 1 || TotalWeight <= 0)
            return Tiles[0].TileId;

        var target = sample * TotalWeight;
        foreach (var tile in Tiles)
        {
            target -= Math.Max(0.0, tile.Weight);
            if (target < 0)
                return tile.TileId;
        }

        return Tiles[^1].TileId;
    }

    private int RotateClockwise(int mask, int steps) =>
        steps == 0 ? mask : ((mask >> steps) | (mask << (NeighborCount - steps))) & ((1 << NeighborCount) - 1);

    private static (int Required, int Forbidden) ParsePattern(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length is 0 or > MaxNeighbors)
            throw new FormatException($"Auto-tile pattern '{pattern}' must have one character per neighbor.");

        int required = 0, forbidden = 0;
        for (var d = 0; d < pattern.Length; d++)
        {
            switch (pattern[d])
            {
                case '+':
                    required |= 1 << d;
                    break;
                case '-':
                    forbidden |= 1 << d;
                    break;
                case '*':
                    break;
                default:
                    throw new FormatException($"Auto-tile pattern '{pattern}' contains '{pattern[d]}'; use '+', '-' or '*'.");
            }
        }

        return (required, forbidden);
    }
}

/// <summary>A paintable terrain whose cells pick their tile from neighbor-aware <see cref="AutoTileRule"/>s, as in the Hexy editor.</summary>
/// <remarks>Immutable apart from the id a map assigns; edit a terrain by replacing it with <see cref="TileMap.ReplaceTerrain"/>.</remarks>
public sealed class Terrain
{
    private readonly HashSet<int> _members;

    /// <param name="id">The map-unique id; 0 lets the map assign one when the terrain is added.</param>
    public Terrain(string name, int tilesetId, int baseTileId, IReadOnlyList<AutoTileRule> rules, int id = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(rules);
        Id = id;
        Name = name;
        TilesetId = tilesetId;
        BaseTileId = baseTileId;
        Rules = rules;
        _members = [baseTileId];
        foreach (var rule in rules)
        {
            foreach (var tile in rule.Tiles)
                _members.Add(tile.TileId);
        }
    }

    public int Id { get; internal set; }

    public string Name { get; }

    /// <summary>The tileset providing every tile of the terrain.</summary>
    public int TilesetId { get; }

    /// <summary>The tile used when no rule matches.</summary>
    public int BaseTileId { get; }

    /// <summary>Rules evaluated in order; the first match wins.</summary>
    public IReadOnlyList<AutoTileRule> Rules { get; }

    public TileCell BaseTile => new(TilesetId, BaseTileId);

    /// <summary>Data the format the terrain was read from keeps to write it back faithfully; editors leave it alone.</summary>
    public object? FormatData { get; init; }

    /// <summary>Whether a tile is produced by this terrain, whatever its orientation.</summary>
    public bool Contains(TileCell tile) => tile.TilesetId == TilesetId && !tile.IsEmpty && _members.Contains(tile.TileId);

    public Terrain WithName(string name) => new(name, TilesetId, BaseTileId, Rules, Id) { FormatData = FormatData };

    public Terrain WithRules(IReadOnlyList<AutoTileRule> rules) => new(Name, TilesetId, BaseTileId, rules, Id) { FormatData = FormatData };

    public Terrain WithBaseTile(int baseTileId) => new(Name, TilesetId, baseTileId, Rules, Id) { FormatData = FormatData };

    public override string ToString() => Name;
}
