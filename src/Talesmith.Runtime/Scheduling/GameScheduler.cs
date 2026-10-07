namespace Talesmith.Runtime.Scheduling;

/// <summary>The default <see cref="IGameScheduler"/>, advanced once per frame by the game loop.</summary>
public sealed class GameScheduler : IGameScheduler
{
    private readonly List<Waiter> _waiters = [];
    private readonly List<Waiter> _added = [];
    private double _scaledTime;
    private double _unscaledTime;

    public int PendingCount => _waiters.Count + _added.Count;

    public Task Delay(double seconds, bool scaled = true, CancellationToken cancellationToken = default) =>
        Add(new Waiter(scaled, (scaled ? _scaledTime : _unscaledTime) + Math.Max(0, seconds), null, cancellationToken)).Task;

    public Task NextFrame(CancellationToken cancellationToken = default) => Add(new Waiter(false, 0, null, cancellationToken)).Task;

    public Task WaitUntil(Func<bool> condition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Add(new Waiter(false, 0, condition, cancellationToken)).Task;
    }

    public IDisposable Every(double interval, Action action, bool scaled = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval);
        var repeat = new Repeat();
        _ = RunEvery(interval, action, scaled, repeat.Token);
        return repeat;
    }

    /// <summary>Advances time and completes due waits; called by the game loop on the game thread.</summary>
    public void Update(double scaledDelta, double unscaledDelta)
    {
        _scaledTime += scaledDelta;
        _unscaledTime += unscaledDelta;
        _waiters.AddRange(_added);
        _added.Clear();

        for (var i = _waiters.Count - 1; i >= 0; i--)
        {
            var waiter = _waiters[i];
            if (waiter.Cancellation.IsCancellationRequested)
            {
                waiter.Source.TrySetCanceled(waiter.Cancellation);
                _waiters.RemoveAt(i);
                continue;
            }

            bool due;
            try
            {
                due = waiter.Condition?.Invoke() ?? (waiter.Scaled ? _scaledTime : _unscaledTime) >= waiter.DueTime;
            }
            catch (Exception ex)
            {
                waiter.Source.TrySetException(ex);
                _waiters.RemoveAt(i);
                continue;
            }

            if (due)
            {
                waiter.Source.TrySetResult();
                _waiters.RemoveAt(i);
            }
        }
    }

    /// <summary>Cancels every pending wait, for example when a scene unloads.</summary>
    public void CancelAll()
    {
        foreach (var waiter in _waiters.Concat(_added))
            waiter.Source.TrySetCanceled();
        _waiters.Clear();
        _added.Clear();
    }

    private Waiter Add(Waiter waiter)
    {
        _added.Add(waiter);
        return waiter;
    }

    private async Task RunEvery(double interval, Action action, bool scaled, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Delay(interval, scaled, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            action();
        }
    }

    private sealed class Waiter(bool scaled, double dueTime, Func<bool>? condition, CancellationToken cancellation)
    {
        public readonly TaskCompletionSource Source = new(TaskCreationOptions.None);

        public bool Scaled { get; } = scaled;

        public double DueTime { get; } = dueTime;

        public Func<bool>? Condition { get; } = condition;

        public CancellationToken Cancellation { get; } = cancellation;

        public Task Task => Source.Task;
    }

    private sealed class Repeat : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private bool _disposed;

        public CancellationToken Token => _source.Token;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _source.Cancel();
            _source.Dispose();
        }
    }
}
