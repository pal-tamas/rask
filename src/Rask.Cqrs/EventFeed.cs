using System.Collections.Concurrent;

namespace Rask.Cqrs;

/// <summary>
///     Every published event, handed to every open subscription for its type — and, where the subscription asked
///     for one thing in particular, only when it says the event is one of its own. One per container: the
///     process on a server, the tab in a browser.
/// </summary>
/// <remarks>
///     <para>
///         <b>Replay.</b> A new subscription first receives the most recent event it matches, so a page that
///         opens — or reconnects — after an export finished still shows "Download". Only a type somebody has subscribed
///         to is remembered, so a domain event nobody watches is never held; and at most
///         <see cref="CqrsOptions.ReplayCapacity" /> events are, oldest out first.
///     </para>
///     <para>
///         <b>Order.</b> Each delivery carries a sequence number and a listener drops anything older than what it has
///         seen, so a replay racing a fresh publish can never land after it and put the older value back.
///     </para>
/// </remarks>
internal sealed class EventFeed(CqrsExecutionOptions? options = null)
{

    private readonly ConcurrentDictionary<Type, Watched> _types = new();
    private readonly ConcurrentQueue<Watched> _replayOrder = new();
    private readonly int _replayCapacity = (options ?? CqrsExecutionOptions.Default).ReplayCapacity;
    private int _replayCount;
    private long _sequence;

    /// <summary>Delivers <paramref name="e" /> to the subscriptions watching it.</summary>
    public void Publish(IEvent e) => Deliver(e.GetType(), e);

    /// <summary>
    ///     Calls <paramref name="received" /> with each event of <paramref name="type" /> that
    ///     <paramref name="matches" /> accepts — starting with the most recent one, when there is one — until the result
    ///     is disposed. A null <paramref name="matches" /> takes every event of the type.
    /// </summary>
    /// <remarks>
    ///     Both <paramref name="matches" /> and <paramref name="received" /> run on the publisher's thread, so they must
    ///     be quick and must not throw: the subscriptions hand the event straight to a channel.
    /// </remarks>
    public IDisposable Listen(Type type, Func<IEvent, bool>? matches, Action<IEvent> received)
    {
        var watched = _types.GetOrAdd(type, static _ => new Watched());

        var listener = new Listener(matches, received);
        IEvent? replay = null;
        long sequence = 0;
        lock (watched.Gate)
        {
            watched.Listeners = [.. watched.Listeners, listener];

            // Newest first: the one entry this subscription would have wanted is the last it matches.
            for (var i = watched.Recent.Count - 1; i >= 0; i--)
            {
                var candidate = watched.Recent[i];
                if (matches is null || matches(candidate.Event))
                {
                    replay = candidate.Event;
                    sequence = candidate.Sequence;
                    break;
                }
            }
        }

        if (replay is not null)
        {
            listener.Offer(replay, sequence);
        }

        return new Unlisten(watched, listener);
    }

    /// <summary>How many subscriptions are listening for <paramref name="type" /> right now.</summary>
    internal int ListenerCount(Type type) =>
        _types.TryGetValue(type, out var watched) ? Volatile.Read(ref watched.Listeners).Length : 0;

    private void Deliver(Type type, IEvent e)
    {
        // Nobody has ever subscribed to this type: nobody to tell, and nothing worth remembering.
        if (!_types.TryGetValue(type, out var watched))
        {
            return;
        }

        Listener[] listeners;
        long sequence;
        lock (watched.Gate)
        {
            sequence = Interlocked.Increment(ref _sequence);
            watched.Recent.Add((e, sequence));
            listeners = watched.Listeners;
        }

        Remembered(watched);

        foreach (var listener in listeners.Where(listener => listener.Wants(e)))
        {
            listener.Offer(e, sequence);
        }
    }

    // Bounds the replay store across the whole feed: past capacity the oldest remembered event goes, whichever
    // type it belonged to, so no one busy event can hold the whole store.
    private void Remembered(Watched watched)
    {
        _replayOrder.Enqueue(watched);
        if (Interlocked.Increment(ref _replayCount) <= _replayCapacity)
        {
            return;
        }

        if (!_replayOrder.TryDequeue(out var oldest))
        {
            return;
        }

        Interlocked.Decrement(ref _replayCount);
        lock (oldest.Gate)
        {
            if (oldest.Recent.Count > 0)
            {
                oldest.Recent.RemoveAt(0);
            }
        }
    }

    private sealed class Watched
    {
        public readonly Lock Gate = new();

        // Oldest first, so the newest match is found by walking back from the end.
        public readonly List<(IEvent Event, long Sequence)> Recent = [];

        // Copy-on-write: a publish reads a stable array while subscriptions come and go under the gate.
        public Listener[] Listeners = [];
    }

    private sealed class Listener(Func<IEvent, bool>? matches, Action<IEvent> received)
    {
        private long _seen;

        public bool Wants(IEvent e) => matches is null || matches(e);

        public void Offer(IEvent e, long sequence)
        {
            var seen = Volatile.Read(ref _seen);
            while (sequence > seen)
            {
                var previous = Interlocked.CompareExchange(ref _seen, sequence, seen);
                if (previous == seen)
                {
                    received(e);
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
