namespace Rask.Cqrs;

/// <summary>
///     Asks for the <typeparamref name="TNotification" />s about one thing — this order, this user's export — and says
///     which those are. The fourth message shape, beside <see cref="IQuery{TResult}" /> and <see cref="ICommand" />.
/// </summary>
/// <remarks>
///     <para>
///         A record, like every other message, so what it asks for is typed and compared structurally:
///     </para>
///     <code>
///     public sealed record WatchOrder(Guid OrderId) : ISubscription&lt;OrderShipped&gt;
///     {
///         public bool Matches(OrderShipped e) =&gt; e.OrderId == OrderId;
///     }
///
///     var shipped = QueryClient.Subscribe(new WatchOrder(Id));
///     </code>
///     <para>
///         Who may open it is an <see cref="IWatchPolicy{TSubscription}" />, asked in the subscriber's own scope before
///         anything is delivered. With no policy registered nobody may open it, so a forgotten one is a broken screen
///         rather than one customer reading another's events.
///     </para>
///     <para>
///         A subscription that is about nothing in particular — every order placed, on an admin board — needs no record:
///         <c>QueryClient.Subscribe&lt;OrderPlaced&gt;()</c> watches the type itself.
///     </para>
/// </remarks>
/// <typeparam name="TNotification">The notification this subscription carries.</typeparam>
public interface ISubscription<in TNotification>
    where TNotification : INotification
{
    /// <summary>Whether <paramref name="notification" /> is one of the ones this subscription asked for.</summary>
    /// <remarks>
    ///     Runs for each published notification of the type, on the publisher's thread, so it reads the notification and
    ///     nothing else: no database, no service, no await. What the subscriber is <em>allowed</em> to see is the policy's
    ///     business, and it is decided once, when the subscription opens.
    /// </remarks>
    /// <param name="notification">The notification just published.</param>
    bool Matches(TNotification notification);
}
