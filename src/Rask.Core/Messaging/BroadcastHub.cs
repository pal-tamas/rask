using System.Collections.Concurrent;
using System.Text.Json;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core.Messaging;

/// <summary>
///     The in-process <see cref="IBroadcast" />: topic → subscriptions, delivered into each subscriber's session through
///     <see cref="IRenderHandle" /> (#1061). One per host, shared by every session.
/// </summary>
/// <remarks>
///     <para>
///         <b>A subscription is removed by its owner's unmount.</b> It registers on the component's LIFETIME token —
///         cancelled once, when the component unmounts — never on <c>Component.CancellationToken</c>, which inside an
///         event handler is linked to that dispatch and would end the subscription at the handler's timeout. That also
///         means the hub holds nothing for a session that has gone: its components unmount, and their subscriptions with
///         them.
///     </para>
///     <para>
///         <b>One delivery per session.</b> A message's subscribers are grouped by the session they render in, so a page
///         with three subscribed components handles the message in one dispatch and paints once. How the dispatch runs is
///         the host's: a server session queues it behind its WebSocket events, a WebAssembly session behind the browser's.
///     </para>
///     <para>
///         <b>Across hosts (#1115).</b> A topic declared with a <c>JsonTypeInfo</c> is also handed, serialized, to the
///         <see cref="IBroadcastBackplane" /> when one is registered. This host starts listening for a topic when its
///         first local subscriber arrives, and a message that comes back from another host goes through
///         <see cref="Deliver{T}" /> — delivered to this host's subscribers exactly as a local publish is.
///     </para>
/// </remarks>
/// <param name="backplane">The transport to the app's other hosts; <see langword="null" /> keeps every topic local.</param>
internal sealed class BroadcastHub(IBroadcastBackplane? backplane = null) : IBroadcast
{
    private readonly ConcurrentDictionary<(string Name, Type Type), Subscribers> _topics = new();

    // A cross-host topic is matched on the other side by NAME alone, so one name carries one type across the app.
    private readonly ConcurrentDictionary<string, Type> _crossHostTypes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _listening = new(StringComparer.Ordinal);

    public void Subscribe<T>(Component owner, Topic<T> topic, Action<T> handler) => Add(owner, topic, handler);

    public void Subscribe<T>(Component owner, Topic<T> topic, Func<T, Task> handler) => Add(owner, topic, handler);

    public ValueTask PublishAsync<T>(Topic<T> topic, T message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topic);
        cancellationToken.ThrowIfCancellationRequested();

        if (backplane is null || topic.CrossHost is not { } contract)
        {
            Deliver(topic, message);
            return default;
        }

        ClaimCrossHostName(topic);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, contract);
        Deliver(topic, message);
        return new ValueTask(backplane.PublishAsync(topic.Name, payload, cancellationToken));
    }

    /// <summary>The transport to the other hosts, when the app registered one.</summary>
    internal IBroadcastBackplane? Backplane => backplane;

    /// <summary>How many subscriptions <paramref name="topic" /> has right now.</summary>
    internal int SubscriberCount<T>(Topic<T> topic) =>
        _topics.TryGetValue((topic.Name, typeof(T)), out var subscribers) ? subscribers.Count : 0;

    /// <summary>Queues <paramref name="message" /> into the session of every current subscriber to <paramref name="topic" />.</summary>
    internal void Deliver<T>(Topic<T> topic, T message)
    {
        if (!_topics.TryGetValue((topic.Name, typeof(T)), out var subscribers))
        {
            return;
        }

        var snapshot = subscribers.Snapshot();
        if (snapshot.Length == 0)
        {
            return;
        }

        foreach (var session in snapshot.GroupBy(static s => s.Owner.RenderHandle))
        {
            var group = session.ToArray();
            if (session.Key is { } handle)
            {
                _ = handle.DeliverAsync(() => RunAsync(group, message));
            }
            else
            {
                // Mounted outside any session (a test render): nothing to queue behind, so it runs now.
                _ = RunAsync(group, message);
            }
        }
    }

    private void Add<T>(Component owner, Topic<T> topic, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(handler);

        if (owner.IsUnmountedInternal)
        {
            return;
        }

        if (backplane is not null && topic.CrossHost is not null)
        {
            Listen(topic);
        }

        var subscribers = _topics.GetOrAdd((topic.Name, typeof(T)), static _ => new Subscribers());
        var subscription = new Subscription(owner, handler);
        subscribers.Add(subscription);

        // The lifetime token, deliberately — see the type's remarks. Register runs the removal inline when the token is
        // already cancelled, so a subscribe racing the owner's unmount cannot leak.
        owner.LifetimeTokenInternal.Register(
            static state =>
            {
                var (list, item) = ((Subscribers, Subscription))state!;
                list.Remove(item);
            },
            (subscribers, subscription));
    }

    // Starts receiving the topic from the other hosts, once per name for the life of the hub. Never undone: a host
    // with no subscriber left just delivers to nobody, which costs less than re-subscribing on the next mount.
    private void Listen<T>(Topic<T> topic)
    {
        ClaimCrossHostName(topic);
        if (!_listening.TryAdd(topic.Name, true))
        {
            return;
        }

        var contract = topic.CrossHost!;
        backplane!.Subscribe(topic.Name, payload =>
        {
            T message;
            try
            {
                message = JsonSerializer.Deserialize(payload.Span, contract)!;
            }
            catch (JsonException ex)
            {
                // Another host running another version of the message type. Dropped rather than delivered half-read.
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning,
                    "Rask.Broadcast",
                    $"A {typeof(T).Name} message on '{topic.Name}' from another host could not be read, and was dropped",
                    ex);
                return;
            }

            Deliver(topic, message);
        });
    }

    private void ClaimCrossHostName<T>(Topic<T> topic)
    {
        var claimed = _crossHostTypes.GetOrAdd(topic.Name, typeof(T));
        if (claimed != typeof(T))
        {
            throw new InvalidOperationException(
                $"The cross-host topic '{topic.Name}' is declared for both {claimed.Name} and {typeof(T).Name}. A topic that "
                + "crosses hosts is matched by its name alone, so give each message type its own name.");
        }
    }

    private static async Task RunAsync<T>(Subscription[] group, T message)
    {
        foreach (var subscription in group)
        {
            if (subscription.Owner.IsUnmountedInternal)
            {
                continue;
            }

            try
            {
                switch (subscription.Handler)
                {
                    case Action<T> sync:
                        sync(message);
                        break;
                    case Func<T, Task> async:
                        await async(message).ConfigureAwait(false);
                        break;
                }

                // Marks the owner dirty; inside the session's dispatch this folds into the one render that ends it.
                subscription.Owner.StateHasChanged();
            }
#pragma warning disable CA1031 // One subscriber's fault must not stop the others, or the session.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Error,
                    "Rask.Broadcast",
                    $"A {typeof(T).Name} subscriber on {subscription.Owner.GetType().Name} threw",
                    ex);
            }
        }
    }

    // A class, not a record: removal finds THIS subscription, and two identical ones from one owner are still two.
    private sealed class Subscription(Component owner, Delegate handler)
    {
        public Component Owner { get; } = owner;

        public Delegate Handler { get; } = handler;
    }

    // Copy-on-write: a publish reads a stable array while subscribes and unmounts change the list under the lock.
    private sealed class Subscribers
    {
        private readonly object _gate = new();
        private Subscription[] _items = [];

        public int Count => Volatile.Read(ref _items).Length;

        public Subscription[] Snapshot() => Volatile.Read(ref _items);

        public void Add(Subscription item)
        {
            lock (_gate)
            {
                Volatile.Write(ref _items, [.. _items, item]);
            }
        }

        public void Remove(Subscription item)
        {
            lock (_gate)
            {
                var index = Array.IndexOf(_items, item);
                if (index >= 0)
                {
                    Volatile.Write(ref _items, [.. _items.AsSpan(0, index), .. _items.AsSpan(index + 1)]);
                }
            }
        }
    }
}
