using Rask.Cqrs;

namespace Rask.Site.Features;

// --- Command with a result: mutate, publish an event, return the new value ---
public sealed record IncrementCounter(int By) : ICommand<int>;
