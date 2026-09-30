using Rask.Cqrs;

namespace Rask.Site.Features;

// --- Notification: fanned out to every handler after the command runs ---
public sealed record CounterIncremented(int Value) : IEvent;
