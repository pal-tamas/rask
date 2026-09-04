using Rask.Cqrs;

namespace Rask.Example.Meta.SvelteKit.Features.Hello;

// In-memory, because a starter should run before it has a database. Swap it for a real store —
// `rask new --template react --data` scaffolds one.
public sealed class VisitCounter
{
    private int _visits;

    public int Visits => Volatile.Read(ref _visits);

    public int Record() => Interlocked.Increment(ref _visits);
}

public sealed class GetGreetingHandler(VisitCounter counter) : IQueryHandler<GetGreeting, Greeting>
{
    public Task<Greeting> HandleAsync(GetGreeting query, CancellationToken cancellationToken) =>
        Task.FromResult(new Greeting($"Hello, {query.Name}!", DateTimeOffset.UtcNow, counter.Visits));
}

public sealed class RecordVisitHandler(VisitCounter counter) : ICommandHandler<RecordVisit, int>
{
    public Task<int> HandleAsync(RecordVisit command, CancellationToken cancellationToken) =>
        Task.FromResult(counter.Record());
}
