using Rask.Cqrs;

namespace Rask.Site.Features;

// The two events behind the subscriptions demo. Plain CQRS notifications: publishing one runs its handlers (it has
// none here) and reaches every open subscription for it.

// Goes to every subscriber.
public sealed record OrderPlaced(int Number, string Item) : INotification;

// About one order: [For<DemoOrder>] means it reaches only the subscriptions watching that order's number, each admitted
// by the watch policy below.
public sealed record OrderShipped([For<DemoOrder>] int Number) : INotification;

// What the key identifies. In an app this is the aggregate — Order — and one policy covers every event about it.
public sealed class DemoOrder;

// Who may watch an order. A demo lets anyone; an app checks the signed-in user, for instance
// (await Order.Find((Guid)key)).CustomerId == Current.UserId. With no policy at all, nobody may.
public sealed class WatchingDemoOrders : IWatchPolicy<DemoOrder>
{
    public Task<bool> CanWatchAsync(object key, CancellationToken cancellationToken) => Task.FromResult(true);
}
