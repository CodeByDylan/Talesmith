namespace Talesmith.Runtime.Hosting;

/// <summary>Which thread runs a game's frames.</summary>
public enum GameThreading
{
    /// <summary>The host calls <see cref="Game.Tick"/> on its own thread, such as the UI thread or a headless loop.</summary>
    Host,

    /// <summary>A <see cref="SimulationThread"/> runs the frames, so stalls of the host's UI thread do not delay the simulation.</summary>
    Dedicated
}
