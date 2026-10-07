using System.Collections.Concurrent;

namespace Talesmith.Events;

/// <summary>The default <see cref="IEventBus"/>: lock-free publishing over copy-on-write subscriber lists.</summary>
public sealed class EventBus : IEventBus
{
    private static int _nextTypeIndex;

    private readonly Lock _channelLock = new();
    private readonly ConcurrentQueue<IQueuedEvent> _queue = new();
    private IChannel?[] _channels = new IChannel?[32];

    public IDisposable Subscribe<T>(EventCallback<T> handler, int priority = 0, EventFilter<T>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Channel<T>().Add(new Subscriber<T>(handler, null, priority, filter));
    }

    public IDisposable SubscribeAsync<T>(AsyncEventCallback<T> handler, int priority = 0, EventFilter<T>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Channel<T>().Add(new Subscriber<T>(null, handler, priority, filter));
    }

    public bool Publish<T>(ref T e) => TryChannel<T>() is not { } channel || channel.Publish(ref e);

    public bool Publish<T>(T e) => Publish(ref e);

    public ValueTask<bool> PublishAsync<T>(T e, CancellationToken cancellationToken = default) =>
        TryChannel<T>() is { } channel ? channel.PublishAsync(e, cancellationToken) : ValueTask.FromResult(true);

    public void Enqueue<T>(in T e) => _queue.Enqueue(QueuedEvent<T>.Rent(this, e));

    public void DispatchQueued()
    {
        var count = _queue.Count;
        for (var i = 0; i < count && _queue.TryDequeue(out var queued); i++)
            queued.Dispatch();
    }

    public bool HasSubscribers<T>() => TryChannel<T>() is { HasSubscribers: true };

    private EventChannel<T>? TryChannel<T>()
    {
        var index = TypeIndex<T>.Value;
        var channels = Volatile.Read(ref _channels);
        return index < channels.Length ? (EventChannel<T>?)channels[index] : null;
    }

    private EventChannel<T> Channel<T>()
    {
        if (TryChannel<T>() is { } existing)
            return existing;

        lock (_channelLock)
        {
            var index = TypeIndex<T>.Value;
            var channels = _channels;
            if (index >= channels.Length)
            {
                var grown = new IChannel?[Math.Max(index + 1, channels.Length * 2)];
                channels.CopyTo(grown, 0);
                channels = grown;
            }

            if (channels[index] is not EventChannel<T> channel)
            {
                channel = new EventChannel<T>();
                channels[index] = channel;
            }

            Volatile.Write(ref _channels, channels);
            return channel;
        }
    }

    private static class TypeIndex<T>
    {
        public static readonly int Value = Interlocked.Increment(ref _nextTypeIndex) - 1;
    }

    private interface IChannel
    {
        bool HasSubscribers { get; }
    }

    private interface IQueuedEvent
    {
        void Dispatch();
    }

    private sealed class QueuedEvent<T> : IQueuedEvent
    {
        private static readonly ConcurrentBag<QueuedEvent<T>> Pool = [];

        private EventBus? _bus;
        private T _event = default!;

        public static QueuedEvent<T> Rent(EventBus bus, in T e)
        {
            var queued = Pool.TryTake(out var pooled) ? pooled : new QueuedEvent<T>();
            queued._bus = bus;
            queued._event = e;
            return queued;
        }

        public void Dispatch()
        {
            var bus = _bus!;
            var e = _event;
            _bus = null;
            _event = default!;
            Pool.Add(this);
            bus.Publish(ref e);
        }
    }

    private static class Cancellation<T>
    {
        private delegate bool Check(ref T e);

        private static readonly Check? IsCancelledCheck = typeof(ICancellableEvent).IsAssignableFrom(typeof(T))
            ? typeof(Cancellation<T>).GetMethod(nameof(Read), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(typeof(T)).CreateDelegate<Check>()
            : null;

        public static bool IsCancelled(ref T e) => IsCancelledCheck is { } check && check(ref e);

        private static bool Read<TEvent>(ref TEvent e) where TEvent : ICancellableEvent => e.IsCancelled;
    }

    private sealed record Subscriber<T>(EventCallback<T>? Handler, AsyncEventCallback<T>? AsyncHandler, int Priority, EventFilter<T>? Filter);

    private sealed class EventChannel<T> : IChannel
    {
        private readonly Lock _lock = new();
        private Subscriber<T>[] _subscribers = [];

        public bool HasSubscribers => Volatile.Read(ref _subscribers).Length > 0;

        public Subscription Add(Subscriber<T> subscriber)
        {
            lock (_lock)
            {
                var list = new List<Subscriber<T>>(_subscribers) { subscriber };
                list.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                Volatile.Write(ref _subscribers, [.. list]);
            }

            return new Subscription(() => Remove(subscriber));
        }

        public bool Publish(ref T e)
        {
            foreach (var subscriber in Volatile.Read(ref _subscribers))
            {
                if (subscriber.Handler is null || (subscriber.Filter is { } filter && !filter(in e)))
                    continue;

                subscriber.Handler(ref e);
                if (Cancellation<T>.IsCancelled(ref e))
                    return false;
            }

            return true;
        }

        public async ValueTask<bool> PublishAsync(T e, CancellationToken cancellationToken)
        {
            if (!Publish(ref e))
                return false;

            foreach (var subscriber in Volatile.Read(ref _subscribers))
            {
                if (subscriber.AsyncHandler is null || (subscriber.Filter is { } filter && !filter(in e)))
                    continue;

                await subscriber.AsyncHandler(e, cancellationToken).ConfigureAwait(false);
                if (Cancellation<T>.IsCancelled(ref e))
                    return false;
            }

            return true;
        }

        private void Remove(Subscriber<T> subscriber)
        {
            lock (_lock)
                Volatile.Write(ref _subscribers, _subscribers.Where(s => !ReferenceEquals(s, subscriber)).ToArray());
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
