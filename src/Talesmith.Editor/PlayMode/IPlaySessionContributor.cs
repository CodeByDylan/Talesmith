using Talesmith.Editor.Projects;

namespace Talesmith.Editor.PlayMode;

/// <summary>Takes part in starting play sessions: can keep play mode from starting, and adds to the session, such as the compiled scripts.</summary>
/// <remarks>Register implementations as <see cref="IPlaySessionContributor"/> singletons; they are asked in registration order.</remarks>
public interface IPlaySessionContributor
{
    /// <summary>Returns why play mode cannot start, or null when it can; may wait for work in progress, such as a compilation.</summary>
    ValueTask<string?> PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Adds to the play session's request.</summary>
    GameSessionRequest Contribute(GameSessionRequest request);
}
