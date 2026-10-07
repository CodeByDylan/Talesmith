namespace Talesmith.Runtime.Hosting;

/// <summary>Lets cutscenes, dialogs and menus take control away from the player and give it back.</summary>
/// <remarks>Gameplay systems check <see cref="IsSuspended"/> before acting on player input. Suspensions nest: control returns when every one is disposed.</remarks>
public sealed class PlayerControl
{
    private readonly List<string> _reasons = [];

    public bool IsSuspended => _reasons.Count > 0;

    /// <summary>Why control is suspended, for debugging.</summary>
    public IReadOnlyList<string> Reasons => _reasons;

    /// <summary>Suspends player control until the returned handle is disposed.</summary>
    public IDisposable Suspend(string reason)
    {
        _reasons.Add(reason);
        return new Suspension(this, reason);
    }

    private sealed class Suspension(PlayerControl owner, string reason) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner._reasons.Remove(reason);
        }
    }
}
