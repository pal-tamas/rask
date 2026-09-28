using Rask.Cqrs;

namespace Rask.Site.Features;

// What a page asks for: the shipping events of ONE order. Matches runs per published notification, so it reads the
// notification and nothing else.
public sealed record WatchOrder(int Number) : ISubscription<OrderShipped>
{
    public bool Matches(OrderShipped shipped) => shipped.Number == Number;
}
