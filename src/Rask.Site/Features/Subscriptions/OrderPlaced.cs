using Rask.Cqrs;

namespace Rask.Site.Features;

// One of the two events behind the subscriptions demo (OrderShipped is the other). Plain CQRS events: publishing one runs its handlers (it has
// none here) and reaches every open subscription for it.

// Goes to every subscriber.
public sealed record OrderPlaced(int Number, string Item) : IEvent;
