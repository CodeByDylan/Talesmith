namespace Talesmith.Runtime.Scheduling;

/// <summary>Awaitable waits measured in game frames and game time, for scripts, cutscenes and timed gameplay.</summary>
/// <remarks>
/// Awaits resume on the game thread. Scaled waits follow the time scale and stop while the game is paused; unscaled waits use real
/// time. Cancelling the token completes the task as cancelled.
/// </remarks>
public interface IGameScheduler
{
    /// <summary>Completes after the given number of game seconds.</summary>
    Task Delay(double seconds, bool scaled = true, CancellationToken cancellationToken = default);

    /// <summary>Completes at the start of the next frame.</summary>
    Task NextFrame(CancellationToken cancellationToken = default);

    /// <summary>Completes on the first frame where <paramref name="condition"/> returns true, checked once per frame.</summary>
    Task WaitUntil(Func<bool> condition, CancellationToken cancellationToken = default);

    /// <summary>Calls <paramref name="action"/> every <paramref name="interval"/> game seconds until the returned handle is disposed.</summary>
    IDisposable Every(double interval, Action action, bool scaled = true);
}
