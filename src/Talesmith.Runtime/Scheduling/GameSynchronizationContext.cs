using System.Collections.Concurrent;

namespace Talesmith.Runtime.Scheduling;

/// <summary>Runs continuations of game code on the game thread, at the start of the next frame.</summary>
/// <remarks>
/// The runtime installs it while a frame runs, so <c>await</c> inside scenes, systems and scripts resumes on the game thread no matter
/// where the awaited work completed. Continuations posted from other threads wait for the next <see cref="Pump"/>.
/// </remarks>
public sealed class GameSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    private volatile int _threadId;

    /// <summary>The number of continuations waiting to run.</summary>
    public int Pending => _queue.Count;

    /// <summary>Whether the calling thread is the one that pumps this context.</summary>
    public bool CheckAccess() => _threadId != 0 && Environment.CurrentManagedThreadId == _threadId;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    /// <remarks>Runs the callback directly on the game thread; elsewhere blocks until the next frame runs it.</remarks>
    public override void Send(SendOrPostCallback d, object? state)
    {
        if (CheckAccess())
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(_ =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        }, null);
        done.Wait();
        if (error is not null)
            throw new InvalidOperationException("A callback sent to the game thread failed.", error);
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs the continuations queued before this call; ones queued while running wait for the next pump.</summary>
    public void Pump(Action<Exception> onError)
    {
        Bind();
        var count = _queue.Count;
        for (var i = 0; i < count && _queue.TryDequeue(out var item); i++)
        {
            try
            {
                item.Callback(item.State);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        }
    }

    /// <summary>Makes the calling thread the game thread.</summary>
    internal void Bind() => _threadId = Environment.CurrentManagedThreadId;

    /// <summary>Leaves the context without a game thread until the next <see cref="Pump"/>.</summary>
    internal void Unbind() => _threadId = 0;
}
