using Microsoft.AspNetCore.Authorization;
using Rask.Cqrs;

namespace Company.RaskServer.Features.Hello;

// In-memory, because a starter should run before it has a database. Swap it for a real store —
// `rask new --template react --data` scaffolds one.
public sealed class VisitCounter
{
    private int _visits;

    public int Visits => Volatile.Read(ref _visits);

    public int Record() => Interlocked.Increment(ref _visits);
}

// Public on purpose: the landing page asks for it before anybody has an account. Every other message
// needs a signed-in caller, so leave [AllowAnonymous] off anything you add unless it is meant for anyone.
[AllowAnonymous]
public sealed class GetGreetingHandler(VisitCounter counter) : IQueryHandler<GetGreeting, Greeting>
{
    public Task<Greeting> Handle(GetGreeting query) =>
        Task.FromResult(new Greeting($"Hello, {query.Name}!", DateTimeOffset.UtcNow, counter.Visits));
}

[AllowAnonymous]
public sealed class RecordVisitHandler(VisitCounter counter) : ICommandHandler<RecordVisit, int>
{
    public Task<int> Handle(RecordVisit command) =>
        Task.FromResult(counter.Record());
}
