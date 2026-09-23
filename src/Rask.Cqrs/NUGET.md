# Rask.Cqrs

**Source-generated, reflection-free CQRS / mediator for .NET.** Structure your app as queries,
commands and notifications, and dispatch them through a single injectable interface — with every
handler wired at **compile time**, so there is no runtime reflection and no assembly scanning. It
publishes clean under the WASM/AOT trimmer.

Standalone: the only dependency is `Microsoft.Extensions.DependencyInjection.Abstractions`. You do
**not** need the rest of Rask to use it.

## Install

```bash
dotnet add package Rask.Cqrs
```

## Use

Define messages and their handlers:

```csharp
public sealed record GetUser(int Id) : IQuery<User>;

public sealed class GetUserHandler(AppDb db) : IQueryHandler<GetUser, User>
{
    public Task<User> HandleAsync(GetUser q, CancellationToken ct) => db.Users.FindAsync(q.Id, ct);
}
```

Register once, then dispatch — the response type is inferred:

```csharp
builder.Services.AddRaskCqrs();

// ...
var user = await dispatcher.QueryAsync(new GetUser(42));   // returns User
```

`ICommand` / `ICommand<TResult>` dispatch the same way; `INotification` fans out to every handler
via `PublishAsync` — and to every open subscription, tRPC-style:

```csharp
await foreach (var placed in dispatcher.SubscribeAsync<OrderPlaced>(cancellationToken: ct))
    Console.WriteLine(placed.Customer);
```

Mark the property an event is about with `[For<Order>]` and it reaches only the subscribers watching that key, each
admitted by an `IWatchPolicy<Order>` — none registered means nobody may watch.

## Pipeline behaviors

Cross-cutting concerns (logging, validation, transactions, caching) are decorators — implement
`IPipelineBehavior<TRequest, TResult>` and register it. Behaviors run in registration order, the
first-registered wrapping outermost.

## Notes

- **Reflection-free / trim-safe** — a Roslyn generator emits the handler registry via a
  `[ModuleInitializer]`; nothing is discovered at runtime.
- **Host-agnostic** — the same `AddRaskCqrs()` works on a Rask Server or WASM host, or any plain
  .NET app.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/cqrs.md>
