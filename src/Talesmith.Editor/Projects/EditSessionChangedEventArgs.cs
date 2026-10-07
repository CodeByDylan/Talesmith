using Talesmith.Avalonia.Hosting;

namespace Talesmith.Editor.Projects;

/// <summary>Tells that <see cref="IProjectService.EditSession"/> was replaced; the previous session is disposed once every handler that asked to
/// keep it is done with it.</summary>
public sealed class EditSessionChangedEventArgs(GameSession previous, GameSession current) : EventArgs
{
    private readonly List<Task> _holds = [];

    public GameSession Previous { get; } = previous;

    public GameSession Current { get; } = current;

    /// <summary>Keeps <see cref="Previous"/> alive until <paramref name="until"/> completes, such as while a view still shows it.</summary>
    public void KeepPrevious(Task until)
    {
        ArgumentNullException.ThrowIfNull(until);
        _holds.Add(until);
    }

    internal Task WhenReleased => Task.WhenAll(_holds);
}
