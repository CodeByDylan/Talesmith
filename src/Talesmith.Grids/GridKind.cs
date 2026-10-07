namespace Talesmith.Grids;

/// <summary>The cell shape and arrangement of a grid.</summary>
public enum GridKind
{
    /// <summary>Hexagons with a vertex at the top; rows are offset.</summary>
    HexPointyTop,

    /// <summary>Hexagons with an edge at the top; columns are offset.</summary>
    HexFlatTop,

    /// <summary>Square or rectangular cells in rows and columns.</summary>
    Square
}
