namespace Rask.Site.Features;

// Who may open it. A demo lets anyone; an app checks the signed-in user, for instance
// (await Order.Where(o => o.Number == watch.Number).First())?.CustomerId == Current.UserId. With no policy at all, nobody may.
public sealed class WatchingOrders : IWatchPolicy<WatchOrder>
{
    public Task<bool> CanWatchAsync(WatchOrder subscription, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}
