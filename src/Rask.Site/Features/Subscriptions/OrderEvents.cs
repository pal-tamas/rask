using Rask.Cqrs;

namespace Rask.Site.Features;

// The two events behind the subscriptions demo. Plain CQRS notifications: publishing one runs its handlers (it has
// none here) and reaches every open subscription for it.

// Goes to every subscriber.
public sealed record OrderPlaced(int Number, string Item) : INotification;

// About one order.
public sealed record OrderShipped(int Number) : INotification;

// What a page asks for: the shipping events of ONE order. Matches runs per published notification, so it reads the
// notification and nothing else.
public sealed record WatchOrder(int Number) : ISubscription<OrderShipped>
{
    public bool Matches(OrderShipped shipped) => shipped.Number == Number;
}

// Who may open it. A demo lets anyone; an app checks the signed-in user, for instance
// (await Order.Find(watch.Number)).CustomerId == Current.UserId. With no policy at all, nobody may.
public sealed class WatchingOrders : IWatchPolicy<WatchOrder>
{
    public Task<bool> CanWatchAsync(WatchOrder subscription, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
