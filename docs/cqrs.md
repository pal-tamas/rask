# CQRS (`Rask.Cqrs`)

`Rask.Cqrs` is an opt-in, **source-generated** CQRS/mediator. You define messages — queries, commands
and notifications — and their handlers, then dispatch through `IDispatcher`. The generator wires every
handler at compile time, so dispatch does **no runtime reflection and no assembly scanning**. That is
the whole point: it publishes clean under the WASM/AOT trimmer, where a reflection-based mediator
cannot. It is standalone — it depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`
and works in any .NET app, not just Rask.

> Rask itself has no data layer and encourages **vertical slices** (see [data-access.md](data-access.md)).
> CQRS is the natural way to structure those slices: each slice owns its query/command and handler.
> Reach for it when that structure earns its keep — a small app is fine calling services directly.

```bash
dotnet add package Rask.Cqrs
```

## The four message shapes

| Interface | Meaning | Handler | Returns |
| --- | --- | --- | --- |
| `IQuery<TResult>` | read, no side effects | `IQueryHandler<TQuery, TResult>` | `TResult` |
| `ICommand` | write, no value | `ICommandHandler<TCommand>` | — |
| `ICommand<TResult>` | write, returns a value | `ICommandHandler<TCommand, TResult>` | `TResult` |
| `INotification` | event, fanned out to many | `INotificationHandler<TNotification>` | — |

```csharp
public sealed record GetCounterState : IQuery<CounterState>;

public sealed class GetCounterStateHandler(CqrsCounterStore store)
    : IQueryHandler<GetCounterState, CounterState>
{
    public Task<CounterState> HandleAsync(GetCounterState query, CancellationToken ct) =>
        Task.FromResult(new CounterState(store.Count, store.Log));
}
```

## Register and dispatch

Call `AddRaskCqrs` once at startup. It is **host-agnostic** — the same call works on the Rask Server
and WASM hosts (the dispatcher captures the per-session scope on Server, the root scope on WASM), so
you never split registration per host. All discovered handlers are registered for you; you never list
them.

```csharp
builder.Services.AddRaskCqrs();
```

Inject `IDispatcher` — or the read-only `IQueryDispatcher` / write-only `ICommandDispatcher` when a
type only reads or only writes — and dispatch. The result type is inferred from the message:

```csharp
public sealed class CounterView(IDispatcher dispatcher) : Component
{
    private CounterState _view = new(0, []);

    protected override async Task OnMountAsync() =>
        _view = await dispatcher.QueryAsync(new GetCounterState(), CancellationToken);

    private async Task IncrementAsync()
    {
        await dispatcher.SendAsync(new IncrementCounter(1), CancellationToken); // ICommand<int>
        _view = await dispatcher.QueryAsync(new GetCounterState(), CancellationToken);
    }
}
```

## Notifications

A command handler can publish an `INotification`; every registered `INotificationHandler` runs. Choose
`Sequential` (default) or `WhenAll` fan-out via `CqrsOptions.NotificationPublishStrategy`. Handlers are
matched by the notification's **concrete runtime type** (a handler declared against a base type is not
invoked for a derived one — see Limitations), their run order is deterministic but not the declaration
order, so don't depend on it, and publishing a notification that has no handlers is a no-op.

```csharp
public sealed class IncrementCounterHandler(CqrsCounterStore store, IPublisher publisher)
    : ICommandHandler<IncrementCounter, int>
{
    public async Task<int> HandleAsync(IncrementCounter command, CancellationToken ct)
    {
        var value = store.IncrementBy(command.By);
        await publisher.PublishAsync(new CounterIncremented(value), ct);
        return value;
    }
}
```

## Pipeline behaviors (decorators)

Behaviors are the extension point for cross-cutting concerns — logging, validation, transactions,
caching. `Rask.Cqrs` ships **none**: you implement `IPipelineBehavior<TRequest, TResult>` and register
it. Behaviors run as an onion in **registration order** (first-registered is outermost); call `next`
to continue or return without it to short-circuit. A void `ICommand` flows through as `TResult = Unit`,
so one behavior shape covers everything.

```csharp
public sealed class DispatchLogBehavior<TRequest, TResult>(CqrsCounterStore store)
    : IPipelineBehavior<TRequest, TResult>
{
    public Task<TResult> HandleAsync(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
    {
        store.Note($"⚙ dispatch {typeof(TRequest).Name}");
        return next();
    }
}

// register: open-generic wraps every request, closed wraps one request/result pair
builder.Services.AddRaskCqrs(o => o.AddOpenBehavior(typeof(DispatchLogBehavior<,>)));
```

## It all fits together

The demo below is one vertical slice — the query, the result-command, the notification the command
publishes, and the logging behavior above — all dispatched reflection-free. Click **Increment** to
watch the pipeline log build up.

<!-- demo:cqrs-counter -->

## Diagnostics

The generator validates your handlers at compile time:

- **RASK028** (error) — a query or command has more than one handler; dispatch would be ambiguous.
- **RASK029** (warning) — a handler is an open generic or has no public constructor, so it can't be
  registered and is skipped.

A request with **no** handler is not a compile error (the handler may live in another assembly) — it
throws a clear `InvalidOperationException` at dispatch time.

## Limitations

These follow directly from the reflection-free, compile-time design — the same thing that makes it
trim-safe:

- **Handlers must live in a loaded assembly.** Registration is driven by a per-assembly
  `[ModuleInitializer]`, which runs when the CLR first loads that assembly. If your handlers are in a
  separate library that nothing touches before `AddRaskCqrs()`, they won't be registered. In practice a
  Rask/Blazor app already uses types from its handler assemblies at startup; if yours doesn't, reference
  any type from that assembly (or keep handlers in the app assembly).
- **One handler per query/command, app-wide.** RASK028 catches duplicates within a compilation. Two
  handlers for the *same* request type in *different* assemblies aren't diagnosed (no whole-program
  view) — keep each query/command's handler unique across the whole app.
- **Notifications match the concrete published type.** There is no base-type/polymorphic fan-out
  (that would need runtime type-hierarchy reflection). Declare a handler for the exact `INotification`
  type you publish.

## Trimming

Every handler's constructor is preserved with a generated `[DynamicDependency]`, and dispatch is a
compile-time closed-generic map, so a WASM app using `Rask.Cqrs` publishes with **zero IL warnings**.

## Testing

Handlers are plain classes — unit-test them directly, no dispatcher required. To test wiring, register
`AddRaskCqrs` into a `ServiceCollection`, build the provider, and dispatch. See
[`tests/Rask.Cqrs.Tests`](../tests/Rask.Cqrs.Tests) and the generator tests in
[`tests/Rask.Cqrs.Generators.Tests`](../tests/Rask.Cqrs.Generators.Tests).
