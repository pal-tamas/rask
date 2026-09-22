# Lifecycle

Every `Component` can override five lifecycle hooks. Each is one `Task`-returning method, runs at a
well-defined point around a render, and behaves identically on the Server and WASM hosts (only the transport
differs). This page documents the hooks, their order, the async rules, and the gotchas.

See also: [routing.md](routing.md) for how route/query params drive `OnUpdated`, and the README *Lifecycle
reference* table for a one-glance summary.

## The hooks

All five are `protected virtual` on `Component`:

```csharp
protected override async Task OnMount()       => _items = await LoadItems();          // once, before the first render
protected override async Task OnUpdated()     => _product = await Product.Find(Id);   // new props arrived (and on mount)
protected override async Task OnFirstRendered() => await _map.Init(_center);            // once, after the first render
protected override async Task OnRendered()    => await _map.Refresh(_markers);        // after every render
protected override async Task OnUnmount()     => await _socket.Close();               // once, on the way out
```

There is one hook per moment, not a synchronous and an asynchronous twin. The part of a hook before its first
`await` runs synchronously — before the first render, for `OnMount` — so work that used to go in a separate
synchronous hook simply goes above the `await`:

```csharp
protected override async Task OnMount()
{
    feed.Updated += StateHasChanged;                        // now, before the first render
    _items = await Cache.Remember("catalog", LoadItems);    // later — the component re-renders when it lands
}
```

A hook with nothing to await is still written `async`; the compiler does not warn about it.

## Order

| Hook              | When |
|-------------------|----------------------------------------------------------------------------------------------------|
| `OnMount`         | **Once**, before the instance's first render. |
| `OnUpdated`       | On the **first render**, and on any later render where a bound prop / route or query param **actually changed**. |
| `OnFirstRendered` | **Once**, after the first render is in the page — where browser work that needs the elements starts. |
| `OnRendered`      | After **every** render, the first included (after `OnFirstRendered`). |
| `OnUnmount`       | **Once**, on disposal (navigation away, parent subtree torn down, session teardown). Children unmount before parents (depth-first). |

So a component's life reads:

```
first time:  OnMount → OnUpdated → Render → OnFirstRendered → OnRendered
new props:   OnUpdated → Render → OnRendered
own state:   Render → OnRendered
leaving:     OnUnmount   (its CancellationToken is cancelled right after)
```

### Live probe

The component below counts every hook and re-renders so you can watch the order. **Trigger re-render** fires a
bare event-handler render — note it re-runs `OnRendered` but does **not** re-fire `OnMount` / `OnUpdated` (nothing the
component is bound to changed), and `OnFirstRendered` stays at one:

<!-- demo:lifecycle-hooks -->

### Mount / unmount cycle

Toggle the probe in and out of the tree to watch `OnUnmount` fire (children before parents). The log is held by the
parent, so it survives the probe's unmount:

<!-- demo:lifecycle-cycle -->

A typical async-data page uses `OnMount` to fetch once and renders a placeholder until it lands:

```csharp
[Route("/weather")]
public sealed partial class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMount() =>
        _forecasts = await service.GetForecasts();

    protected override Component? Render() =>
        _forecasts is null
            ? P[Em["Loading..."]]
            : Table[/* render rows */];
}
```

On the **Server host the initial `GET` waits for that fetch**, so the first response carries the
forecasts rather than the placeholder — which is what a crawler, a cache and the user's first paint
all see. The placeholder still renders whenever the page mounts later (a client-side navigation), and
still shows if the fetch outlives the budget, in which case the page keeps its live session and
finishes loading over the socket. See [Live pages](render-modes.md#the-initial-get-waits-for-your-data).

Work you deliberately detach from the hook is **not** waited on. A poll loop started with
`_ = Poll()` returns from `OnMount` immediately, so the response goes out and the loop
keeps pushing over the live connection:

```csharp
protected override async Task OnMount()
{
    await Read();    // awaited: the GET waits for this
    _ = Poll();      // detached: it must not hold the response open
}
```

Neither call passes a cancellation token: both run inside the component's own work and are cancelled when it
unmounts.

### When `OnUpdated` refires

`OnUpdated` fires on the first render and whenever a value the component is bound to **actually changes** —
including:

- A parent passing a different value for a chain step (a prop).
- A `[RouteParam]` / `[QueryParam]` value changing because the URL changed.
- A **reused routed page** whose URL **path** changes (the router keeps the instance and re-binds it rather than
  remounting).

What does **not** refire it: a bare event-handler re-render. Clicking a button that mutates a local field re-renders
the component but does **not** re-fire `OnUpdated` — nothing the component is bound to changed. (`Key` is a
reconciliation identity, not a reactive prop, so a key change doesn't fire `OnUpdated` either; it mounts a fresh
instance.)

### Do not run an unbounded loop in `OnMount`

The first render waits on the task a lifecycle hook hands back. That is right for *load the data this page
shows* and wrong for *run until this component goes away* — a `while (!ct.IsCancellationRequested)` loop
awaited inside `OnMount` never returns, so the render never settles. It waits out its whole budget and
is then reported as timed out; under [prerendering](prerendering.md) the page is skipped entirely and ships
to a crawler as a boot shell.

Splitting the loop does not rescue it either. Letting the hook return before the first tick paints an empty
widget, and starting the loop detached lets it outlive the render pass and re-render against a session scope
that has already been disposed.

Put ongoing work in a **service with its own lifetime** and have the component subscribe to it — which is what
[Background service](#background-service) below shows. The component's own hooks then do what they are for:
subscribe on mount, unsubscribe on unmount.

## Async rules

The hooks install a synchronization context so each `await` inside a hook triggers an automatic re-render after
the continuation, plus one terminal re-render on completion — you get "mutate state after the await and it paints"
without calling `StateHasChanged()` by hand. The runtime coalesces these into one payload per handler dispatch.

```csharp
protected override async Task OnMount()
{
    // placeholder shows here
    _data = await Load();
    // auto re-render after the await → real data paints, no StateHasChanged()
}
```

**`OnRendered` is loop-safe.** The terminal auto re-render is a *publish-only* walk: it does **not** re-fire
`OnRendered` on components that have already rendered at least once. That's what keeps a `OnRendered` hook which awaits a
next-frame side effect (e.g. drawing a chart, or a scoped-JS call) from looping on itself. Newly-mounted children on
the same walk still get their `OnFirstRendered` and `OnRendered`.

```csharp
protected override async Task OnRendered() =>
    await js.InvokeVoidAsync("Rask.CodeSample.rendered");
    // re-render from another component won't re-fire this — no loop
```

## Gotcha: a faulted async hook takes the page, not the component

**If an async hook faults, it trips the nearest `ErrorBoundary` — and in a live app there is always one.** The host
wraps your `App` in an implicit root boundary, and every component is stamped with the boundary above it during the
render walk, so a faulting `OnMount` / `OnUpdated` / `OnRendered` renders that boundary's fallback
rather than logging quietly.

The practical symptom is therefore the opposite of what you might expect: not a component stuck forever on a loading
placeholder, but **the whole page replaced by an error page** — because the boundary that caught it is the root one,
unless you put a closer boundary in the way.

```csharp
// Without a boundary of your own, a throw here replaces the entire document.
protected override async Task OnMount() => _rows = await api.LoadAsync();

// With one, the blast radius is the subtree you chose.
ErrorBoundary.Fallback((ex, retry) => Div[
    P["Could not load the rows."],
    Button.OnClick(retry)["Try again"]
])[
    RowList()
]
```

Two things follow:

- **Scope the damage yourself.** An `ErrorBoundary` around the risky subtree keeps the rest of the page alive, and its
  `Fallback` receives a `retry` callback that clears the error and re-renders that subtree. A `try/catch` inside the
  hook is still the right tool when you want to render an error *state* rather than a fallback.
- **The root error page offers `Try again` as well as `Reload this page`.** The first clears the error and re-renders
  in place, keeping the session, the state and the scroll position — enough for the common case, a handler that threw
  and damaged nothing. A render that faults deterministically simply lands back on the error page, and then the reload
  is what you want.

The initial GET for a page whose render faulted answers **500**, not 200 — the body is still the error page, so both
buttons work.

`Console.Error` only comes into it when there is genuinely no boundary — a component rendered outside a live render
context. In a live app that path is unreachable, so do not go looking there for a fault you can see on screen.

## Gotcha: don't `StateHasChanged()` in unmount

When `OnUnmount` runs, the component's lifetime `CancellationToken` is still **live** — it's
cancelled immediately *after* the hook returns. But the component is already leaving the tree, so calling
`StateHasChanged()` from inside an unmount hook is a **no-op** by design (it's been flagged unmounted before the hook
fires). Don't request a render from unmount.

```csharp
protected override async Task OnUnmount()
{
    route.Changed -= StateHasChanged;   // typical: tear down subscriptions
    // do NOT call StateHasChanged() here — the component is leaving the tree
}
```

## Disposal: `IDisposable` / `IAsyncDisposable`

Components that implement `IDisposable` or `IAsyncDisposable` get their `Dispose` / `DisposeAsync` called by the
framework when they leave the render tree. Use it to release timers, subscriptions, or any handle you took out in
`OnMount`. Disposal walks children depth-first, so nested disposables tear down bottom-up.

Mount, then unmount — the sync probe's `Dispose()` runs as the parent's diff removes it from the tree:

<!-- demo:disposal-sync -->

The async variant is awaited on its own dispatch path; the log entry shows up after the next render cycle resolves the
continuation:

<!-- demo:disposal-async -->

## `OnUnmount` vs `IDisposable`

`OnUnmount` is the framework-side cleanup signal. It fires **before** the lifetime
`CancellationToken` is cancelled, so cleanup code can still observe the token. Reach for it when the resource is
conceptually a *lifecycle hook* (unsubscribe from an event, stop a timer you started in `OnMount`) and reserve
`IDisposable` for things you would dispose anyway in non-Rask code (file handles, HTTP responses, DB connections):

<!-- demo:disposal-unmount -->

## Cancellation tied to component lifetime

Every component exposes a `protected CancellationToken CancellationToken`. It's allocated lazily (a component that
never reads it pays nothing) and cancelled exactly once when the component is unmounted. Pass it into `HttpClient`
calls, `Task.Delay`, or any cancellable async work started in a lifecycle hook so it aborts cleanly when the user
navigates away:

```csharp
public sealed partial class CancellationProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override async Task OnMount()
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2500), CancellationToken);
            Log($"#{InstanceId} completed");
        }
        catch (OperationCanceledException)
        {
            Log($"#{InstanceId} cancelled");
        }
    }
}
```

The framework cancels the token **before** disposing the subtree, so awaits unwind via `OperationCanceledException`
before `Dispose` runs and the unmount hooks fire. Cooperation is required: the framework only *signals* the token — it
doesn't abort blocking calls. Thread the token through anything you want cancelled.

Mount the probe to start a 2.5-second `Task.Delay` inside `OnMount`; click **Unmount** before it settles to
cancel — the probe records what happened into the log:

<!-- demo:cancellation -->

## Background service

An app-wide background process can push updates into the UI, and this is where ongoing work belongs — not in a
lifecycle hook. Register the producer as a singleton with a lifetime of its own, have it raise an event each tick,
and let components subscribe:

```csharp
protected override async Task OnMount()   => feed.Updated += StateHasChanged;
protected override async Task OnUnmount() => feed.Updated -= StateHasChanged;
```

Unlike a poll loop inside one component, this producer is **decoupled from the component tree** — it keeps ticking
across navigations (and, on the Server, across every session), and no first render is ever waiting on it.

Each consumer subscribes on mount and **unsubscribes on unmount** so it stops repainting (and can be collected) once
it leaves the tree. The loop runs on a background thread, so `StateHasChanged()` crosses threads — safe here: it
schedules a render under the subscriber's own session lock and is a no-op once the component unmounts. Publish the
producer's state as a single immutable snapshot swapped by reference, so a reader on the render thread cannot catch
a half-built one.

### Hosted services

A self-starting singleton is the simplest producer, but it gives you no say over *when* it starts and no chance to shut
it down cleanly. For that, register an `IHostedService` — usually by deriving from `BackgroundService`:

```csharp
builder.Services.AddHostedService<ReportGenerator>();
```

This works the same on **both hosts**. On the Server the generic host starts it; on WASM the framework starts it for
you at the end of boot — late enough that a service is free to mutate state and call `StateHasChanged()` against a
mounted tree, and early enough that the work has begun before anyone can interact. Registration order is start order,
and startup is sequential.

Be precise about what "started" buys you, though: for a `BackgroundService` it means `ExecuteAsync` reached its
**first await**, not that it finished initialising. If one service must not run until another is genuinely *ready* —
a job processor that must not poll until its store has restored a snapshot — make it wait on something explicit
(a `TaskCompletionSource`, a readiness flag); registration order alone will not do it.

Three differences from the Server are worth knowing:

- **A failure to start is not fatal.** On the Server a hosted service that throws from `StartAsync` aborts startup,
  which is right when an orchestrator can restart the process. A browser tab has nothing to restart, so the failure is
  logged and the app carries on without that service rather than showing a blank page. One caveat: a hosted service
  whose *constructor* throws (or whose dependency is not registered) takes the whole set down, because the container
  builds them all in a single call — you get a clear error, and no hosted services.
- **A loop that faults later is reported.** `StartAsync` has already returned by the time a `BackgroundService`'s
  `ExecuteAsync` fails, and nothing on this host awaits it, so Rask observes the execute task for you and logs a
  fault. Without that, a crashed background loop would look exactly like one that was never started.
- **Shutdown is best-effort.** The browser's nearest thing to `SIGTERM` is `pagehide`, and it does not wait for
  anything a handler starts. Rask drains hosted services there (in reverse start order, and not for a back/forward-cache
  suspend, where the page can be restored still running), but a service may get little time or none. Treat it as an
  optimisation — `Rask.Jobs`' processor, for instance, hands its lease back in `StopAsync`, and when that does not land
  the lease simply expires, exactly as it would for a server that was killed rather than drained.
