namespace Talesmith.Runtime.Hosting;

/// <summary>Lets game code end the game, for example from a quit button; the host closes its window or stops its loop.</summary>
public sealed class GameLifetime
{
    private int _quitRequested;

    public bool IsQuitRequested => Volatile.Read(ref _quitRequested) != 0;

    /// <summary>Raised once, on the thread that called <see cref="Quit"/>.</summary>
    public event Action? QuitRequested;

    /// <summary>Asks the host to end the game; calling it again does nothing.</summary>
    public void Quit()
    {
        if (Interlocked.Exchange(ref _quitRequested, 1) == 0)
            QuitRequested?.Invoke();
    }
}
