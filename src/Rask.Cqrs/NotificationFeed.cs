using System.Collections.Concurrent;

namespace Rask.Cqrs;

/// <summary>
///     Every published notification, handed to every open subscription for its type — and, for a scoped one, only to
///     those watching its key. One per container: the process on a server, the tab in a browser.
/// </summary>
/// <remarks>
///     <para>
///         <b>Replay.</b> A new subscription first receives the last notification published for what it watches, so a
///         page that opens — or reconnects — after an export finished still shows "Download". Only a type somebody has
///         subscribed to is remembered, so a domain event nobody watches is never held; and at most
///         <see cref="CqrsOptions.ReplayCapacity" /> entries are, oldest out first.
///     </para>
///     <para>
///         <b>Order.</b> Each delivery carries a sequence number and a listener drops anything older than what it has
///         seen, so a replay racing a fresh publish can never land after it and put the older value back.
///     </para>
/// </remarks>
internal sealed class NotificationFeed(CqrsExecutionOptions? options = null)
{

    // A dictionary key cannot be null, and an unscoped subscription's key is.
    private static readonly object Unscoped = new();

    private readonly ConcurrentDictionary<Type, Watched> _types = new();
    private readonly ConcurrentQueue<(Watched Type, object Key, long Sequence)> _replayOrder = new();
    private readonly int _replayCapacity = (options ?? CqrsExecutionOptions.Default).ReplayCapacity;
    private int _replayCount;
    private long _sequence;

    /// <summary>Delivers <paramref name="notification" /> to the subscriptions watching it.</summary>
    public void Publish(INotification notification) => Deliver(notification.GetType(), notification);

    /// <summary>
    ///     Calls <paramref name="received" /> with each notification of <paramref name="type" /> for
    ///     <paramref name="key" /> — starting with the last one, when there is one — until the result is disposed.
    /// </summary>
    /// <remarks>
    ///     <paramref name="received" /> runs on the publisher's thread, so it must be quick and must not throw: the
    ///     subscriptions hand it straight to a channel.
    /// </remarks>
    public IDisposable Listen(Type type, object? key, Action<INotification> received)
    {
        var watched = _types.GetOrAdd(type, static t => new Watched());

        var listener = new Listener(key, received);
        (INotification Notification, long Sequence) replay;
        bool hasReplay;
        lock (watched.Gate)
        {
            watched.Listeners = [.. watched.Listeners, listener];
            hasReplay = watched.Last.TryGetValue(key ?? Unscoped, out replay);
        }

        if (hasReplay)
        {
            listener.Offer(replay.Notification, replay.Sequence);
        }

        return new Unlisten(watched, listener);
    }

    /// <summary>How many subscriptions are listening for <paramref name="type" /> right now.</summary>
    internal int ListenerCount(Type type) =>
        _types.TryGetValue(type, out var watched) ? Volatile.Read(ref watched.Listeners).Length : 0;

    private void Deliver(Type type, INotification notification)
    {
        // Nobody has ever subscribed to this type: nobody to tell, and nothing worth remembering.
        if (!_types.TryGetValue(type, out var watched))
        {
            return;
        }

        var scope = CqrsRegistry.FindNotificationScope(type);
        var key = scope?.KeyOf(notification) ?? Unscoped;

        Listener[] listeners;
        long sequence;
        lock (watched.Gate)
        {
            sequence = Interlocked.Increment(ref _sequence);
            watched.Last[key] = (notification, sequence);
            listeners = watched.Listeners;
        }

        Remembered(watched, key, sequence);

        foreach (var listener in listeners)
        {
            if (scope is null || Equals(listener.Key, key))
            {
                listener.Offer(notification, sequence);
            }
        }
    }

    // Bounds the replay store: past capacity, the oldest remembered entry goes — unless that key has been published
    // again since, in which case the queue holds a newer ticket for it and this one is stale.
    private void Remembered(Watched watched, object key, long sequence)
    {
        _replayOrder.Enqueue((watched, key, sequence));
        if (Interlocked.Increment(ref _replayCount) <= _replayCapacity)
        {
            return;
        }

        if (!_replayOrder.TryDequeue(out var oldest))
        {
            return;
        }

        Interlocked.Decrement(ref _replayCount);
        lock (oldest.Type.Gate)
        {
            if (oldest.Type.Last.TryGetValue(oldest.Key, out var stored) && stored.Sequence == oldest.Sequence)
            {
                oldest.Type.Last.Remove(oldest.Key);
            }
        }
    }

    private sealed class Watched
    {
        public readonly object Gate = new();
        public readonly Dictionary<object, (INotification Notification, long Sequence)> Last = [];

        // Copy-on-write: a publish reads a stable array while subscriptions come and go under the gate.
        public Listener[] Listeners = [];
    }

    private sealed class Listener(object? key, Action<INotification> received)
    {
        private long _seen;

        public object? Key { get; } = key;

        public void Offer(INotification notification, long sequence)
        {
            var seen = Volatile.Read(ref _seen);
            while (sequence > seen)
            {
                var previous = Interlocked.CompareExchange(ref _seen, sequence, seen);
                if (previous == seen)
                {
                    received(notification);
                    return;
                }

                seen = previous;
            }
        }
    }

    private sealed class Unlisten(Watched watched, Listener listener) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (watched.Gate)
            {
                var index = Array.IndexOf(watched.Listeners, listener);
                if (index >= 0)
                {
                    watched.Listeners = [.. watched.Listeners.AsSpan(0, index), .. watched.Listeners.AsSpan(index + 1)];
                }
            }
        }
    }
}
