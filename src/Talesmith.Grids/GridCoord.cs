namespace Talesmith.Grids;

/// <summary>The integer address of a grid cell.</summary>
/// <remarks>On hex grids these are axial coordinates, with <see cref="X"/> as q and <see cref="Y"/> as r. On square grids they are column and row.</remarks>
public readonly record struct GridCoord(int X, int Y)
{
    public static GridCoord Zero => default;

    public static GridCoord operator +(GridCoord a, GridCoord b) => new(a.X + b.X, a.Y + b.Y);

    public static GridCoord operator -(GridCoord a, GridCoord b) => new(a.X - b.X, a.Y - b.Y);

    public static GridCoord operator -(GridCoord a) => new(-a.X, -a.Y);

    public static GridCoord operator *(GridCoord a, int factor) => new(a.X * factor, a.Y * factor);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>An inclusive rectangle of grid coordinates.</summary>
public readonly record struct GridBounds(int MinX, int MinY, int MaxX, int MaxY)
{
    /// <summary>Bounds that contain nothing; including a cell makes them that cell.</summary>
    public static GridBounds Empty => new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

    public bool IsEmpty => MaxX < MinX || MaxY < MinY;

    public int Width => IsEmpty ? 0 : MaxX - MinX + 1;

    public int Height => IsEmpty ? 0 : MaxY - MinY + 1;

    public static GridBounds Of(GridCoord cell) => new(cell.X, cell.Y, cell.X, cell.Y);

    public bool Contains(GridCoord coord) => coord.X >= MinX && coord.X <= MaxX && coord.Y >= MinY && coord.Y <= MaxY;

    public GridBounds Include(GridCoord coord) =>
        new(Math.Min(MinX, coord.X), Math.Min(MinY, coord.Y), Math.Max(MaxX, coord.X), Math.Max(MaxY, coord.Y));

    public GridBounds Union(GridBounds other) => IsEmpty ? other : other.IsEmpty ? this
        : new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY), Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public bool Intersects(GridBounds other) =>
        !IsEmpty && !other.IsEmpty && MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;
}
