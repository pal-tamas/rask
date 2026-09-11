# Live pages

How a Rask Server page reaches the browser, what it costs, and what you can say about the response.

Rask server-renders. The first `GET` returns a complete document — doctype, `<head>`, `<body>`, and every
event-handler id — and the client attaches to that markup rather than replacing it. There is no hydration
step: the ids in the served HTML *are* the event binding.

## Every page is live

There is nothing to choose. `builder.Services.AddRask()` and `app.UseRask<App>()` are the whole of it, and
every page a Rask Server app renders is live:

1. the `GET` creates the page's session and renders it;
2. the response carries the session id and the runtime script;
3. the browser connects, and every state change from then on streams to it as a minimal diff.

A page with no handler still gets a session, so a component that pushes from a timer or an event
subscription just works — there is no render-time guess about which pages need a connection, and so no
page that guessed wrong and went silently inert.

What that costs is bounded, not unlimited:

| Bound | Default | What it does |
|---|---|---|
| [`UnconnectedSessionGracePeriod`](configuration.md) | 10 s | A session whose browser never connects — a crawler, a bounced visit — is released after this. |
| [`MaxSessions`](configuration.md#sizing-maxsessions-for-a-memory-budget) | set it for your memory budget | A `GET` that would exceed it is answered `503` instead of taking a session the box cannot hold. |

So `MaxSessions` bounds live sessions *and* recent page loads together. Size it against both — see
[Scaling](scaling.md).

> **WebAssembly is not a render mode.** A Rask app that runs in the browser is a single-page app of its own:
> `rask new --template wasm` for a standalone one, or `rask new --wasm` for a server that serves one from its
> `Client/` folder. Either way the server renders none of its pages — see [Single-page apps](spa.md#a-rask-webassembly-app).

## The initial GET waits for your data

`OnMountAsync` is fire-and-forget by design: the render walk starts it, keeps walking, and the continuation
paints later over the live connection. That is right once a connection exists, and wrong for the first
response — where "later" is after the bytes have already gone.

So a page like this would serve its placeholder as the first paint, and as the entire document every
crawler and cache ever saw:

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

The `GET` waits for that work, so the document carries the forecasts. Nothing in the component changes.

It renders in **waves**: render, wait for what that render started, render again. A wave is the right unit
because resolved data mounts new components, which start their own work — a page whose list loads and whose
rows then load is two waves, not one longer wait.

```csharp
builder.Services.AddRask(configureServer: o =>
{
    o.QuiescenceTimeout = TimeSpan.FromSeconds(5); // default; Zero disables the wait
});
```

Blowing the budget is not an error. The page is served as it stands, and the load finishes over the live
connection. It does mean a slow page holds a request open for up to that long, so size it together with
`MaxSessions` — the two multiply.

**Work you deliberately detach is not waited on.** A polling loop started with `_ = LoopAsync()` returns
immediately from the hook, so the response goes out and the loop keeps pushing over the connection.

**Work blocked on JavaScript is not waited on either**, and cannot be. A JS call made during a render queues
onto a frame, and during the `GET` there is no client to send that frame to — so the awaiting task completes
once the browser has connected and never before. A hook that reads browser storage is exactly this shape:

```csharp
protected override async Task OnMountAsync()
{
    var stored = await _protectedStorage.GetAsync<string>("token");   // needs the browser
    // …
}
```

The render stops waiting the moment it sees a queued JS call, and the page finishes over the connection.
Waiting would only have spent the whole budget on every page load.

### Rask.Query

A query is waited for too. `Rask.Query` starts its fetch inside the client rather than returning it from a
lifecycle hook, so the render hands it over at the point a component reads it — which means the `GET` waits
for exactly the queries that page actually displays:

```csharp
public sealed partial class Orders(IQueryClient client) : Component
{
    private readonly Query<Order[]> _orders = client.Query(new GetOrders());

    protected override Component? Render() =>
        _orders.IsLoading ? P["Loading…"] : Ul[_orders.Data!.Select(o => Li[o.Ref])];
}
```

That page serves its orders, not its spinner.

Only a query with **nothing to show** holds the response. One that is disabled (`QueryOptions.Enabled =
false`) is pending but has nothing coming, so waiting for it would spend the whole budget to change nothing.
One that is serving cached data while it revalidates has real content to render, and its refresh lands over
the live connection.

Worth knowing when reasoning about cache hits: `IQueryClient` is registered **scoped**, which on the Server
host means one cache per session. Every initial `GET` therefore starts cold — stale-while-revalidate only
arises after a navigation inside a live session, never on a first paint.

## Caching

Every page carries `Cache-Control: no-store, no-cache, must-revalidate, private` and `Pragma: no-cache`.
The document holds a session id that belongs to one visit, so no cache — the browser's own back/forward cache
included — may keep it. Put public content that should be cached somewhere that renders without a session: a
[prerendered WebAssembly site](prerendering.md), or a plain ASP.NET endpoint.

## Saying what the response is

### Status codes

A path that falls through to the not-found page answers a real **404**, which tells caches, crawlers and uptime
checks that a missing page is missing.

The framework can only speak for the cases it knows about. `/products/9999` matches a real route and renders a
perfectly ordinary "no such product" page — only the page knows:

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

A faulted render still wins with `500` — a page that threw does not get to claim it succeeded. Setting `200` on
the not-found page is the supported way to express a deliberate soft-404.

`IPageResponse` is legal only during the initial server render (`Render`, `OnMount`, `OnMountAsync`). From an
event handler it **throws**: by then the response is long gone, and a silently dropped status is worse than a
crash you can see. On WASM it is a no-op — there is no response to shape — so a component calling it runs
unchanged on both hosts.

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

During the initial render the host turns that into a real **302**, before rendering a body at all — one
response instead of a whole page the client immediately navigates away from, and one a crawler and a cache both
understand where a client-side hop is neither. No session is left behind, and the redirect is `no-store`: one
computed from runtime state that a browser pinned would be unrecoverable without changing the URL.

Only same-site paths are accepted; anything else throws.

## See also

- [Lifecycle](lifecycle.md) — when `OnMountAsync` runs and what the initial render waits for.
- [Routing](routing.md) — `[NotFound]`, `Navigator`, and route-driven redirects.
- [Scaling](scaling.md) and [Deployment](deployment.md) — session accounting and sticky routing in production.
- [Single-page apps](spa.md) — a WebAssembly app, served by a server that renders none of its pages.
