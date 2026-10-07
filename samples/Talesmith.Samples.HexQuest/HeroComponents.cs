using System.Numerics;
using Talesmith.Grids;

namespace Talesmith.Samples.HexQuest;

/// <summary>The player's hero: the cell it stands on and the step it is taking.</summary>
public struct Hero
{
    public GridCoord Cell;

    public GridCoord NextCell;

    public Vector2 StepFrom;

    public Vector2 StepTo;

    /// <summary>Progress of the current step from 0 to 1.</summary>
    public float StepProgress;

    public bool IsStepping;
}

/// <summary>The cells the hero still has to walk through after a click.</summary>
public sealed class HeroPath
{
    public List<GridCoord> Cells { get; } = [];
}
