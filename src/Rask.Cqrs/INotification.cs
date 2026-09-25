namespace Rask.Cqrs;

/// <summary>
/// Marks an event: something that has happened. Publishing it through
/// <see cref="IDispatcher.Publish{TNotification}"/> runs every
/// <see cref="INotificationHandler{TNotification}"/> for it <b>and</b> hands it to every open subscription —
/// <c>IDispatcher.Subscribe</c>, or a component's <c>QueryClient.Subscribe</c> — so one record reaches the
/// code that reacts and the screens that show it.
/// </summary>
/// <remarks>
/// The event itself stays plain. To watch the ones about one thing, write an
/// <see cref="ISubscription{TNotification}"/> record that says which those are, and an
/// <see cref="IWatchPolicy{TSubscription}"/> that says who may open it:
/// <code>
/// public sealed record OrderShipped(Guid OrderId, string Status) : INotification;
///
/// public sealed record WatchOrder(Guid OrderId) : ISubscription&lt;OrderShipped&gt;
/// {
///     public bool Matches(OrderShipped e) =&gt; e.OrderId == OrderId;
/// }
/// </code>
/// </remarks>
public interface INotification;
