# Render modes

How a Rask Server page reaches the browser, what it costs, and what you can say about the response.

Rask has always server-rendered. The first `GET` returns a complete document — doctype, `<head>`,
`<body>`, and every event-handler id — and the client attaches to that markup rather than replacing
it. There is no hydration step: the ids in the served HTML *are* the event binding.

What changed is everything around that render.

## Turning a rung off

Every rung is automatic — a page climbs as far as it needs to and no further, and nothing here has
to be set for an app to work. The switches are a **ceiling**, for an app that wants a rung it will
never use turned off rather than merely unused:

```csharp
builder.Services.AddRask(configureServer: o =>
{
    o.RenderModes.Static = true;                 // serve a page needing nothing live as a document
    o.RenderModes.ServerInteractivity = true;    // the WebSocket (default)
    o.RenderModes.Streaming = false;             // not implemented yet
    o.RenderModes.Wasm = false;                  // move eligible pages into the browser
    o.RenderModes.WasmBundle = "/main.js";       // where the bundle's boot module is served
    o.RenderModes.QuiescenceTimeout = TimeSpan.FromSeconds(5);
});
```

Defaults are today's behaviour exactly: server-interactive, no static pages, no streaming, no browser
runtime. An app that configures nothing notices nothing.

**Turning `ServerInteractivity` off** means a page is served as HTML and becomes interactive only
once the browser bundle boots — no WebSocket is ever opened. That is the offline-first and
edge-hosted arrangement, and it requires `Wasm`, because otherwise a page with a handler would have
no way to answer it at all.

**A combination that cannot serve a working page throws when the host is built**, naming what is off
and what to do. A contradiction is a configuration mistake, and a host that refuses to start is far
cheaper to diagnose than a page that silently does nothing in production. Turning on a rung that is
not implemented yet throws for the same reason: a switch that reads as supported and quietly does
nothing leaves an app looking configured for something it is not doing.

### The browser rung

With `Wasm` on, a live page fetches the browser bundle **once it goes idle** — never on the critical
path. The visitor already has a rendered, interactive page, and the bundle is several megabytes that
buys them nothing until they navigate. When it is ready, the **next navigation** renders in the
browser and the socket closes.

Landing the handover on a navigation is what makes it cheap: a fresh mount is what a navigation
already does, so no live state has to survive the crossing.

Everything about the fetch is best-effort. A bundle that 404s, fails to boot, or never finishes
downloading leaves the page exactly as it is — live over its socket, which is what it already was.
The failure is logged rather than shown, because there is nothing wrong with the page the visitor is
looking at.

The app itself needs no branch. The same `App` class is a standalone WASM app on an empty page and a
takeover on a server-rendered one; `RunAsync` reads which of those it is from the document and either
paints or prepares. Whether a given *page* is eligible is decided per page — one reaching a database
stays server-live, and [RASK054](diagnostics.md#rask054) says so at the call site.

> **Publish the bundle with `WasmFingerprintAssets=false`.** The WebAssembly SDK otherwise
> content-hashes the framework files and maps them through an import map it writes into the bundle's
> own `index.html` — a document nobody loads here, because the page comes from the server. Without
> the map, `_framework/dotnet.js` resolves to a path that exists in a build output and not in a
> publish, so the bundle boots locally and 404s in production. Turned off, the framework files sit at
> their literal paths and need no map; cache-busting is then the server's, which it is already
> equipped for in a way a static host is not.

### A page that knows better

Nothing needs this. How far a page climbs is detected from its render, and the detection is biased
towards keeping a connection — a page wrongly judged interactive behaves exactly as it always has,
while one wrongly judged static loses its interactivity silently. The attribute is for what detection
cannot see.

```csharp
// Pushes on a timer. Nothing in its render says so, so nothing would mark it.
[RenderMode(RenderMode.Interactive)]
public abstract partial class PollingPanel : Component { }
```

`Interactive` is honoured from **anywhere** in the page's tree, which is what lets a base component
declare the need once — a polling panel says it, and every dashboard built on it inherits the need
without its author knowing to. The attribute is `Inherited`, so a subclass carries it.

```csharp
[Route("/pricing")]
[RenderMode(RenderMode.Static)]
public sealed partial class Pricing : Component { }
```

`Static` is honoured only on the routed page itself or the app root. Letting an arbitrary helper deep
in a tree force a whole page static would be a very quiet way to break it.

And `Static` is a **request, not a command**: if the render shows the page genuinely needs a
connection, it keeps one and the contradiction is logged under `Rask.Ssr` naming the page. Serving it
static would have left that part of the page inert, which is the one outcome worth refusing.

A page can only move **within** what `RenderModes` allows. It cannot ask for a rung the app has
turned off.

## The initial GET waits for your data

`OnMountAsync` is fire-and-forget by design: the render walk starts it, keeps walking, and the
continuation paints later over the live connection. That is right once a socket exists, and wrong
for the first response — where "later" is after the bytes have already gone.

So a page like this used to serve its placeholder as the first paint, and as the entire document
every crawler and cache ever saw:

```csharp
public sealed partial class Weather(IForecastService service) : Component
{
    private Forecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync();

    protected override Component? Render() =>
        _forecasts is null ? P["Loading…"] : Ul[_forecasts.Select(f => Li[f.Summary])];
}
```

The `GET` now waits for that work, so the document carries the forecasts. Nothing in the component
changes.

It renders in **waves**: render, wait for what that render started, render again. A wave is the
right unit because resolved data mounts new components, which start their own work — a page whose
list loads and whose rows then load is two waves, not one longer wait.

```csharp
builder.Services.AddRask(configureServer: o =>
{
    o.RenderModes.QuiescenceTimeout = TimeSpan.FromSeconds(5); // default; Zero disables the wait
});
```

Blowing the budget is not an error. The page is served as it stands and keeps its live session, so
the load finishes over the socket exactly as it did before. It does mean a slow page holds a request
open for up to that long, so size it together with `MaxSessions` — the two multiply.

**Work you deliberately detach is not waited on.** A polling loop started with `_ = LoopAsync()`
returns immediately from the hook, so the response goes out and the loop keeps pushing over the
socket, as it always did.

**Work blocked on JavaScript is not waited on either**, and cannot be. A JS call made during a
render queues onto a frame, and during the `GET` there is no client to send that frame to — so the
awaiting task completes once the socket is up and never before. A hook that reads browser storage to
restore a session is exactly this shape:

```csharp
protected override async Task OnMountAsync()
{
    var stored = await _protectedStorage.GetAsync<string>("token");   // needs the socket
    // …
}
```

The render stops waiting the moment it sees a queued JS call. Nothing is lost by that: such a page
is already interactive *because* of the interop, so it keeps its session and finishes over the
socket exactly as it did before. Waiting would only have spent the whole budget on every page load.

### Rask.Query

A query is waited for too. `Rask.Query` starts its fetch inside the client rather than returning it
from a lifecycle hook, so the render hands it over at the point a component reads it — which means
the `GET` waits for exactly the queries that page actually displays:

```csharp
public sealed partial class Orders(IQueryClient client) : Component
{
    private readonly Query<Order[]> _orders = client.Query(new GetOrders());

    protected override Component? Render() =>
        _orders.IsLoading ? P["Loading…"] : Ul[_orders.Data!.Select(o => Li[o.Ref])];
}
```

That page serves its orders, not its spinner.

Only a query with **nothing to show** holds the response. One that is disabled
(`QueryOptions.Enabled = false`) is pending but has nothing coming, so waiting for it would spend the
whole budget to change nothing. One that is serving cached data while it revalidates has real content
to render and its refresh lands over the live connection. A retry needs no special case: the fetch
task completes when the policy gives up, and the budget bounds it either way.

Worth knowing when reasoning about cache hits: `IQueryClient` is registered **scoped**, which on the
Server host means one cache per session. Every initial `GET` therefore starts cold — stale-while-
revalidate only arises after a navigation inside a live session, never on a first paint.

## A page that needs nothing live is served as a document

Opt in with `RenderModes.Static`:

```csharp
builder.Services.AddRask(configureServer: o => o.RenderModes.Static = true);
```

A page with no event handler, no form, no element `Ref` and no call into JavaScript is inert once it
reaches the browser. It still cost a DI scope, a component tree held for ten seconds against
`MaxSessions`, a socket, and a `no-store` header that put it beyond every cache — including the
browser's own back/forward. Such a page now comes back as plain HTML: no session, no WebSocket, no
runtime script.

Which pages those are is **detected from the render**, not declared. You write ordinary components.

| Signal | Why it needs a connection |
|---|---|
| An event handler | The handler id is inert with nothing to send to |
| A form or bound control | A submit with no socket goes nowhere |
| An element `Ref` | A ref exists to be handed to JavaScript |
| A call into `IJSRuntime` | The call rides a frame |
| Async work still in flight when the response goes out | The page must be able to finish loading |

### What detection cannot see

Detection observes what the render *did*. A component that pushes updates from a `Timer` or an
`event` subscription wired in `OnMount` — work no render walk can observe — would be judged static
and go quiet. `Rask.Dashboard`'s polling panels are exactly this shape.

**It is reported when it happens, though.** A page served without a session that later asks to
re-render has nowhere to send the update — and that request is the one observable symptom of this
failure. Rask logs a warning naming the session under the `Rask.Ssr` category the first time it
happens, so a misjudged page announces itself in your logs rather than going quietly wrong. It fires
once per session; an ordinary teardown is not reported.

That is why the feature is **off by default**, and why you should check the pages it changes before
turning it on in production. In Development every page keeps its session, so `rask dev` hot reload
is unaffected — which also means the static path is not exercised there yet.

### Caching

Conservative by construction, and never something you have to remember:

| Page | `Cache-Control` |
|---|---|
| Keeps a live session | `no-store, no-cache, must-revalidate, private` |
| Faulted, or status ≥ 400 | `no-store, no-cache, must-revalidate, private` |
| Static, authenticated | `no-store, no-cache, must-revalidate, private` |
| Static, anonymous | `private, max-age=0, must-revalidate` + `Vary: Cookie` |

Dropping `no-store` is the user-visible win: it restores bfcache, so browser back/forward is
instant. `private` keeps every shared cache out, and `Vary: Cookie` matters because "anonymous" is
itself a function of the cookie — without it a cache could serve the logged-out page to a signed-in
user. On a localized app the language is already in the `Vary` too.

"Authenticated" is the union of the request principal and the one after the render, because a render
can sign someone in.

## Saying what the response is

### Status codes

A path that falls through to the not-found page answers a real **404**. It used to answer `200`,
which told every cache, crawler and uptime check that a missing page was fine.

The framework can only speak for the cases it knows about. `/products/9999` matches a real route and
renders a perfectly ordinary "no such product" page — only the page knows:

```csharp
public sealed partial class ProductPage(IPageResponse response, IProducts products) : Component
{
    [RouteParam] public int Id { get; set; }

    private Product? _product;

    protected override async Task OnMountAsync()
    {
        _product = await products.FindAsync(Id);
        if (_product is null)
        {
            response.SetStatus(404);
        }
    }

    protected override Component? Render() =>
        _product is null ? P["No such product."] : H1[_product.Name];
}
```

A faulted render still wins with `500` — a page that threw does not get to claim it succeeded.
Setting `200` on the not-found page is the supported way to express a deliberate soft-404.

`IPageResponse` is legal only during the initial server render (`Render`, `OnMount`,
`OnMountAsync`). From an event handler it **throws**: by then the response is long gone, and a
silently dropped status is worse than a crash you can see. On WASM it is a no-op — there is no
response to shape — so a page calling it runs unchanged on both hosts.

### Redirecting on load

Use `Navigator`, the same API you would call from a handler:

```csharp
protected override void OnMount()
{
    if (!_tenant.IsProvisioned)
    {
        navigator.NavigateTo("/onboarding");
    }
}
```

During the initial render the host turns that into a real **302**, before rendering a body at all —
one response instead of a whole page the client immediately navigates away from, and one a crawler
and a cache both understand where a client-side hop is neither. No session is left behind, and the
redirect is `no-store`: one computed from runtime state that a browser pinned would be unrecoverable
without changing the URL.

Only same-site paths are accepted; anything else throws.

## Sharp edges

- **A static page reached by client-side navigation is interactive.** The socket already exists, so
  the page renders in-session — but pressing F5 gives a document with no runtime. "Static" is a
  property of the entry document, not of the page.
- **Detection is per page, decided at the end of the render.** There are no islands: one handler
  anywhere on the page makes the whole page interactive.
- **`MaxSessions` now means what its name says.** It used to bound both concurrent users and GET
  traffic, because every `GET` retained a session for ten seconds. Static pages retain none.

## See also

- [Lifecycle](lifecycle.md) — when `OnMountAsync` runs and what the initial render waits for.
- [Routing](routing.md) — `[NotFound]`, `Navigator`, and route-driven redirects.
- [Deployment](deployment.md) and [Scaling](scaling.md) — caching and session accounting in production.
