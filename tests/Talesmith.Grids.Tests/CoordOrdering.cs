namespace Talesmith.Grids.Tests;

internal static class CoordOrdering
{
    /// <summary>Orders cells row by row, so sets can be compared regardless of their enumeration order.</summary>
    public static GridCoord[] Sorted(this IEnumerable<GridCoord> cells) => [.. cells.OrderBy(c => c.Y).ThenBy(c => c.X)];
}
