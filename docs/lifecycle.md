# Lifecycle

Every `Component` can override a small set of lifecycle hooks. They fire at well-defined points around each render, in
both synchronous and asynchronous flavours, and are identical on the Server and WASM hosts (only the transport
differs). This page documents the exact hooks, their order, the async rules, and the gotchas.

See also: [routing.md](routing.md) for how route/query params drive `OnPropsChanged*`, and the README *Lifecycle
reference* table for a one-glance summary.

## The hooks

All hooks are `protected virtual` on `Component`. Each has a sync and an async variant; you can override either or
both, and they run in pairs (sync first, then async):

```csharp
protected virtual void OnMount() { }
protected virtual Task OnMountAsync() => Task.CompletedTask;

protected virtual void OnPropsChanged() { }
protected virtual Task OnPropsChangedAsync() => Task.CompletedTask;

protected virtual void OnRendered(bool firstRender) { }
protected virtual Task OnRenderedAsync(bool firstRender) => Task.CompletedTask;

protected virtual void OnUnmount() { }
protected virtual Task OnUnmountAsync() => Task.CompletedTask;
```

## Order

| Hook                                     | When                                                                                     |
|------------------------------------------|------------------------------------------------------------------------------------------|
| `OnMount` / `OnMountAsync`               | **Once**, on first creation of the instance (first render only).                         |
| `OnPropsChanged` / `OnPropsChangedAsync` | On the **first render**, and on any later render where a bound prop / route or query param **actually changed**. |
| `OnRendered` / `OnRenderedAsync`         | After **every** render commit, with a `firstRender` flag.                                |
| `OnUnmount` / `OnUnmountAsync`           | **Once**, on disposal (navigation away, parent subtree torn down, session teardown). Children unmount before parents (depth-first). |

So on the first render of a component you get, in order: `OnMount` → `OnMountAsync` → `OnPropsChanged` →
`OnPropsChangedAsync` → (render) → `OnRendered(firstRender: true)` → `OnRenderedAsync(firstRender: true)`. On disposal:
`OnUnmount` → `OnUnmountAsync`.

### Live probe

The component below records every hook invocation into a list and re-renders so you can watch the order.
**Trigger re-render** fires a bare event-handler render — note it re-runs `OnRendered*` but does **not** re-fire
`OnMount*` / `OnPropsChanged*` (nothing the component is bound to changed):

<!-- demo:lifecycle-hooks -->

### Mount / unmount cycle

Toggle the probe in and out of the tree to watch `OnUnmount` and `OnUnmountAsync` fire (children before parents). The
log is held by the parent, so it survives the probe's unmount:

<!-- demo:lifecycle-cycle -->

A typical async-data page uses `OnMountAsync` to fetch once and renders a placeholder until it lands:

```csharp
[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync();

    protected override Component? Render() =>
        _forecasts is null
            ? P[Em["Loading..."]]
            : Table[/* render rows */];
}
```

### When `OnPropsChanged*` refires

`OnPropsChanged*` fires on the first render and whenever a value the component is bound to **actually changes** —
including:

- A parent passing a different value for a factory parameter (a prop).
- A `[RouteParam]` / `[QueryParam]` value changing because the URL changed.
- A **reused routed page** whose URL **path** changes (the router keeps the instance and re-binds it rather than
  remounting).

What does **not** refire it: a bare event-handler re-render. Clicking a button that mutates a local field re-renders
the component but does **not** re-fire `OnPropsChanged*` — nothing the component is bound to changed. (`Key` is a
reconciliation identity, not a reactive prop, so a key change doesn't fire `OnPropsChanged` either; it mounts a fresh
instance.)

The live-ticker demo puts the hooks together: a poll loop started in `OnMountAsync` streams a synthetic price
into a **zero-JS, server-rendered SVG chart**, and the **BTC / ETH / SOL** switcher hands the ticker a new
`Symbol` — a changed factory parameter — so `OnPropsChanged*` refires (watch the *Hook activity* log), clears
the buffer, and wakes the loop to poll the new asset immediately. `CancellationToken` tears the loop down on
unmount:

<!-- demo:lifecycle-ticker -->

## Sync vs async rules

The async hooks install a synchronization context so each `await` inside a hook triggers an automatic re-render after
the continuation, plus one terminal re-render on completion — you get "mutate state after the await and it paints"
without calling `StateHasChanged()` by hand. The runtime coalesces these into one payload per handler dispatch.

```csharp
protected override async Task OnMountAsync()
{
    // placeholder shows here
    _data = await LoadAsync();
    // auto re-render after the await → real data paints, no StateHasChanged()
}
```

**`OnRenderedAsync` is loop-safe.** The terminal auto re-render is a *publish-only* walk: it does **not** re-fire
`OnRendered` / `OnRenderedAsync` on components that have already rendered at least once. That's what keeps an
`OnRenderedAsync` hook which awaits a next-frame side effect (e.g. drawing a chart, or a scoped-JS call) from looping
on itself. Newly-mounted children on the same walk still get their first `OnRendered(firstRender: true)`.

```csharp
protected override async Task OnRenderedAsync(bool firstRender) =>
    await js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender);
    // re-render from another component won't re-fire this — no loop
```

## Gotcha: a faulted async hook takes the page, not the component

**If an async hook faults, it trips the nearest `ErrorBoundary` — and in a live app there is always one.** The host
wraps your `App` in an implicit root boundary, and every component is stamped with the boundary above it during the
render walk, so a faulting `OnMountAsync` / `OnPropsChangedAsync` / `OnRenderedAsync` renders that boundary's fallback
rather than logging quietly.

The practical symptom is therefore the opposite of what you might expect: not a component stuck forever on a loading
placeholder, but **the whole page replaced by an error page** — because the boundary that caught it is the root one,
unless you put a closer boundary in the way.

```csharp
// Without a boundary of your own, a throw here replaces the entire document.
protected override async Task OnMountAsync() => _rows = await api.LoadAsync();

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

When `OnUnmount` / `OnUnmountAsync` runs, the component's lifetime `CancellationToken` is still **live** — it's
cancelled immediately *after* the hook returns. But the component is already leaving the tree, so calling
`StateHasChanged()` from inside an unmount hook is a **no-op** by design (it's been flagged unmounted before the hook
fires). Don't request a render from unmount.

```csharp
protected override void OnUnmount()
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

`OnUnmount` / `OnUnmountAsync` is the framework-side cleanup signal. It fires **before** the lifetime
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
public sealed class CancellationProbe : Component
{
    public required Action<string> Log { get; set; }
    public required int InstanceId { get; set; }

    protected override async Task OnMountAsync()
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

Mount the probe to start a 2.5-second `Task.Delay` inside `OnMountAsync`; click **Unmount** before it settles to
cancel — the probe records what happened into the log:

<!-- demo:cancellation -->

## Background service

An app-wide background process can push updates into the UI. A single `IMetricsFeed` singleton runs its own loop and
raises an event each tick; the two widgets below each subscribe independently (`feed.Updated += StateHasChanged`) and
repaint themselves. Unlike a poll loop that lives inside one component, this producer is **decoupled from the component
tree** — it keeps ticking across navigations (and, on the Server, across every session):

<!-- demo:background-metrics -->

The producer is a DI `AddSingleton<IMetricsFeed, MetricsFeed>()` — one instance for the whole app. Each consumer is a
tiny component that subscribes on mount and **unsubscribes on unmount** so it stops repainting (and can be collected)
once it leaves the tree. The loop runs on a background thread, so `StateHasChanged()` crosses threads — safe here: it
schedules a render under the subscriber's own session lock and is a no-op once the component unmounts.

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
