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
protected override async Task OnUpdated()     => _product = await Product.Where(p => p.Id == Id).FirstOrDefault();   // new props arrived (and on mount)
protected override async Task OnFirstRender() => await _map.Init(_center);            // once, after the first render
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
| `OnFirstRender` | **Once**, after the first render is in the page — where browser work that needs the elements starts. |
| `OnRendered`      | After **every** render, the first included (after `OnFirstRender`). |
| `OnUnmount`       | **Once**, on disposal (navigation away, parent subtree torn down, session teardown). Children unmount before parents (depth-first). |

**What "the instance" is.** A component keeps its instance — and so mounts once — for as long as the
component that writes it goes on writing it at the same place *among the children of its type*, or under the
same `Key`. A sibling of another type, or an element, appearing or disappearing ahead of it does not unmount
it. A sibling of its own type doing so moves every later one of that type along by one, state included, unless
they carry keys: see [keys and identity](composition.md#children--fragments).

So a component's life reads:

```
first time:  OnMount → OnUpdated → Render → OnFirstRender → OnRendered
new props:   OnUpdated → Render → OnRendered
own state:   Render → OnRendered
leaving:     OnUnmount   (its CancellationToken is cancelled right after)
```

### Live probe

The component below counts every hook and re-renders so you can watch the order. **Trigger re-render** fires a
bare event-handler render — note it re-runs `OnRendered` but does **not** re-fire `OnMount` / `OnUpdated` (nothing the
component is bound to changed), and `OnFirstRender` stays at one:

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
all see. If the fetch outlives the budget the placeholder is what is served, and the page finishes loading
over the socket. See [Live pages](render-modes.md#the-initial-get-waits-for-your-data).

**A page reached in place is sent at once**, on both hosts — in WebAssembly the first page the app renders
too: a link, Back, `Go()` from a handler or a redirect shows the new page as soon as it is mounted, its
placeholder where the data will be, and the data in the frame that follows when the fetch lands. So the
placeholder is worth writing: it is what every navigation shows until its data are back. To keep the rows the
reader already sees, keep them in the component (or use `KeepPreviousData` on a query) instead of emptying
them. See [a page reached in place is sent at once](routing.md#a-page-reached-in-place-is-sent-at-once).

An edit page is the same page with a form where the table was: read the record in the page's hook, draw the
form once it is there, and let a form component of your own read its model prop in `Render` — a copy taken in
its `OnMount` is taken once, before the read is back. See
[an edit page that reads its record](forms.md#an-edit-page-that-reads-its-record).

Data that has to stay fresh after the first render is not a loop in a hook. Poll it with the query that reads it —
`RefetchInterval` refetches on that interval while the component renders it, stops when it unmounts, and waits while
the reader's tab is hidden ([Polling](query.md#polling)):

```csharp
protected override Component? Render()
{
    var sales = QueryClient.Query(new GetSales(), new QueryOptions { RefetchInterval = TimeSpan.FromSeconds(30) });
    return sales.Data is { } data ? P[$"{data.Total} sold"] : P["Loading..."];
}
```

## Load when in view

A widget far down a page need not cost the first response anything. Wrap it in `Lazy`, and it is not mounted —
no `OnMount`, none of its reads — until the reader scrolls to it; what stands in its place until then is the
placeholder:

```csharp
protected override Component? Render() =>
[
    Ui.Heading["Dashboard"],
    Lazy.Placeholder(Ui.Skeleton)[SalesWidget],   // mounted once the skeleton is scrolled into view
];
```

The first `GET` carries the placeholder. The moment any of it is in the viewport the browser says so, once, and
the page answers with the widget in its place — on both hosts. `Lazy[SalesWidget]` puts an empty `div` there, as
Livewire does. Each lazy is a frame of its own, so a slow one holds no other back, and a lazy inside a loaded one
waits for its own turn. It is Livewire's `#[Lazy]`; Livewire's `defer` — load right after the page, wherever it
is — has no Rask spelling yet.

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

Data to keep fresh is a polling query, above. Any other ongoing work goes in a **service with its own lifetime**
that the component subscribes to — which is what [Background service](#background-service) below shows. The component's own hooks then do what they are for:
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
the same walk still get their `OnFirstRender` and `OnRendered`.

```csharp
protected override async Task OnRendered() =>
    await js.InvokeVoidAsync("Rask.CodeSample.rendered");
    // re-render from another component won't re-fire this — no loop
```

### When a handler's state reaches the screen

An event handler is different from a hook: what it sets is **not** painted at each `await`. The page is sent
when the handler has answered — one frame, with what the reads it started brought back — and only a handler
still running after the handler budget (150 ms) is painted as it stands, once, with every later `await`
repainting as it lands:

```csharp
private async Task Save()
{
    _saving = true;                 // shown only if the save is still running at 150 ms
    await _order.Save();
    _saving = false;                // a save that was back in time: this is the only frame
}
```

So an intermediate state — `_saving`, `_loading` — is what a *slow* handler looks like, and a fast one never
flashes it. An async validator's "Checking…" follows the same rule: a slow check shows it at the budget, and a
quick one shows it only beside its verdict, for the indicator's own 200 ms tail. The button that was pressed says it is busy by itself after
200 ms ([buttons that wait](ui-kit.md#buttons-that-wait)), so most handlers need no flag at all. Call
`StateHasChanged()` when something must be painted regardless: it is honoured when the handler ends, as
before. The budget is `Rask:Server:HandlerQuiescenceTimeout` (`RaskWasmOptions.HandlerQuiescenceTimeout` in the
browser); zero paints at every `await`. See
[a handler's work reaches the screen once](routing.md#a-handlers-work-reaches-the-screen-once).

## An event that arrives after a re-render

An event the browser sent before a re-render and that arrives after it runs the handler it was sent to, wherever
that handler is in the page now — or nothing, when its button, row or component has left. It never runs another
handler, and a component that was unmounted never hears an event again. What counts as the same handler, and what
that means for a list of rows, is in
[An event that outlived its render](js-interop-runtime.md#an-event-that-outlived-its-render).

## Each event gets its own scoped services

A live page answers many events over one connection, and each one runs the way a request would: the app's
**scoped** services it reaches are fresh for that event, signed in as the user the session holds *for that
event* (revalidated as it started), and disposed once the event has rendered. A click, a form submit, a
navigation, a render the server pushes and the lifecycle hooks they run each count as one event; the events of
one batched frame count as one.

```csharp
builder.Services.AddDbContext<ShopDb>();          // scoped: one per event, not one per open tab
builder.Services.AddScoped<IPricing, Pricing>();  // likewise

// A command handler that takes the context gets the event's own, and it is disposed after the click.
public sealed class PlaceOrderHandler(ShopDb db) : ICommandHandler<PlaceOrder> { … }
```

Everything scoped is one per event — your services, the batteries' (Rask.Data, Rask.Auth) and any package's —
and so is every transient, which is how the CQRS dispatcher an event reaches, and the handler behind it, get the
event's context. A few services are the **session's**, because they *are* the open page: its route and navigator,
its connection (`IJSRuntime`), its user (`IUserProvider`), its toaster, its language, its query cache and the
tenant its resolver named. A service made for the event that takes one of those is handed the session's own, so
a scoped service of yours can inject `IToaster` or `IUserProvider` as it always could. The scope is opened only
when something asks for a per-event service, so an event that asks for none costs nothing.

**Each component's part of an event is its own.** As a Livewire component's action is a request of its own, every
component a live event runs code for — the one whose handler was clicked, each one whose `OnMount`/`OnUpdated` the
event's render started — gets its own scope. A page whose notices, progress bar and file list each load in their own
hook loads them **at once, each through its own `DbContext`**, where one shared context would throw EF Core's "a
second operation was started on this context instance":

```csharp
public sealed partial class LoadNotices : Component
{
    protected override async Task OnMount()
    {
        var db = Current.Service<ShopDb>();             // this component's, in this event
        _notices = await db.Notices.ToListAsync();      // runs beside LoadProgress's read, never on its context
    }
}
```

Within one component the event's instance is one: its handler, its hooks and what they call — a command handler the
click dispatches, a helper that takes nothing — all get the same `ShopDb`. Code that runs for no component (a
`Render()`, a read a render started) gets the event's own. Each component's scope lives by the rules below on its
own: it goes when the event has ended and *that component's* hooks are done, whatever its neighbours are still
reading.

Two rules follow:

- **A component's constructor is the session's.** A component outlives every event, so what it takes in its
  constructor comes from the session, as before. Reach the event's own with `Current.Service<T>()` inside the
  handler — or through the calls that take no service, `await Dispatcher.Send(command)`, `Cache.Remember(…)`, and the
  handler they reach.
- **Do not keep a scoped service past its event.** A field that holds a `DbContext` it was handed in one event
  finds it disposed in the next. This is a behaviour change: an app that kept scoped state across events (a
  scoped "unit of work" collecting changes over several clicks) now gets a fresh one per event — keep such state
  on the component, or in a singleton keyed by the user.

**An event's scope lives as long as the work the event started.** A lifecycle hook — `OnMount`, `OnUpdated`,
`OnRendered` — belongs to the event whose render started it, and so does every `Current.Service<T>()` it makes,
before an await or after one, itself or through a helper it calls. A navigation is sent at once and its pages'
hooks load on behind it; their reads keep that event's scope (one `DbContext`, the same instance throughout) while
other events come and go, and it is disposed when the event has ended **and** the last hook it started — and the
last read its page went out without — is done. Two navigations in a row each keep their own. Two limits keep this
from leaking: when a hook's component leaves the page, its token (`Current.Cancellation`) is cancelled and the hook
keeps the scope for **ten seconds** more at most — a loop over a stream that never ends does not keep its
`DbContext` for the rest of the session — and when the session ends, every scope is disposed, whatever is still
running.

**Work a hook detaches.** A page that should go out before its slow read is back can detach the read and return —
the first response and the navigation never wait for it. Take the provider in the hook and resolve **inside** the
detached work, from that provider:

```csharp
protected override async Task OnUpdated()
{
    _definition = await Reports.Definition(Id);   // the page goes out with this
    _ = LoadRows(++_turn);                        // the rows follow over the socket
}

private async Task LoadRows(int turn)
{
    var services = Current.Service<IServiceProvider>();   // the event's, lent to the work below
    var rows = await Task.Run(() => services.GetRequiredService<ShopDb>().Rows.ToList());
    if (turn == _turn) _rows = rows;                      // painted when it is back
}
```

The provider a hook takes is its event's, wherever it is used — another thread included — and it keeps that
event's scope until the detached work **comes back to the page** (the first await that resumes there once the hook
itself has finished). Work that never comes back keeps it until its component has left the page (ten seconds more
at most) or the session ends — so a tab closed mid-read, or a first response whose page never connects, lets it go
with its session. A scope is never disposed on the thread of a response or an event: a `DbContext` that waits, as
it is disposed, for a query still running on it holds nobody up. A `Current.Service<T>()` made inside the detached
work itself, without the provider, is not covered — it reaches whichever event is in progress then. A hook that
awaits its work instead needs no provider: `Current.Service<T>()` in it is the event's until the hook is done, and
taking the provider there keeps the scope while the page is open.

### `Current.Service<T>()` — the event's own, with nothing injected

```csharp
Button.OnClick(async () =>
{
    var db = Current.Service<ShopDb>();   // this event's ShopDb
    db.Orders.Add(order);
    await db.SaveChangesAsync();          // still the same instance after the await
})
```

It answers from whatever work is in flight, as its constructor would have been handed it:

| Where it is called | What it returns |
| --- | --- |
| A live event (handler, navigation, pushed render, the hooks they run) — both hosts | The scoped instance of the event's part for the component running it: one per component per event, the same across `await`s, disposed when the event and that component's hooks are done. The session's services (route, navigator, `IJSRuntime`, `IUserProvider`, toaster, language, query cache, tenant) are the session's. |
| An HTTP request (minimal API, controller) | The request's `RequestServices`. |
| A background job, a CQRS handler's scope | That scope. |
| Nowhere — a constructor, a hosted service's own loop | An `InvalidOperationException` naming what to inject instead. Never the root provider. |

An unregistered `T` throws the standard "No service for type" error. Keep what it hands you in a local: a field
that holds it finds it disposed in the next event. The scope is opened on first use, so an event that never asks
costs nothing.

## Asking another component to render, from a hook

A page often has something to tell the layout above it — its title for a breadcrumb, say — and tells it as it
mounts: it sets a value on a scoped service, and whoever shows that value re-renders on the service's event.

```csharp
// the page
protected override async Task OnMount() => heading.Set("Orders");

// a crumb in the layout
protected override async Task OnMount() => heading.Changed += StateHasChanged;
protected override Component? Render() => Text[heading.Text];
```

By then the layout's crumb has already rendered, with the old value: a layout is walked before the page
inside it mounts. That is fine. `StateHasChanged()` on a component the pass in flight has already rendered —
the crumb, or the layout itself — is never lost: the pass finishes, and one more follows that renders exactly
what asked. It works the same from `OnMount`, `OnUpdated` and an event handler, on the component itself or on
any other.

On the **Server host the first response includes that follow-up render**, so the crumb arrives filled in
rather than being corrected a moment later over the socket. It is bounded: a component that asks again on
every render gets two follow-ups per request, and the rest goes over the live connection.

## Gotcha: a faulted async hook takes the page, not the component

**If a lifecycle hook faults, it trips the nearest `ErrorBoundary` — and in a live app there is always one.** The host
wraps your `App` in an implicit root boundary, so a faulting `OnMount` / `OnUpdated` / `OnFirstRender` / `OnRendered`
renders that boundary's fallback rather than logging quietly. It makes no difference where in the hook it throws —
on its first line, in a read that fails before it ever awaits, or after an `await` — nor how deep the component
is: a page, a layout, a child of a child.

The practical symptom is therefore the opposite of what you might expect: not a component stuck forever on a loading
placeholder, but **the whole page replaced by an error page** — the one a `Render` that throws gets — because the
boundary that caught it is the root one, unless you put a closer boundary in the way.

```csharp
// Without a boundary of your own, a throw here replaces the entire document.
protected override async Task OnMount() => _rows = await api.Load();

// With one, the blast radius is the subtree you chose.
ErrorBoundary.Fallback((ex, retry) => Div[
    P["Could not load the rows."],
    Button.OnClick(retry)["Try again"]
])[
    RowList()
]
```

What the reader gets, on both hosts:

| The hook faults | The reader sees | The first `GET` answers |
|---|---|---|
| before the page is sent — on the first request, or before the first `await` of a page reached in place | the error page, never the placeholder | **500** |
| after the page was painted on its placeholder (an `await` of a page [reached in place](routing.md#a-page-reached-in-place-is-sent-at-once), a first request's read that outlasted its wait, an `OnRendered`) | the placeholder, then the error page in the frame that follows | 200 — it was already sent |
| inside an `ErrorBoundary` you wrote | your fallback, the rest of the page as it was | 200 |
| after its component left the page | nothing: leaving cancels the load, and what that throws is recorded at Information | — |

Two things follow:

- **Scope the damage yourself.** An `ErrorBoundary` around the risky subtree keeps the rest of the page alive, and its
  `Fallback` receives a `retry` callback that clears the error and re-renders that subtree. A `try/catch` inside the
  hook is still the right tool when you want to render an error *state* rather than a fallback.
- **The root error page offers `Try again` as well as `Reload this page`.** The first clears the error and re-renders
  in place, keeping the session and the state. **A load that faulted is run again**: the component whose `OnMount` or
  `OnUpdated` threw gets both once more, so `Try again` tries the read and not only the drawing — the same for the
  `retry` your own `Fallback` is handed. A render or a load that faults deterministically simply lands back on the
  error page, and then the reload is what you want.
- **The error page stands in for the page that faulted, not for the app.** Back, or any other address, is another
  page, and it is drawn; one that faults too gets the error page again.

The initial GET for a page whose render or hook faulted before it was sent answers **500**, not 200 — the body is
still the error page, so both buttons work.

### What a visitor sees when something throws

Outside Development, **nothing of the exception**. The built-in error page — the root one, and a nested
`ErrorBoundary` with no `Fallback` — shows a heading, `Try again` where the boundary can recover, `Reload this
page`, and one sentence with a reference:

```text
Something went wrong
[Try again] [Reload this page]
The problem has been recorded. If it keeps happening, quote this reference: K7WQ2MZP
```

No exception type, no message, no inner exception, no stack and no source reach the response or the live frame: a
message routinely carries a connection string, a query, a path or a user's data. The texts are `RaskString.ErrorHeading`,
`ErrorTryAgain`, `ErrorReload` and `ErrorReference` ([localization](localization.md)).

**A handler that throws is not that page.** A click, a change, any callback whose handler throws, in a part
of the app you wrote no `ErrorBoundary` around, keeps the page it was on — with whatever the handler changed
before it threw — and says so in an error toast that carries the same reference:

```text
That did not work. Please try again. The problem has been recorded. If it keeps happening, quote this reference: K7WQ2MZP
```

The next click is answered as usual. The sentence is `RaskString.HandlerFailed` followed by `ErrorReference`.
What still replaces the page, or the part your own boundary wraps, is a fault the tree cannot be drawn
around: a `Render` or a lifecycle hook that throws, a handler inside an `ErrorBoundary` you wrote (see
[a handler that fails](composition-lists.md#a-handler-that-fails)), and a handler in a host whose layout
draws no toasts — there would be nowhere to say it.

The reference is how support finds it. Every exception a boundary or that toast takes — the root one or your own, whatever its
fallback draws — is logged **once at Error** on the `Rask.Errors` category, with the exception attached and the same
reference in the message:

```text
fail: Rask.Errors[0]
      An unhandled exception reached an error boundary from an event handler. Reference K7WQ2MZP
      System.InvalidOperationException: Login failed for user 'sa' …
         at …
```

A handler's line reads `reached a toast from an event handler` in place of the boundary. It is eight random characters, made on the host when the exception is first seen, and derived from nothing: not the
exception, not the visitor, not the request. One exception keeps one reference however many times its page is drawn.
With request tracing on, the same log line carries the host's trace id as usual. A build-time
[prerender](prerendering.md) shows its pages to nobody: what a boundary takes there — typically a hook reaching for a
browser that is not there — is one Information line, and the pass itself reports a page it could not write.

In **Development** the page shows everything it always has — type, message, each cause, the stack with source
excerpts — and the reference beside them, for a render and for a lifecycle hook alike. A handler's fault is shown
over the running app instead ([`rask dev`](cli.md)), and a handler's toast adds the exception's type and message
after its sentence.

**What decides Development:** the host's own `IHostEnvironment`, which `AddRask`/`RaskApp` register for that host alone —
two hosts in one process (a test run, an app mounting another) each answer for their own renders, handlers and
sessions, never with whichever started first. A host
that never says — a hand-wired host with no `IHostEnvironment`, a component rendered outside a host — falls back to
`ASPNETCORE_ENVIRONMENT`, then `DOTNET_ENVIRONMENT`, and with neither set to `Development` it is **not** Development.
A browser (WASM) app has no such variables, so it shows the Production page unless its own startup sets
`LiveOptions.IsDevelopment = true`; the exception is in the browser console either way.

**Your own `Fallback` is handed the exception — do not print it.** `ex.Message` and `ex.GetType().Name` on a page are
exactly what the built-in page no longer shows. Say what the visitor can do, as the example above does, and branch on
the exception's type if the wording depends on it. It is already in the log.

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
            await Task.Delay(2500.Milliseconds, CancellationToken);
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

**Each component answers to its own lifetime, whoever caused it to mount.** A call that passes no token —
`await Product.Count()`, `Cache.Remember(…)` — is cancelled with the work in progress: in `OnMount` and
`OnUpdated` that is the hook's own component, in an event handler the component the handler belongs to. A page
reached by `Go()` from a handler mounts during that handler's turn and still loads under its own lifetime, not
under the saving page's, which the navigation has just ended. The handler keeps its own component's: code after a
`Go()` that unmounted it finds the token cancelled, so finish the work before navigating. Leaving a page while its
load is in flight shows nothing — the cancellation is silent, and a failure a data provider raises because of it
is logged, never displayed on the page the visitor went to.

**A callback answers to the component that wrote it, not the one that invoked it.** A page's
`.OnSaved(async () => { _editing = false; await Reload(); })` is the page's code, reached through the editor's
handler: while it runs — its first line and everything after each `await` — the work in progress is the page's,
so unmounting the editor does not cancel the reload, and leaving the page does. When it returns, the editor's
handler is back under the editor's lifetime. With a `HandlerTimeout` configured the callback is bound by both:
its owner's lifetime and the same timeout as the event that raised it. More in
[callbacks](composition-callbacks-context.md#callbacks-child--parent).

Mount the probe to start a 2.5-second `Task.Delay` inside `OnMount`; click **Unmount** before it settles to
cancel — the probe records what happened into the log:

<!-- demo:cancellation -->

## Background service

An app-wide background process can push updates into the UI, and this is where ongoing work belongs — not in a
lifecycle hook. Have the producer — a singleton, a hosted service, a timer, a job, another page's handler —
**publish an event**, and let the page that shows it **handle** it:

```csharp
// Anywhere: a BackgroundService, a job, a handler — any thread.
await Dispatcher.Publish(new PumpStatusChanged(pumpId));

// The page: a component that implements IEventHandler<T>.
public sealed partial class PumpsPage : Component, IEventHandler<PumpStatusChanged>
{
    private List<Pump> _pumps = [];

    public async Task Handle(PumpStatusChanged e) => _pumps = await Pump.Where(p => p.Active).ToList();

    protected override Component? Render() => …;
}
```

Unlike a poll loop inside one component, this producer is **decoupled from the component tree** — it keeps working
across navigations (and, on the Server, across every session), and no first render is ever waiting on it.

### A page that handles an event

A component that implements `IEventHandler<T>` is the page's listener. **A service handler runs once per event; a
component handler runs once per open page** that has that component mounted — each in its own page's session:

- **After what the page is doing, never beside it.** It is queued behind the page's own events, under the session's
  lock, like a click: a click in flight finishes first, and the handlers of one page run in the order the events were
  published.
- **As that page's user, with the event's own services.** It gets a fresh scope, as every event does
  ([above](#each-event-gets-its-own-scoped-services)): the session's current user and tenant, a `DbContext` of its own.
- **Drawn once.** The page renders once when `Handle` returns; no `StateHasChanged()` to call.
- **Never dropped.** A page between two connections handles it and is sent the result when it reconnects.
- **Only while mounted.** A component that has left the page hears nothing more; there is nothing to unsubscribe.
- **A fault stays on its page.** One that throws is logged and shown as the handler-fault toast, as a click's would be;
  every other page still hears the event.
- **After the commit.** Published inside `Db.Transaction` — or raised by an aggregate with `Raise(…)` — it reaches the
  pages once the transaction commits, and never after a rollback.

Registration is generated at build time, so a component that handles nothing costs nothing to mount, and a component
is never a DI service: a publish never builds one. Delivery stays inside this process — every open page this server
holds; a browser app hears what is published in that tab.

### Following a plain .NET event

`source.Changed += StateHasChanged` still works for anything that raises an ordinary event, and `StateHasChanged()`
may be called **from any thread**: a `BackgroundService` raising its event straight from the pool, several threads
at once, with no dispatcher and no `await`. It never renders on top of a render in flight — it marks the component
and asks its session, which draws under its own lock. It does not run your code in the page's session, though: reach
for a component handler (above) when the reaction reads data or needs the user. Three things are guaranteed:

- **The last state is always drawn.** Whatever was marked when the call returned reaches the reader's page without
  another event, wherever the call landed: while a handler runs, inside a render, after a read came back, between
  two connections.
- **Calls that land together are drawn together.** While the session is handling an event or rendering, every call
  is folded into the next render rather than given one of its own. A dispatch renders at most three times; what is
  asked for after that is one more render straight after the dispatch.
- **A runaway is slowed and named, never spun.** A component that asks for a render from every `Render()` would
  otherwise never stop. After ten renders in a row that were each asked for while the one before was being made,
  each further one waits 250 ms, and a warning on `Rask.Live` names the component types that asked. The page stays
  current and the reader's events are answered in between. The row ends after a second with no render; the time a
  slow render takes does not count toward that second, and the session's `TimeProvider` keeps the time.

Subscribe on mount and unsubscribe on unmount, mutate the state **before** the call, and do not call
`StateHasChanged()` from `Render()` itself.

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
