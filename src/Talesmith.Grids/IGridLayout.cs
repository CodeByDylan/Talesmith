using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Grids;

/// <summary>Maps grid cells to world space and back, and knows how cells connect.</summary>
/// <remarks>
/// World space has Y pointing down and the center of cell (0, 0) at the origin. Implementations are immutable and safe to share.
/// </remarks>
public interface IGridLayout
{
    GridKind Kind { get; }

    /// <summary>How cells connect, for shapes, fills and auto-tiling; square grids use edge neighbors even when they allow diagonal movement.</summary>
    GridTopology Topology => GridTopology.For(Kind);

    /// <summary>The size of a cell's bounding box in world units.</summary>
    Vector2 CellSize { get; }

    /// <summary>The number of corners of a cell's outline.</summary>
    int CornerCount { get; }

    /// <summary>The offsets to adjacent cells, in a fixed order.</summary>
    ReadOnlySpan<GridCoord> NeighborOffsets { get; }

    /// <summary>The number of rotation steps in a full turn: 6 on hex grids, where a step is 60°, and 4 on square grids, where it is 90°.</summary>
    int RotationSteps { get; }

    /// <summary>Gets the world position of a cell's center.</summary>
    Vector2 CellToWorld(GridCoord cell);

    /// <summary>Gets the world position of a continuous cell coordinate, such as an object placed between cell centers.</summary>
    Vector2 CellToWorld(Vector2 cell);

    /// <summary>Gets the continuous cell coordinate of a world position.</summary>
    Vector2 WorldToContinuous(Vector2 world);

    /// <summary>Gets the cell containing a world position.</summary>
    GridCoord WorldToCell(Vector2 world);

    /// <summary>Gets the offset of a corner from the cell center, for drawing outlines.</summary>
    Vector2 CornerOffset(int corner);

    /// <summary>Gets the world bounding box of a cell.</summary>
    Rect2 CellBounds(GridCoord cell);

    /// <summary>Gets cell bounds that include every cell that may intersect a world rectangle, for culling.</summary>
    GridBounds CoveringBounds(in Rect2 world);

    /// <summary>The number of steps between two cells when moving between neighbors.</summary>
    int Distance(GridCoord a, GridCoord b);
}
