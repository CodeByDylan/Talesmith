using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Grids;

/// <summary>Square or rectangular cells in rows and columns, matching the orthogonal maps of the Hexy editor.</summary>
/// <remarks>Cell (0, 0) is centered on the origin; X counts columns to the east and Y rows to the south.</remarks>
/// <param name="allowDiagonals">Whether diagonal cells count as neighbors, for movement and distance.</param>
public sealed class SquareLayout(float width, float height, bool allowDiagonals = false) : IGridLayout
{
    private static readonly GridCoord[] Orthogonal = [new(1, 0), new(0, -1), new(-1, 0), new(0, 1)];
    private static readonly GridCoord[] WithDiagonals = [new(1, 0), new(1, -1), new(0, -1), new(-1, -1), new(-1, 0), new(-1, 1), new(0, 1), new(1, 1)];

    public GridKind Kind => GridKind.Square;

    public Vector2 CellSize { get; } = width > 0 && height > 0 ? new Vector2(width, height) : throw new ArgumentOutOfRangeException(nameof(width), "Cells must have a positive size.");

    public bool AllowDiagonals { get; } = allowDiagonals;

    public int CornerCount => 4;

    public int RotationSteps => 4;

    public ReadOnlySpan<GridCoord> NeighborOffsets => AllowDiagonals ? WithDiagonals : Orthogonal;

    public Vector2 CellToWorld(GridCoord cell) => new(cell.X * CellSize.X, cell.Y * CellSize.Y);

    public Vector2 CellToWorld(Vector2 cell) => cell * CellSize;

    public Vector2 WorldToContinuous(Vector2 world) => world / CellSize;

    public GridCoord WorldToCell(Vector2 world) =>
        new((int)MathF.Floor(world.X / CellSize.X + 0.5f), (int)MathF.Floor(world.Y / CellSize.Y + 0.5f));

    public Vector2 CornerOffset(int corner) => corner switch
    {
        0 => new Vector2(CellSize.X / 2, -CellSize.Y / 2),
        1 => new Vector2(CellSize.X / 2, CellSize.Y / 2),
        2 => new Vector2(-CellSize.X / 2, CellSize.Y / 2),
        3 => new Vector2(-CellSize.X / 2, -CellSize.Y / 2),
        _ => throw new ArgumentOutOfRangeException(nameof(corner))
    };

    public Rect2 CellBounds(GridCoord cell) => Rect2.FromCenter(CellToWorld(cell), CellSize);

    public GridBounds CoveringBounds(in Rect2 world)
    {
        var min = WorldToCell(world.Position);
        var max = WorldToCell(new Vector2(world.Right, world.Bottom));
        return new GridBounds(min.X - 1, min.Y - 1, max.X + 1, max.Y + 1);
    }

    public int Distance(GridCoord a, GridCoord b)
    {
        var dx = Math.Abs(a.X - b.X);
        var dy = Math.Abs(a.Y - b.Y);
        return AllowDiagonals ? Math.Max(dx, dy) : dx + dy;
    }
}
