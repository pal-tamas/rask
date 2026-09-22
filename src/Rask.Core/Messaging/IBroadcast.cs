namespace Rask.Core.Messaging;

/// <summary>
///     Publishes a message on a <see cref="Topic{T}" /> to every component subscribed to it, across every open session,
///     and re-renders each subscriber where it is — a new order appearing on every admin's open order list.
/// </summary>
/// <remarks>
///     <para>
///         <b>Subscribe in <c>Mount</c>, and nothing else.</b> A subscription lives exactly as long as the component
///         that owns it: when the component unmounts, the subscription is gone. There is nothing to dispose and no
///         <c>StateHasChanged</c> to call — the handler runs the way an event handler does, and the owner re-renders
///         after it.
///         <code>
///         protected override async Task Mount() =>
///             broadcast.Subscribe(this, Topics.Orders, order => _orders.Insert(0, order));
///         </code>
///     </para>
///     <para>
///         <b>Delivery.</b> A handler runs on its own session's dispatch queue, in order with that session's events, so it
///         never races an event handler over the component's state. <see cref="PublishAsync{T}" /> returns once every
///         subscribed session has the message queued; it does not wait for the renders. A session whose queue is full is
///         skipped for that message rather than holding the publisher up, and a session that is reconnecting still
///         applies the message and shows the result when it is back.
///     </para>
///     <para>
///         <b>At most once, this process only.</b> A message is delivered to the subscribers that exist when it is
///         published, once, with no replay for a component that subscribes later. Messages are live objects, never
///         serialized, and reach only the sessions this process holds. In a browser-WASM app that is the one tab.
///     </para>
/// </remarks>
public interface IBroadcast
{
    /// <summary>Queues <paramref name="message" /> for every current subscriber to <paramref name="topic" />.</summary>
    /// <param name="topic">The topic to publish on.</param>
    /// <param name="message">The message. Subscribers in different sessions receive the same instance.</param>
    /// <param name="cancellationToken">Cancels queuing; a message already queued for a session is still delivered.</param>
    /// <typeparam name="T">The topic's message type.</typeparam>
    ValueTask PublishAsync<T>(Topic<T> topic, T message, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs <paramref name="handler" /> for each message published on <paramref name="topic" /> for as long as
    ///     <paramref name="owner" /> is mounted, then re-renders <paramref name="owner" />.
    /// </summary>
    /// <param name="owner">The component the subscription belongs to — normally <c>this</c>.</param>
    /// <param name="topic">The topic to subscribe to.</param>
    /// <param name="handler">What to do with a message.</param>
    /// <typeparam name="T">The topic's message type.</typeparam>
    void Subscribe<T>(Component owner, Topic<T> topic, Action<T> handler);

    /// <inheritdoc cref="Subscribe{T}(Component, Topic{T}, Action{T})" />
    void Subscribe<T>(Component owner, Topic<T> topic, Func<T, Task> handler);
}
