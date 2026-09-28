namespace Rask.Site.Features;

public sealed record CounterState(int Count, IReadOnlyList<string> Log);
