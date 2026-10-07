using Talesmith.Grids;

namespace Talesmith.Assets.Maps;

/// <summary>A square block of cells of a tile layer, kept compressed until it is needed.</summary>
/// <remarks>
/// Large maps decode only the chunks near the camera. <see cref="EnsureDecoded"/> may run on a background thread; <see cref="Evict"/>
/// releases the decoded cells again, keeping the compressed form. Changing cells happens on the game thread and bumps
/// <see cref="Version"/> so renderers and colliders rebuild what they cached.
/// </remarks>
public sealed class TileChunk
{
    private readonly Lock _lock = new();
    private byte[]? _compressed;
    private TileCell[]? _cells;

    internal TileChunk(ChunkCoord coord, int size, byte[]? compressed, TileCell[]? cells)
    {
        Coord = coord;
        Size = size;
        _compressed = compressed;
        _cells = cells;
    }

    public ChunkCoord Coord { get; }

    /// <summary>Cells per side.</summary>
    public int Size { get; }

    public bool IsDecoded => Volatile.Read(ref _cells) is not null;

    /// <summary>Increases whenever a cell changes.</summary>
    public int Version { get; private set; }

    /// <summary>The decoded cells, row by row, or null when the chunk is not decoded.</summary>
    public TileCell[]? Cells => Volatile.Read(ref _cells);

    /// <summary>Decodes the chunk if needed and returns its cells. Thread-safe.</summary>
    /// <exception cref="AssetException">The stored data is invalid.</exception>
    public TileCell[] EnsureDecoded()
    {
        if (Volatile.Read(ref _cells) is { } cells)
            return cells;
        lock (_lock)
        {
            _cells ??= ChunkCodec.Decode(_compressed, Size * Size);
            return _cells;
        }
    }

    /// <summary>Releases the decoded cells; returns false when the chunk has no compressed form to fall back to.</summary>
    public bool Evict()
    {
        lock (_lock)
        {
            if (_compressed is null || _cells is null)
                return false;
            Volatile.Write(ref _cells, null);
            return true;
        }
    }

    /// <summary>Gets the compressed cells as loaded, which stay valid until a cell changes; false once the chunk was edited.</summary>
    public bool TryGetCompressed(out ReadOnlyMemory<byte> compressed)
    {
        var data = Volatile.Read(ref _compressed);
        compressed = data;
        return data is not null;
    }

    /// <summary>Whether every cell is empty; decodes the chunk.</summary>
    public bool IsBlank()
    {
        foreach (var cell in EnsureDecoded())
        {
            if (!cell.IsEmpty)
                return false;
        }

        return true;
    }

    public TileCell this[int localX, int localY] => EnsureDecoded()[localY * Size + localX];

    /// <summary>Sets a cell and returns the value it replaced; unchanged cells keep the version.</summary>
    internal TileCell Exchange(int localX, int localY, TileCell cell)
    {
        var cells = EnsureDecoded();
        ref var slot = ref cells[localY * Size + localX];
        var previous = slot;
        if (previous == cell)
            return previous;
        slot = cell;
        Volatile.Write(ref _compressed, null);
        Version++;
        return previous;
    }
}
