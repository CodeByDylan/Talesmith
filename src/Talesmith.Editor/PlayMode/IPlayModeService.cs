using Talesmith.Avalonia.Hosting;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.PlayMode;

public enum PlayState
{
    /// <summary>Editing; no play session exists.</summary>
    Stopped,

    /// <summary>The play session is being created.</summary>
    Starting,

    Playing,

    /// <summary>The play session exists and is paused; frames still render and the world can be inspected.</summary>
    Paused
}

/// <summary>Plays the open scene, or the game from its start scene, in a separate game session, and stops it again.</summary>
/// <remarks>
/// <para>Playing never changes the edit world or the open document: the play session gets a copy of the scene, so <see cref="StopAsync"/>
/// only disposes the session, and selection, camera and unsaved changes stay as they were.</para>
/// <para>While playing, <see cref="Game"/> is the play session's game, whose world panels such as the hierarchy and inspector show live; they read
/// and change it only through <see cref="Dispatch"/> and <see cref="InvokeAsync{T}"/>.
/// The game's event bus receives <see cref="PlayModeEntered"/> when play starts, <see cref="PauseStateChanged"/> when it pauses or resumes, and
/// <see cref="PlayModeExited"/> before it stops.</para>
/// <para>The play session runs on its own simulation thread, so the editor reaches its game only through <see cref="Dispatch"/> and
/// <see cref="InvokeAsync{T}"/>; reading or changing the play world from the UI thread directly races with the running frames.</para>
/// </remarks>
public interface IPlayModeService
{
    PlayState State { get; }

    /// <summary>Whether a play session is running or paused.</summary>
    bool IsPlaying { get; }

    bool IsPaused { get; }

    /// <summary>The play session, or null while stopped.</summary>
    GameSession? Session { get; }

    /// <summary>The play session's game, or null while stopped.</summary>
    Game? Game { get; }

    /// <summary>Raised on the UI thread after <see cref="State"/> changed.</summary>
    event EventHandler? StateChanged;

    /// <summary>Plays the open scene as it is, including unsaved changes.</summary>
    Task PlayAsync();

    /// <summary>Plays the game from the project's start scene, using unsaved changes of the open scene.</summary>
    Task StartAsync();

    /// <summary>Plays when stopped, stops otherwise.</summary>
    Task TogglePlayAsync();

    /// <summary>Stops and plays again the way play mode last started, such as to pick up scripts that cannot be reloaded.</summary>
    Task RestartAsync();

    void Pause();

    void Resume();

    void TogglePause();

    /// <summary>Runs one frame and pauses again; pauses first when playing.</summary>
    void Step();

    /// <summary>Ends the play session and returns to editing.</summary>
    Task StopAsync();

    /// <summary>Runs an action on the play session's game thread at the start of its next frame; does nothing while stopped.</summary>
    /// <remarks>Safe to call from any thread. Exceptions are reported to the console.</remarks>
    void Dispatch(Action<Game> action);

    /// <summary>Runs a function on the play session's game thread and returns its result, such as a snapshot for a live panel.</summary>
    /// <remarks>The task faults with <see cref="InvalidOperationException"/> while stopped, and is canceled when the session ends first.</remarks>
    Task<T> InvokeAsync<T>(Func<Game, T> func);
}

public static class PlayModeServiceExtensions
{
    /// <summary>Like <see cref="IPlayModeService.InvokeAsync{T}"/>, but gives <paramref name="fallback"/> when no play session runs, it ended or
    /// <paramref name="func"/> threw, as the play world can change under any read.</summary>
    public static async Task<T> TryInvokeAsync<T>(this IPlayModeService play, Func<Game, T> func, T fallback)
    {
        ArgumentNullException.ThrowIfNull(play);
        if (!play.IsPlaying)
            return fallback;
        try
        {
            return await play.InvokeAsync(func);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return fallback;
        }
    }
}
