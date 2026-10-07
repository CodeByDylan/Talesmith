namespace Talesmith.Runtime.Hosting;

/// <summary>The newest snapshot of state that the game shows in its UI: the game thread publishes immutable values, the UI thread reads them.</summary>
/// <remarks>
/// <see cref="Publish"/> belongs to the game thread. <see cref="Changed"/> is raised on the UI thread with the newest value, at most once per
/// UI dispatch however often the game publishes. Use immutable values, such as records.
/// </remarks>
/// <typeparam name="T">The snapshot type; values equal to the current one are not published.</typeparam>
public sealed class ViewState<T>
    where T : class
{
    private readonly IGameUi _ui;
    private readonly Action _notify;
    private T _value;
    private int _notifyPending;

    public ViewState(IGameUi ui, T initial)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(initial);
        _ui = ui;
        _value = initial;
        _notify = Notify;
    }

    /// <summary>The newest published snapshot; safe to read from any thread.</summary>
    public T Value => Volatile.Read(ref _value);

    /// <summary>Raised on the UI thread after one or more snapshots were published.</summary>
    public event Action<T>? Changed;

    /// <summary>Replaces the snapshot and schedules <see cref="Changed"/> on the UI thread.</summary>
    public void Publish(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (EqualityComparer<T>.Default.Equals(Value, value))
            return;
        Volatile.Write(ref _value, value);
        if (Interlocked.Exchange(ref _notifyPending, 1) == 0)
            _ui.Post(_notify);
    }

    private void Notify()
    {
        Volatile.Write(ref _notifyPending, 0);
        Changed?.Invoke(Value);
    }
}
