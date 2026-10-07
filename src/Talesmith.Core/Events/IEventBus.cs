namespace Talesmith.Events;

/// <summary>Handles an event; it receives the event by reference so it can cancel it or fill in results.</summary>
public delegate void EventCallback<T>(ref T e);

/// <summary>Handles an event asynchronously, for work such as loading or showing a dialog.</summary>
public delegate ValueTask AsyncEventCallback<T>(T e, CancellationToken cancellationToken);

/// <summary>Decides whether a handler should receive an event.</summary>
public delegate bool EventFilter<T>(in T e);

/// <summary>An event that handlers can cancel; once cancelled, handlers with lower priority are skipped.</summary>
public interface ICancellableEvent
{
    bool IsCancelled { get; }
}

/// <summary>Delivers typed events to subscribers.</summary>
/// <remarks>
/// Events are usually small structs. <see cref="Publish{T}(ref T)"/> runs handlers immediately on the calling thread, highest priority
/// first. <see cref="Enqueue{T}"/> can be called from any thread and delivers on the next <see cref="DispatchQueued"/>, which the game
/// loop calls once per frame. Subscribing and unsubscribing are thread-safe.
/// </remarks>
public interface IEventBus
{
    /// <summary>Subscribes a handler; dispose the result to unsubscribe. Higher priorities run first.</summary>
    IDisposable Subscribe<T>(EventCallback<T> handler, int priority = 0, EventFilter<T>? filter = null);

    /// <summary>Subscribes an asynchronous handler, which runs only for <see cref="PublishAsync{T}"/>.</summary>
    IDisposable SubscribeAsync<T>(AsyncEventCallback<T> handler, int priority = 0, EventFilter<T>? filter = null);

    /// <summary>Delivers an event to synchronous handlers now; returns false when a handler cancelled it.</summary>
    bool Publish<T>(ref T e);

    /// <summary>Delivers a copy of an event to synchronous handlers now; returns false when a handler cancelled it.</summary>
    bool Publish<T>(T e);

    /// <summary>Delivers an event to synchronous and then asynchronous handlers, awaiting each in priority order.</summary>
    ValueTask<bool> PublishAsync<T>(T e, CancellationToken cancellationToken = default);

    /// <summary>Queues an event for the next <see cref="DispatchQueued"/>. Safe to call from any thread.</summary>
    void Enqueue<T>(in T e);

    /// <summary>Delivers every queued event in the order it was queued.</summary>
    void DispatchQueued();

    /// <summary>Whether any handler is subscribed to <typeparamref name="T"/>, so expensive events can be skipped.</summary>
    bool HasSubscribers<T>();
}
