using System.Collections;
using System.Numerics;

namespace Talesmith.Grids;

/// <summary>How a new set of cells combines with an existing selection.</summary>
public enum SelectionMode
{
    Replace,
    Add,
    Subtract,
    Intersect
}

/// <summary>A set of cells stored as sparse 32×32 bit blocks, so selections, fills and brush footprints of millions of cells stay small and fast.</summary>
/// <remarks>Adding, removing and testing a cell never allocates once its block exists; emptied blocks are recycled. Not thread-safe.</remarks>
public sealed class CellSet : ICollection<GridCoord>, IReadOnlyCollection<GridCoord>
{
    private const int Shift = 5;
    private const int Mask = (1 << Shift) - 1;
    private const int WordsPerBlock = (1 << (Shift * 2)) / 64;

    private readonly Dictionary<ChunkCoord, ulong[]> _blocks = new();
    private readonly Stack<ulong[]> _free = new();

    public CellSet()
    {
    }

    public CellSet(IEnumerable<GridCoord> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        foreach (var cell in cells)
            Add(cell);
    }

    public int Count { get; private set; }

    public bool IsEmpty => Count == 0;

    bool ICollection<GridCoord>.IsReadOnly => false;

    /// <summary>The smallest bounds containing every cell, or <see cref="GridBounds.Empty"/>.</summary>
    public GridBounds Bounds
    {
        get
        {
            var bounds = GridBounds.Empty;
            foreach (var (coord, block) in _blocks)
            {
                for (var w = 0; w < WordsPerBlock; w++)
                {
                    var word = block[w];
                    while (word != 0)
                    {
                        var bit = BitOperations.TrailingZeroCount(word);
                        word &= word - 1;
                        bounds = bounds.Include(CellAt(coord, w * 64 + bit));
                    }
                }
            }

            return bounds;
        }
    }

    public bool Contains(GridCoord cell)
    {
        if (!_blocks.TryGetValue(ChunkCoord.Of(cell, Shift), out var block))
            return false;
        var index = IndexOf(cell);
        return (block[index >> 6] & (1UL << index)) != 0;
    }

    /// <summary>Adds a cell and returns whether it was not in the set yet.</summary>
    public bool Add(GridCoord cell)
    {
        var coord = ChunkCoord.Of(cell, Shift);
        if (!_blocks.TryGetValue(coord, out var block))
        {
            block = _free.Count > 0 ? _free.Pop() : new ulong[WordsPerBlock];
            _blocks.Add(coord, block);
        }

        var index = IndexOf(cell);
        ref var word = ref block[index >> 6];
        var bit = 1UL << index;
        if ((word & bit) != 0)
            return false;
        word |= bit;
        Count++;
        return true;
    }

    void ICollection<GridCoord>.Add(GridCoord item) => Add(item);

    public bool Remove(GridCoord cell)
    {
        var coord = ChunkCoord.Of(cell, Shift);
        if (!_blocks.TryGetValue(coord, out var block))
            return false;

        var index = IndexOf(cell);
        ref var word = ref block[index >> 6];
        var bit = 1UL << index;
        if ((word & bit) == 0)
            return false;
        word &= ~bit;
        Count--;
        if (word == 0 && IsBlank(block))
            Release(coord, block);
        return true;
    }

    public void Clear()
    {
        foreach (var block in _blocks.Values)
        {
            Array.Clear(block);
            _free.Push(block);
        }

        _blocks.Clear();
        Count = 0;
    }

    public void UnionWith(CellSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var (coord, source) in other._blocks)
        {
            if (!_blocks.TryGetValue(coord, out var block))
            {
                block = _free.Count > 0 ? _free.Pop() : new ulong[WordsPerBlock];
                _blocks.Add(coord, block);
            }

            for (var w = 0; w < WordsPerBlock; w++)
            {
                Count += BitOperations.PopCount(source[w] & ~block[w]);
                block[w] |= source[w];
            }
        }
    }

    public void ExceptWith(CellSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (var (coord, source) in other._blocks)
        {
            if (!_blocks.TryGetValue(coord, out var block))
                continue;
            for (var w = 0; w < WordsPerBlock; w++)
            {
                Count -= BitOperations.PopCount(block[w] & source[w]);
                block[w] &= ~source[w];
            }

            if (IsBlank(block))
                Release(coord, block);
        }
    }

    public void IntersectWith(CellSet other)
    {
        ArgumentNullException.ThrowIfNull(other);
        List<ChunkCoord>? blank = null;
        foreach (var (coord, block) in _blocks)
        {
            other._blocks.TryGetValue(coord, out var source);
            for (var w = 0; w < WordsPerBlock; w++)
            {
                var kept = source is null ? 0 : block[w] & source[w];
                Count -= BitOperations.PopCount(block[w] & ~kept);
                block[w] = kept;
            }

            if (IsBlank(block))
                (blank ??= []).Add(coord);
        }

        if (blank is null)
            return;
        foreach (var coord in blank)
            Release(coord, _blocks[coord]);
    }

    /// <summary>Combines <paramref name="cells"/> into this set the way a selection tool does.</summary>
    public void Apply(CellSet cells, SelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(cells);
        switch (mode)
        {
            case SelectionMode.Replace:
                if (ReferenceEquals(cells, this))
                    return;
                Clear();
                UnionWith(cells);
                break;
            case SelectionMode.Add:
                UnionWith(cells);
                break;
            case SelectionMode.Subtract:
                ExceptWith(cells);
                break;
            case SelectionMode.Intersect:
                IntersectWith(cells);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    /// <summary>Gets a copy of this set moved by <paramref name="delta"/>.</summary>
    public CellSet Translate(GridCoord delta)
    {
        var moved = new CellSet();
        foreach (var cell in this)
            moved.Add(cell + delta);
        return moved;
    }

    public void CopyTo(GridCoord[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (var cell in this)
            array[arrayIndex++] = cell;
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<GridCoord> IEnumerable<GridCoord>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static int IndexOf(GridCoord cell) => ((cell.Y & Mask) << Shift) | (cell.X & Mask);

    private static GridCoord CellAt(ChunkCoord coord, int index) =>
        new((coord.X << Shift) + (index & Mask), (coord.Y << Shift) + (index >> Shift));

    private static bool IsBlank(ulong[] block)
    {
        for (var w = 0; w < WordsPerBlock; w++)
        {
            if (block[w] != 0)
                return false;
        }

        return true;
    }

    private void Release(ChunkCoord coord, ulong[] block)
    {
        _blocks.Remove(coord);
        _free.Push(block);
    }

    /// <summary>Enumerates the cells block by block without allocating.</summary>
    public struct Enumerator : IEnumerator<GridCoord>
    {
        private Dictionary<ChunkCoord, ulong[]>.Enumerator _blocks;
        private ChunkCoord _coord;
        private ulong[]? _block;
        private int _wordIndex;
        private ulong _word;

        internal Enumerator(CellSet set)
        {
            _blocks = set._blocks.GetEnumerator();
            _block = null;
            _wordIndex = WordsPerBlock;
        }

        public GridCoord Current { get; private set; }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (true)
            {
                if (_word != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(_word);
                    _word &= _word - 1;
                    Current = CellAt(_coord, _wordIndex * 64 + bit);
                    return true;
                }

                if (_block is not null && ++_wordIndex < WordsPerBlock)
                {
                    _word = _block[_wordIndex];
                    continue;
                }

                if (!_blocks.MoveNext())
                    return false;
                (_coord, _block) = _blocks.Current;
                _wordIndex = 0;
                _word = _block[0];
            }
        }

        public void Reset() => throw new NotSupportedException();

        public readonly void Dispose()
        {
        }
    }
}
