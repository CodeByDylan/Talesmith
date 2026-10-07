namespace Talesmith.Grids;

/// <summary>The address of a square block of cells, used to store and cull large maps piece by piece.</summary>
public readonly record struct ChunkCoord(int X, int Y)
{
    /// <summary>Gets the chunk containing a cell; <paramref name="shift"/> is log2 of the chunk size.</summary>
    public static ChunkCoord Of(GridCoord cell, int shift) => new(cell.X >> shift, cell.Y >> shift);

    /// <summary>The first cell of this chunk.</summary>
    public GridCoord Origin(int shift) => new(X << shift, Y << shift);

    /// <summary>The chunks that contain any cell within the bounds.</summary>
    public static (ChunkCoord Min, ChunkCoord Max) Covering(GridBounds bounds, int shift) =>
        (new ChunkCoord(bounds.MinX >> shift, bounds.MinY >> shift), new ChunkCoord(bounds.MaxX >> shift, bounds.MaxY >> shift));
}
