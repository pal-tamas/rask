using Rask.Cqrs;

namespace Rask.Site.Features;

// --- Query: read the current count + recent pipeline log ---
public sealed record GetCounterState : IQuery<CounterState>;
