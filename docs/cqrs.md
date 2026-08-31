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

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Cqrs.Off());
> ```

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

Inject `IDispatcher` and say which of the three things you are doing: `QueryAsync` asks for data,
`SendAsync` tells the system to do something, `PublishAsync` announces that something happened. The
result type is inferred from the message, so you never state it.

```csharp
public sealed partial class CounterView(IDispatcher dispatcher) : Component
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
public sealed class IncrementCounterHandler(CqrsCounterStore store, IDispatcher dispatcher)
    : ICommandHandler<IncrementCounter, int>
{
    public async Task<int> HandleAsync(IncrementCounter command, CancellationToken ct)
    {
        var value = store.IncrementBy(command.By);
        await dispatcher.PublishAsync(new CounterIncremented(value), ct);
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

## Remote dispatch — a client and a server (`Rask.Cqrs.Client` / `Rask.Cqrs.Server`)

A page running in the browser reaches its server through the **same `IDispatcher` call** it already
makes in-process. There is no `HttpClient` at the call site, no `/api/*` endpoint to write, and nothing
on a message marks it as remote — you write a record and a handler exactly as above, and *where the
handler lives* decides where it runs.

One package and one line per half. Neither references the other, so a browser bundle cannot compile the
endpoint code and the server never carries the browser transport:

```csharp
// The browser half. Every message it dispatches goes to the server.
host.Services.AddRaskCqrsClient();

// The server.
builder.Services.AddRaskCqrsServer();
...
app.MapRaskCqrs();
```

**`rask new --wasm` scaffolds all of it.** The [one-project build](render-modes.md) compiles one set of
sources into both halves, so the message records are shared by construction — there is no Shared project
to put them in any more. What is left is keeping the two transports apart, which the csproj says in one
line each:

```xml
<!-- The bundle gets this one; the server must not. It is the half that CALLS the endpoints the
     server answers, and a plain PackageReference would ship it into the process answering them. -->
<RaskBrowserPackageReference Include="Rask.Cqrs.Client" Version="..."/>

<!-- The bundle has no Program.cs of its own — that file is the server's, and the companion excludes
     it — so this names the type whose Configure(IServiceCollection) runs before the app does. -->
<RaskBrowserStartup>$(RootNamespace).Browser.BrowserStartup</RaskBrowserStartup>
```

That startup type lives under `Browser/`, which is the only place it can: a browser-only reference is
absent from the server by design, so a file using it has to be somewhere the server does not compile.
`Browser/` is the mirror of `Server/` — see [render modes](render-modes.md#one-project).

Keep your handlers under `Server/`, which the browser half does not compile. That is what keeps a
connection string, a table name or a pricing rule out of a download anybody can read.

> **Without `--auth`, the scaffold sets `RequireAuthenticatedUser = false` and says why.** The default is
> on, and that is right for an app that has a sign-in — but an app with no authentication to require
> would answer 401 to every message, and the failure reads as broken transport rather than as the secure
> default working. Add a cookie or JWT scheme and delete the argument.

**A client is a pure client.** Every request message it dispatches travels; a stray client-side handler
can never quietly intercept one. Notifications are the deliberate exception — they fan out, so a
client's own handlers still run *and* the notification travels.

### Two endpoints, not one per message

`GET` and `POST` on `/_rask/cqrs/request/{name}`. The verb carries what `IQuery` and `ICommand` already
declare in C#: a query is safe and idempotent so it is a GET and can be cached; anything that mutates is
a POST. So a command is **405 on GET** and cannot be triggered by a URL, a prefetch or a link scanner. A
query too long for a URL falls back to POST automatically, with an identical result.

Because the name is a route segment, logs, metrics and rate-limit partitions get it for free.
`MapRaskCqrs()` returns the endpoint group, so `.RequireRateLimiting(...)`, CORS or output caching is a
one-line addition.

### It fails closed

- **Authenticated by default.** `[AllowAnonymous]` on the handler is the only way past;
  `[Authorize]` supplies a policy *and* roles, both enforced.
- **An anonymous caller cannot enumerate your messages.** A real name and a typo get the same answer, so
  the endpoint can't be walked to discover what the app has.
- **Both verbs require the `X-Rask-Cqrs` header**, which no cross-site markup can set — adding the GET
  surface adds no cross-site trigger.
- **Handler exceptions become RFC 9457 `problem+json`** with no exception text unless you opt in
  (`IncludeExceptionDetail`): an exception message is written for an operator, not a browser, and
  routinely names tables, paths and credentials.

Failure to *arrive* is the one thing remote dispatch adds to the in-process call, and it is a
`RemoteDispatchException` — a null `StatusCode` means the request never reached the server.

### `[LocalOnly]`

Keeps a message off the wire entirely, and on an **interface** covers a whole family. This matters more
than it looks: `IBackgroundJob` and `IOutboxEvent` both derive from `ICommand`, so without it every job payload and
outbox event in the app would become an internet-reachable endpoint.

It is also **how a client keeps a message in-process.** "A client is a pure client" is not a figure of
speech — `AddRaskCqrsClient()` replaces the invoker for *every* request message it has a contract for, so
a handler sitting in the client project stops being reached and the message goes to the server instead.
If a client genuinely owns a message end to end — a browser-local counter, an offline queue, anything the
server has no handler for — mark it `[LocalOnly]` and it never leaves:

```csharp
[LocalOnly]                                        // stays in the browser
public sealed record IncrementLocalCounter(int By) : ICommand<int>;
```

Without it the dispatch travels, the server answers **404** (it has no handler for that name), and the
failure looks like a transport problem rather than the design decision it is.

### Files, both directions

**A message declares its file as a `RaskFile`** — the same type a file input hands a component. So the
file a user picked is passed straight to the handler, with nothing to convert and nothing to learn:

```csharp
public sealed record AttachReceipt(int OrderId, RaskFile File) : ICommand;

// The call site. Identical whether this page is server-rendered or running in the browser.
await dispatcher.SendAsync(new AttachReceipt(orderId, picked));

// Download: the file the handler returned, saved by the browser.
navigator.Download(await dispatcher.QueryAsync(new ExportOrders(year)));
```

The handler receives a `RaskFile` too, and reads it exactly as it would in-process:

```csharp
public sealed class AttachReceiptHandler : ICommandHandler<AttachReceipt>
{
    public async Task HandleAsync(AttachReceipt command, CancellationToken cancellationToken)
    {
        await using var stream = command.File.OpenReadStream(command.File.Size, cancellationToken);
        // ...
    }
}
```

Where the message runs in-process the handler simply gets the picked file. Where it travels, the
generated codec carries the bytes and hands the handler a `RaskFile` over what arrived — so the *host*
changes and the code does not. Every host reads a `RaskFile` in bounded slices (the browser ones
through `Blob.slice`), and a download is streamed back headers-first.

Bounded by the server's `MaxUploadBytes` and `MaxFileCount`.

**`ChunkedUploadThreshold` is load-bearing in the browser, not a tuning knob.** `fetch` has no request
streaming, so a browser reads a whole request body into memory before sending it — a single-shot
multipart upload of a 500 MB file costs 500 MB *in the tab*. Above the threshold the file goes up in
bounded pieces first and the message follows carrying only the session id, which is what keeps the
request small as well as the read. Raising it raises the browser's peak memory by the same amount.

**A dropped chunk resumes.** Every chunk response echoes `X-Rask-Upload-Offset`, and a chunk that does
not follow on from what the server holds is answered `409` carrying the offset it *does* hold — 409
rather than 400 because the request is well-formed, it is just out of step. The client restarts from
that offset, bounded by a small retry count per file, so one lost chunk does not fail an upload that
the protocol can recover.

The limit is **in-session**: a browser's `File` handle dies with the page, so resuming across a reload
is not possible and the upload starts again.

### Wire codecs, and RASK053

The codecs are **source-generated** `Utf8JsonWriter`/`Utf8JsonReader` code, not reflection, so remote
dispatch publishes clean under the WASM/AOT trimmer. **RASK053** reports a message or result shape that
has no wire encoding. It reaches no existing code: codecs are generated only for a compilation that
references one of the two transport packages, so an app using `Rask.Cqrs` in-process is unconstrained.

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
[`tests/Rask.Batteries.Generators.Tests`](../tests/Rask.Batteries.Generators.Tests).
