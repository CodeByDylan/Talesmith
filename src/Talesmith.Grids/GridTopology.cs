using System.Numerics;

namespace Talesmith.Grids;

/// <summary>How the cells of a grid connect: edge neighbors, rotations, mirrors, distances and the basic shapes built from them.</summary>
/// <remarks>
/// Matches the topologies of the Hexy editor. Neighbors are the cells sharing an edge, in the order of
/// <see cref="IGridLayout.NeighborOffsets"/> without diagonals: E, NE, NW, W, SW, SE on hex grids and E, N, W, S on square grids.
/// Distances measure brush and shape radii, so square grids count a diagonal step as one. Rotations are clockwise on screen, like
/// <c>TileCell.Rotation</c>. Instances are immutable and shared.
/// </remarks>
public sealed class GridTopology
{
    private static readonly GridCoord[] HexDirections = [new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)];
    private static readonly GridCoord[] SquareDirections = [new(1, 0), new(0, -1), new(-1, 0), new(0, 1)];

    private readonly GridCoord[] _directions;

    private GridTopology(GridKind kind)
    {
        Kind = kind;
        IsHex = kind != GridKind.Square;
        _directions = IsHex ? HexDirections : SquareDirections;
    }

    public static GridTopology HexPointyTop { get; } = new(GridKind.HexPointyTop);

    public static GridTopology HexFlatTop { get; } = new(GridKind.HexFlatTop);

    public static GridTopology Square { get; } = new(GridKind.Square);

    public GridKind Kind { get; }

    public bool IsHex { get; }

    /// <summary>The offsets to the cells sharing an edge, counter-clockwise from east.</summary>
    public ReadOnlySpan<GridCoord> Directions => _directions;

    public int NeighborCount => _directions.Length;

    /// <summary>The rotation steps in a full turn: 6 on hex grids and 4 on square grids.</summary>
    public int RotationSteps => _directions.Length;

    public static GridTopology For(GridKind kind) => kind switch
    {
        GridKind.HexPointyTop => HexPointyTop,
        GridKind.HexFlatTop => HexFlatTop,
        GridKind.Square => Square,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public GridCoord Direction(int direction) => _directions[direction];

    /// <summary>The steps between two cells as measured for brush and shape radii.</summary>
    public int Distance(GridCoord a, GridCoord b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return IsHex ? (Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dx + dy)) / 2 : Math.Max(Math.Abs(dx), Math.Abs(dy));
    }

    /// <summary>Gets the cell containing a continuous cell coordinate.</summary>
    public GridCoord Round(double x, double y)
    {
        if (!IsHex)
            return new GridCoord((int)Math.Floor(x + 0.5), (int)Math.Floor(y + 0.5));

        var z = -x - y;
        var rx = Math.Round(x);
        var ry = Math.Round(y);
        var rz = Math.Round(z);
        var dx = Math.Abs(rx - x);
        var dy = Math.Abs(ry - y);
        var dz = Math.Abs(rz - z);
        if (dx > dy && dx > dz)
            rx = -ry - rz;
        else if (dy > dz)
            ry = -rx - rz;
        return new GridCoord((int)rx, (int)ry);
    }

    public GridCoord Round(Vector2 cell) => Round(cell.X, cell.Y);

    /// <summary>Rotates an offset around the origin clockwise on screen by whole rotation steps.</summary>
    public GridCoord Rotate(GridCoord offset, int clockwiseSteps)
    {
        var steps = ((clockwiseSteps % RotationSteps) + RotationSteps) % RotationSteps;
        int x = offset.X, y = offset.Y;
        for (var i = 0; i < steps; i++)
        {
            if (IsHex)
                (x, y) = (-y, x + y);
            else
                (x, y) = (-y, x);
        }

        return new GridCoord(x, y);
    }

    /// <summary>Mirrors an offset across the vertical axis through the origin, so left and right swap on screen.</summary>
    public GridCoord MirrorHorizontal(GridCoord offset) => Kind switch
    {
        GridKind.HexPointyTop => new GridCoord(-offset.X - offset.Y, offset.Y),
        GridKind.HexFlatTop => new GridCoord(-offset.X, offset.X + offset.Y),
        _ => new GridCoord(-offset.X, offset.Y)
    };

    /// <summary>Mirrors an offset across the horizontal axis through the origin, so top and bottom swap on screen.</summary>
    public GridCoord MirrorVertical(GridCoord offset) => Kind switch
    {
        GridKind.HexPointyTop => new GridCoord(offset.X + offset.Y, -offset.Y),
        GridKind.HexFlatTop => new GridCoord(offset.X, -offset.X - offset.Y),
        _ => new GridCoord(offset.X, -offset.Y)
    };

    /// <summary>Converts a cell to screen-aligned column and row: odd rows shift right on pointy-top grids, odd columns shift down on flat-top grids.</summary>
    public GridCoord ToOffset(GridCoord cell) => Kind switch
    {
        GridKind.HexPointyTop => new GridCoord(cell.X + ((cell.Y - (cell.Y & 1)) >> 1), cell.Y),
        GridKind.HexFlatTop => new GridCoord(cell.X, cell.Y + ((cell.X - (cell.X & 1)) >> 1)),
        _ => cell
    };

    /// <summary>Converts a screen-aligned column and row from <see cref="ToOffset"/> back to a cell.</summary>
    public GridCoord FromOffset(GridCoord offset) => Kind switch
    {
        GridKind.HexPointyTop => new GridCoord(offset.X - ((offset.Y - (offset.Y & 1)) >> 1), offset.Y),
        GridKind.HexFlatTop => new GridCoord(offset.X, offset.Y - ((offset.X - (offset.X & 1)) >> 1)),
        _ => offset
    };

    /// <summary>Adds the cells on the straight line from <paramref name="start"/> to <paramref name="end"/>, both included, in order.</summary>
    public void Line(GridCoord start, GridCoord end, ICollection<GridCoord> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var distance = Distance(start, end);
        if (distance == 0)
        {
            output.Add(start);
            return;
        }

        const double nudge = 1e-6;
        var step = 1.0 / distance;
        for (var i = 0; i <= distance; i++)
        {
            var t = step * i;
            output.Add(Round(start.X + nudge + (end.X - start.X) * t, start.Y + nudge + (end.Y - start.Y) * t));
        }
    }

    /// <summary>Adds every cell within <paramref name="radius"/> steps of <paramref name="center"/>: a hexagon on hex grids, a square on square grids.</summary>
    public void Range(GridCoord center, int radius, ICollection<GridCoord> output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentNullException.ThrowIfNull(output);
        for (var dx = -radius; dx <= radius; dx++)
        {
            var minY = IsHex ? Math.Max(-radius, -dx - radius) : -radius;
            var maxY = IsHex ? Math.Min(radius, -dx + radius) : radius;
            for (var dy = minY; dy <= maxY; dy++)
                output.Add(new GridCoord(center.X + dx, center.Y + dy));
        }
    }

    /// <summary>Adds the cells exactly <paramref name="radius"/> steps from <paramref name="center"/>.</summary>
    public void Ring(GridCoord center, int radius, ICollection<GridCoord> output)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentNullException.ThrowIfNull(output);
        if (radius == 0)
        {
            output.Add(center);
            return;
        }

        if (IsHex)
        {
            var cell = center + Directions[4] * radius;
            for (var side = 0; side < 6; side++)
            {
                for (var j = 0; j < radius; j++)
                {
                    output.Add(cell);
                    cell += Directions[side];
                }
            }

            return;
        }

        for (var dx = -radius; dx <= radius; dx++)
        {
            output.Add(new GridCoord(center.X + dx, center.Y - radius));
            output.Add(new GridCoord(center.X + dx, center.Y + radius));
        }

        for (var dy = 1 - radius; dy < radius; dy++)
        {
            output.Add(new GridCoord(center.X - radius, center.Y + dy));
            output.Add(new GridCoord(center.X + radius, center.Y + dy));
        }
    }

    public override string ToString() => Kind.ToString();
}
