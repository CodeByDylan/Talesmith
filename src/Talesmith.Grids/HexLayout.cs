using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Grids;

/// <summary>Axial hex grid geometry, matching the Hexy editor.</summary>
/// <remarks>
/// The cell size is the bounding box of one hex, so regular and squashed hexes are both supported. Pointy-top grids step +q to the
/// east and +r to the south-east; flat-top grids step +q to the east-south-east and +r to the south.
/// </remarks>
public sealed class HexLayout : IGridLayout
{
    private static readonly double Sqrt3 = Math.Sqrt(3.0);
    private static readonly GridCoord[] Neighbors = [new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)];

    private readonly double _f0, _f1, _f2, _f3;
    private readonly double _b0, _b1, _b2, _b3;
    private readonly double _sizeX, _sizeY;
    private readonly Vector2[] _corners = new Vector2[6];

    public HexLayout(bool pointyTop, float width, float height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Kind = pointyTop ? GridKind.HexPointyTop : GridKind.HexFlatTop;
        CellSize = new Vector2(width, height);

        if (pointyTop)
        {
            (_f0, _f1, _f2, _f3) = (Sqrt3, Sqrt3 / 2.0, 0.0, 3.0 / 2.0);
            (_b0, _b1, _b2, _b3) = (Sqrt3 / 3.0, -1.0 / 3.0, 0.0, 2.0 / 3.0);
            _sizeX = width / Sqrt3;
            _sizeY = height / 2.0;
        }
        else
        {
            (_f0, _f1, _f2, _f3) = (3.0 / 2.0, 0.0, Sqrt3 / 2.0, Sqrt3);
            (_b0, _b1, _b2, _b3) = (2.0 / 3.0, 0.0, -1.0 / 3.0, Sqrt3 / 3.0);
            _sizeX = width / 2.0;
            _sizeY = height / Sqrt3;
        }

        var startAngle = pointyTop ? 0.5 : 0.0;
        for (var i = 0; i < 6; i++)
        {
            var angle = 2.0 * Math.PI * (startAngle + i) / 6.0;
            _corners[i] = new Vector2((float)(_sizeX * Math.Cos(angle)), (float)(_sizeY * Math.Sin(angle)));
        }
    }

    public GridKind Kind { get; }

    public Vector2 CellSize { get; }

    public int CornerCount => 6;

    public int RotationSteps => 6;

    public ReadOnlySpan<GridCoord> NeighborOffsets => Neighbors;

    public Vector2 CellToWorld(GridCoord cell) => ToWorld(cell.X, cell.Y);

    public Vector2 CellToWorld(Vector2 cell) => ToWorld(cell.X, cell.Y);

    public Vector2 WorldToContinuous(Vector2 world)
    {
        var x = world.X / _sizeX;
        var y = world.Y / _sizeY;
        return new Vector2((float)(_b0 * x + _b1 * y), (float)(_b2 * x + _b3 * y));
    }

    public GridCoord WorldToCell(Vector2 world)
    {
        var x = world.X / _sizeX;
        var y = world.Y / _sizeY;
        return Round(_b0 * x + _b1 * y, _b2 * x + _b3 * y);
    }

    public Vector2 CornerOffset(int corner) => _corners[corner];

    public Rect2 CellBounds(GridCoord cell) => Rect2.FromCenter(CellToWorld(cell), CellSize);

    public GridBounds CoveringBounds(in Rect2 world)
    {
        var a = WorldToCell(new Vector2(world.Left, world.Top));
        var b = WorldToCell(new Vector2(world.Right, world.Top));
        var c = WorldToCell(new Vector2(world.Left, world.Bottom));
        var d = WorldToCell(new Vector2(world.Right, world.Bottom));
        return new GridBounds(
            Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - 1,
            Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - 1,
            Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + 1,
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + 1);
    }

    public int Distance(GridCoord a, GridCoord b)
    {
        var dq = a.X - b.X;
        var dr = a.Y - b.Y;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    private Vector2 ToWorld(double q, double r) =>
        new((float)((_f0 * q + _f1 * r) * _sizeX), (float)((_f2 * q + _f3 * r) * _sizeY));

    private static GridCoord Round(double q, double r)
    {
        var s = -q - r;
        var rq = Math.Round(q);
        var rr = Math.Round(r);
        var rs = Math.Round(s);
        var dq = Math.Abs(rq - q);
        var dr = Math.Abs(rr - r);
        var ds = Math.Abs(rs - s);
        if (dq > dr && dq > ds)
            rq = -rr - rs;
        else if (dr > ds)
            rr = -rq - rs;
        return new GridCoord((int)rq, (int)rr);
    }
}
