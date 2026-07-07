using Rask.Cqrs;

namespace Company.RaskServer;

// A CQRS query and its handler. Rask.Cqrs discovers the handler at build time (source-generated,
// reflection-free) so a single AddRaskCqrs() in Program.cs registers it — no manual wiring here.
// Dispatch it with IDispatcher.DispatchAsync(new GreetingQuery(...)); the result type is inferred
// from IQuery<string>. Add more IQuery<T>/ICommand/ICommand<T> messages the same way. See docs/cqrs.md.
public sealed record GreetingQuery(string Name) : IQuery<string>;

public sealed class GreetingQueryHandler : IQueryHandler<GreetingQuery, string>
{
    public Task<string> HandleAsync(GreetingQuery query, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(query.Name) ? "world" : query.Name.Trim();
        return Task.FromResult($"Hello, {name}!");
    }
}
