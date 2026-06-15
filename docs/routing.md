# Routing

Rask routing is attribute-driven and source-generated. You annotate a component with `[Route("/path")]`; a module
initializer (emitted by the `RoutesGenerator`) registers it at startup, and the `Router()` in your `App` tree matches
the current URL against the registry and renders the matching page. The same generator also emits a **type-safe URL
builder** for every route, so links and navigation never carry stringly-typed paths that rot.

Bring in the routing namespace where you use these APIs:

```csharp
using Rask.Core.Routing;
```

See also: [lifecycle.md](lifecycle.md) for hook timing on routed pages, [authentication.md](authentication.md) for
auth gating, and [diagnostics.md](diagnostics.md) for the routing analyzers (RASK003–RASK013).

## Registering routes

Put `[Route("/path")]` on a `Component`. That's the whole registration:

```csharp
[Route("/about")]
public sealed class AboutPage : Component
{
    protected override RenderResult Render() => H1()["About"];
}
```

Routes use Blazor-style `{param}` placeholders, support optional segments (`{name?}`), and accept type constraints
(`{id:int}`). The generator validates the template at compile time:

- A malformed template raises [RASK003](diagnostics.md#rask003).
- A segment with no matching property raises [RASK004](diagnostics.md#rask004).

A route only matters once a `Router()` is somewhere in the tree to match against it. The standard place is the page
root (`App`):

```csharp
public sealed class App : Component
{
    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),
                Body()[
                    Router()        // matches RouteState.Path and renders the page
                ]
            ]
        ];
}
```

`Router()` matches `RouteState.Path`, builds the route chain, instantiates each page via DI, binds URL pieces to
properties, and fires the page lifecycle.

### Type-safe URLs (`Routes.Page(...)`)

For each routed component `Foo`, the generator emits a static formatter `Routes.Foo(...)` returning a `RouteUrl`. The
formatter's parameters mirror the route's bound properties:

```csharp
[Route("/")]
public sealed class HomePage : Component { /* ... */ }
// → Routes.HomePage()  returns a RouteUrl for "/"

[Route("/users/{id:int}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
}
// → Routes.UserPage(int Id)  — the path param becomes a required argument
```

`RouteUrl` is a small `readonly record struct` carrying `Path` and an optional `QueryString`. It converts implicitly
to and from `string`, so you can pass it straight to `NavLink`, `Navigator.Navigate`, or anywhere a path string is
expected:

```csharp
NavLink(Href: Routes.UserPage(Id: 42))["View user"];
```

Path values are formatted through `RouteValueFormatter.Format`, so an `int`, `Guid`, `DateOnly`, etc. round-trips
correctly without a manual `.ToString()`. There is also a generic `Route<T>(...)` helper in
`Rask.Core.Routing.Generated` used by the registry machinery; in app code you almost always reach for the named
`Routes.Foo(...)` formatter.

> The generated `Routes.*` and component factory symbols don't exist until the generator runs. If the IDE flags them
> as undefined, run `dotnet build` once and reload the solution.

## Route and query parameters

Two attributes bind URL pieces to properties on the page:

- `[RouteParam]` — binds a **path segment** (`{id}` in the template) to a property.
- `[QueryParam]` — binds a **query-string** value to a property.

```csharp
[Route("/users/{id}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }       // /users/42  → Id = 42
    [QueryParam] public string? Tab { get; set; }   // ?tab=profile → Tab = "profile"

    protected override RenderResult Render() => Span()[$"User #{Id} — {Tab ?? "overview"}"];
}
```

Both attributes take an optional name to bind a segment/key whose name differs from the property:
`[RouteParam("id")]`, `[QueryParam("page")]`.

**Supported types.** A bound property must be `string` or implement `IParsable<T>` (covers `int`, `long`, `double`,
`bool`, `Guid`, `DateOnly`, `DateTime`, enums, and your own `IParsable<T>` types). A non-parsable type raises
[RASK011](diagnostics.md#rask011). When a route template constrains a segment (`{id:int}`), the bound property's CLR
type must match the constraint, or you get [RASK005](diagnostics.md#rask005).

Other binding-related analyzers worth knowing:

- [RASK006](diagnostics.md#rask006) — `[QueryParam]` placed on a property that's actually a path segment.
- [RASK008](diagnostics.md#rask008) — `[RouteParam]` with no matching path segment in the template.
- [RASK009](diagnostics.md#rask009) / [RASK010](diagnostics.md#rask010) — `[RouteParam]` / `[QueryParam]` on a class
  that isn't a routed page (no `[Route]`).

Route/query binding feeds the lifecycle: `OnPropsChanged*` fires on first render and whenever a bound param actually
changes value. See [lifecycle.md](lifecycle.md).

## Nested routes — `[ParentRoute]` + `Outlet()`

A page can declare a parent layout with `[ParentRoute(typeof(Parent))]`. The child's template is joined onto the
parent's, and the parent renders the matched child wherever it places an `Outlet()`:

```csharp
[Route("/")]
public sealed class Layout : Component
{
    protected override RenderResult Render() =>
        Div()[
            Nav()[ /* sidebar */ ],
            Main()[Outlet()]        // the matched child page renders here
        ];
}

[Route("about"), ParentRoute(typeof(Layout))]
public sealed class AboutPage : Component
{
    protected override RenderResult Render() => H1()["About"];
}

// /about now matches Layout → AboutPage, with AboutPage rendered into Layout's Outlet().
```

An empty child template (`[Route("")]`) means "the default child for this layout". The showcase app is built this way:
every page declares `[ParentRoute(typeof(ShowcaseLayout))]` and the layout hosts the `Outlet()`.

`Outlet()` must be called inside a `Router()` render tree (it throws otherwise). A `[ParentRoute]` cycle raises
[RASK007](diagnostics.md#rask007).

## Programmatic navigation — `Navigator`

`Navigator` is the scoped service for imperative navigation and query mutation. Inject it through the **constructor**
like any other framework service:

```csharp
public sealed class ProductsPage(Navigator nav) : Component
{
    protected override RenderResult Render() =>
        Button(OnClick: () => nav.Navigate("/dashboard"))["Open dashboard"];
}
```

**Event-handler only.** Every `Navigator` method throws `InvalidOperationException` if called outside an event
handler — calling it during `Render()` or the initial GET would mid-render the page out from under itself. Navigate
from button clicks, form submits, or lifecycle hooks that ran in response to an event. Navigation that must happen on
load belongs in a redirect/route, not in `Render()`.

`Navigator` mutates the shared `RouteState`; after the handler returns, the live runtime pushes (or replaces) the
resulting URL into browser history.

### Methods

```csharp
// Path navigation — CLEARS any existing query string:
nav.Navigate("/users/42");
nav.Navigate(Routes.UserPage(Id: 42));     // type-safe RouteUrl overload

// Path + a complete new query in one step (REPLACES the whole query):
nav.Navigate("/users/ada",
    new[] { KeyValuePair.Create<string, string?>("tab", "profile") });

// Single-param mutations on the CURRENT path (path unchanged):
nav.SetQuery("page", "2");                  // set/update; null value removes the key
nav.SetQuery(                               // several at once
    KeyValuePair.Create<string, string?>("page", "2"),
    KeyValuePair.Create<string, string?>("sort", "asc"));
nav.RemoveQuery("page");                    // remove one key (missing key = no-op)
nav.ClearQuery();                           // drop all query params, keep the path
```

Key behaviours:

- `Navigate(path)` and `Navigate(RouteUrl)` **clear the query** unless the `RouteUrl` itself carries one. To navigate
  to a path and keep params, use the `Navigate(path, query)` overload or follow up with `SetQuery`.
- `Navigate(path, query)` **replaces** the entire query string with the supplied pairs. Pairs with a `null` value are
  dropped; repeated keys concatenate into a multi-value param.
- `SetQuery` / `RemoveQuery` / `ClearQuery` operate on the **current** path and leave it unchanged — they're for
  partial query updates (`?page=2&sort=asc`).

### The `replace` flag

`Navigate(...)` overloads take an optional `replace` parameter (default `false`). `true` replaces the current history
entry instead of pushing a new one, so it adds no extra Back-button stop:

```csharp
nav.Navigate("/login", replace: true);   // redirect without a back-stack entry
```

`Navigator` also exposes `Download(...)` for pushing files to the browser (same event-handler-only rule); that lives in
the Files section of the README.

### Scroll position on navigation

Forward navigation — a `NavLink` click or `Navigate(...)` that **pushes** a history entry — scrolls the window back to
the top of the new page, matching how a server-rendered page load behaves. `replace: true` navigations and the browser's
Back/Forward buttons do **not** force a scroll reset: the browser's native scroll restoration owns those, so returning to
a page restores where you were. If a `NavLink`'s `Href` includes a `#fragment` that matches an element on the destination
page, the runtime scrolls to that element (and keeps the fragment in the address bar) instead of jumping to the top. This
is handled entirely in the client runtime and applies to both transports.

## Reading the current URL — `RouteState`

`RouteState` is the scoped, per-session source of truth for the current location. Inject it to read the live URL:

```csharp
public sealed class CurrentLocation(RouteState route) : Component
{
    protected override RenderResult Render() =>
        Div()[
            "path: ", Code()[route.Path],
            " query count: ", route.Query.Count
        ];
}
```

- `route.Path` — the current path, always starting with `/` (defaults to `"/"`).
- `route.Query` — the parsed query string as an `IQueryCollection` (defaults to empty).

Mutate `RouteState` through `Navigator`, not by setting `Path`/`Query` directly, so browser history stays in sync.

### Reacting to navigation — `RouteState.Changed`

`RouteState` raises an `event Action? Changed` whenever `Path` or `Query` actually changes (`Path` is compared by
value, `Query` by reference, so a no-op set doesn't fire). Components **inside** the routed page subtree usually don't
need it — the router re-renders them on navigation. But a component rendered **above** the `Router()` (a sidebar,
breadcrumb, header path display) won't be re-rendered by the router, so it must subscribe explicitly. Subscribe in
`OnMount`, unsubscribe in `OnUnmount`:

```csharp
public sealed class PathDisplay(RouteState route) : Component
{
    protected override void OnMount() => route.Changed += StateHasChanged;
    protected override void OnUnmount() => route.Changed -= StateHasChanged;

    protected override RenderResult Render() =>
        Span()["path: ", Code()[route.Path]];
}
```

The handler is just `StateHasChanged` — the framework coalesces the resulting render with whatever the dispatcher is
already processing. **Always pair the subscribe with the unsubscribe**, or `RouteState` keeps a strong reference to the
unmounted component. (`NavLink` and `Outlet` do this subscription internally so they stay current even outside the
router subtree.)

## Not-found and auth gating

**404 / catch-all.** Mark a component `[NotFound]` to register it as the catch-all page when no route matches; the
framework falls back to a minimal built-in page if no app-defined one exists.

```csharp
[NotFound]
public sealed class NotFoundPage : Component
{
    protected override RenderResult Render() => H1()["Page not found"];
}
```

Only one `[NotFound]` component is allowed ([RASK012](diagnostics.md#rask012)), and `[NotFound]` cannot be combined
with `[Route]` on the same class ([RASK013](diagnostics.md#rask013)).

**Route-level authorization.** Put `[Authorize]` (optionally `[Authorize(Roles = "admin")]`) or `[AllowAnonymous]` on
a page component; the `RouteAuthorizationGuard` enforces it before the page renders. Auth is configured entirely on
ASP.NET's own `AddCookie` / `AddJwtBearer` / `AddAuthorization` — Rask adds no parallel options. Full flows for
cookie/JWT on Server and WASM are in [authentication.md](authentication.md).
