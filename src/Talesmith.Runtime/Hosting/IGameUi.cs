namespace Talesmith.Runtime.Hosting;

/// <summary>Runs code on the thread that owns the game's user interface, such as Avalonia's UI thread.</summary>
/// <remarks>Game code hands state to overlays through it, usually with a <see cref="ViewState{T}"/>. Without a UI, actions run immediately.</remarks>
public interface IGameUi
{
    /// <summary>Whether the calling thread is the UI thread.</summary>
    bool CheckAccess();

    /// <summary>Queues an action for the UI thread; safe to call from any thread.</summary>
    void Post(Action action);
}

/// <summary>The <see cref="IGameUi"/> of games without a user interface: actions run on the calling thread.</summary>
internal sealed class ImmediateGameUi : IGameUi
{
    public bool CheckAccess() => true;

    public void Post(Action action) => action();
}
