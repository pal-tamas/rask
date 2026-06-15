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

A typical async-data page uses `OnMountAsync` to fetch once and renders a placeholder until it lands:

```csharp
[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync();

    protected override RenderResult Render() =>
        _forecasts is null
            ? P()[Em()["Loading..."]]
            : Table()[/* render rows */];
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

## Gotcha: faulted async hooks are silent

**If an async hook faults, the framework logs the exception to `Console.Error` and does NOT trigger a re-render.** It
does not surface as an error page or a thrown exception up the render stack. The practical symptom: a component stuck
on a loading placeholder that never resolves is almost always an `OnMountAsync` / `OnPropsChangedAsync` /
`OnRenderedAsync` that threw. Check `Console.Error`. (Wrap risky work in `try/catch` if you want to render an error
state yourself, or use an `ErrorBoundary` around the subtree for render/handler faults.)

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

(See `samples/Rask.Example.Shared/Features/Cancellation/CancellationPage.cs` and `LifecyclePage.cs` for runnable probes that log every
hook invocation, and `PropsPage.cs` for the props-binding demo.)
