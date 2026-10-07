using Talesmith.Runtime.Hosting;

namespace Talesmith.Samples.IsleHopper;

/// <summary>A snapshot of the HUD: the level title, emeralds, time, the nearest sign and whether the level is complete.</summary>
/// <param name="Seconds">Whole seconds since the run started.</param>
/// <param name="Falls">How often the hero was sent back to a checkpoint.</param>
/// <param name="Hint">The text of the sign the hero stands at, if any.</param>
public sealed record IsleHudState(string Title, int Gems, int GemsTotal, int Seconds, int Falls, string? Hint, bool IsComplete)
{
    public static readonly IsleHudState Empty = new(string.Empty, 0, 0, 0, 0, null, false);
}

/// <summary>What the HUD shows. Systems change it on the game thread; <see cref="HudOverlay"/> shows <see cref="View"/> on the UI thread.</summary>
public sealed class IsleHud(IGameUi ui)
{
    public ViewState<IsleHudState> View { get; } = new(ui, IsleHudState.Empty);

    public string Title
    {
        get => View.Value.Title;
        set
        {
            if (value != Title)
                View.Publish(View.Value with { Title = value });
        }
    }

    public int Gems
    {
        get => View.Value.Gems;
        set
        {
            if (value != Gems)
                View.Publish(View.Value with { Gems = value });
        }
    }

    public int GemsTotal
    {
        get => View.Value.GemsTotal;
        set
        {
            if (value != GemsTotal)
                View.Publish(View.Value with { GemsTotal = value });
        }
    }

    /// <summary>Whole seconds since the run started.</summary>
    public int Seconds
    {
        get => View.Value.Seconds;
        set
        {
            if (value != Seconds)
                View.Publish(View.Value with { Seconds = value });
        }
    }

    /// <summary>How often the hero was sent back to a checkpoint.</summary>
    public int Falls
    {
        get => View.Value.Falls;
        set
        {
            if (value != Falls)
                View.Publish(View.Value with { Falls = value });
        }
    }

    /// <summary>The text of the sign the hero stands at, if any.</summary>
    public string? Hint
    {
        get => View.Value.Hint;
        set
        {
            if (value != Hint)
                View.Publish(View.Value with { Hint = value });
        }
    }

    public bool IsComplete
    {
        get => View.Value.IsComplete;
        set
        {
            if (value != IsComplete)
                View.Publish(View.Value with { IsComplete = value });
        }
    }

    /// <summary>Clears the run's numbers for a new run of a level.</summary>
    public void Reset(string title, int gemsTotal) => View.Publish(IsleHudState.Empty with { Title = title, GemsTotal = gemsTotal });
}
