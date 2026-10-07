using Talesmith.Grids;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Samples.HexQuest;

/// <summary>A snapshot of the HUD: the map title and where the hero stands.</summary>
public sealed record QuestHudState(string MapTitle, GridCoord Cell, string Terrain)
{
    public static readonly QuestHudState Empty = new(string.Empty, default, string.Empty);
}

/// <summary>What the HUD shows. Systems change it on the game thread; <see cref="HudOverlay"/> shows <see cref="View"/> on the UI thread.</summary>
public sealed class QuestHud(IGameUi ui)
{
    public ViewState<QuestHudState> View { get; } = new(ui, QuestHudState.Empty);

    public string MapTitle
    {
        get => View.Value.MapTitle;
        set
        {
            if (value != MapTitle)
                View.Publish(View.Value with { MapTitle = value });
        }
    }

    public GridCoord Cell
    {
        get => View.Value.Cell;
        set
        {
            if (value != Cell)
                View.Publish(View.Value with { Cell = value });
        }
    }

    public string Terrain
    {
        get => View.Value.Terrain;
        set
        {
            if (value != Terrain)
                View.Publish(View.Value with { Terrain = value });
        }
    }
}
