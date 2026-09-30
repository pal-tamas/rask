using Rask.Cqrs;

namespace Rask.Site.Features;

// About one order.
public sealed record OrderShipped(int Number) : IEvent;
