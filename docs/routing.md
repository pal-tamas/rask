# Routing

Rask routing is declaration-driven and source-generated. A routable component carries a `[Route]` attribute
naming the URL it answers; a module initializer (emitted by the `RoutesGenerator`) registers it at startup, and the `Router()` in your `App` tree matches
the current URL against the registry and renders the matching page. The same generator also emits a **type-safe URL
builder** for every route, so links and navigation never carry stringly-typed paths that rot.

Bring in the routing namespace where you use these APIs:

```csharp
using Rask.Core.Routing;
```

See also: [lifecycle.md](lifecycle.md) for hook timing on routed pages, [authentication.md](authentication.md) for
auth gating, and [diagnostics.md](diagnostics.md) for the routing analyzers (RASK003–RASK013).

## Registering routes

Put `[Route]` on a component. That's the whole registration:

```csharp
[Route("/about")]
public sealed partial class AboutPage : Component
{
    protected override Component? Render() => H1["About"];
}
```

The template is read **at compile time** — it builds the route table and the typed URL helpers below. Being an
attribute argument, it is constant by construction: a literal, a `const`, or constant concatenation all work, and
nothing computed can be written there at all. Carrying `[Route]` is also what makes a class a valid target for
`[RouteParam]`/`[QueryParam]` ([RASK009](diagnostics.md#rask009)/[RASK010](diagnostics.md#rask010)).

### One page, several URLs

`[Route]` is repeatable, which is how a page answers more than one URL — an old path kept alive after a rename, or
one screen whose sub-states are their own links:

```csharp
[Route("/todos")]
[Route("/todos/new")]
[Route("/todos/{id:guid}/edit")]
public sealed partial class TodosPage : Component
{
}
```

Every template is matched by the router and renders the same page. The **first one declared is canonical**: it is
what `TodosPage.Url(...)` and `Routes.TodosPage(...)` format, so a generated link can never drift onto a path you
meant to keep only for old bookmarks. Read the current URL from `RouteState` to tell the states apart. Under a
`[ParentRoute]`, each template composes onto the parent's first template rather than every combination of the
two.

Routes use Blazor-style `{param}` placeholders, support optional segments (`{name?}`), and accept type constraints
(`{id:int}`). The generator validates the template at compile time:

- A malformed template raises [RASK003](diagnostics.md#rask003).
- A segment with no matching property raises [RASK004](diagnostics.md#rask004).

A route only matters once a `Router()` is somewhere in the tree to match against it. The standard place is the page
root (`App`):

```csharp
public sealed partial class App : Component
{
    // The root renders into <body> — Rask composes the document around it.
    protected override Component? Render() =>
        Router;                   // matches RouteState.Path and renders the page
}
```

`Router()` matches `RouteState.Path`, builds the route chain, instantiates each page via DI, binds URL pieces to
properties, and fires the page lifecycle.

### Type-safe URLs — `SomePage.Url(...)` and `SomePage.Go(...)`

Each page gets two generated helpers, on the page type itself. `Url(...)` builds the `RouteUrl`; `Go(...)`
navigates to it. Their parameters mirror the route's bound properties:

```csharp
[Route("/")]
public sealed partial class HomePage : Component
{
}
// → HomePage.Url()   returns a RouteUrl for "/"
// → HomePage.Go()    navigates there

[Route("/users/{id:int}")]
public sealed partial class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
}
// → UserPage.Url(int Id) / UserPage.Go(int Id)  — the path param is a required argument
```

Use `Url` where a link's target belongs and `Go` where a handler navigates:

```csharp
NavLink.Href(UserPage.Url(Id: 42))["View user"];
Button.OnClick(() => UserPage.Go(42))["View user"];
```

`Go` takes a trailing `replace` flag (`UserPage.Go(42, replace: true)`) to overwrite the current history entry
instead of pushing a new one, and it navigates through the ambient `Navigator.Current` — so, like `Navigator`
itself, it may only be called **from an event handler**.

> **Inside a markup host, the bare page name is the chain's builder entry, not the type.** Every component —
> a page included — has a builder entry of the same name, and within a component class that entry wins name
> resolution and *constructs* the component. So `HomePage.Go()` written inside another page's `Render()` or
> handler does not compile (`CS1929: 'Build<HomePage>' does not contain a definition for 'Go'`). This is the
> same "a component's static members need qualifying inside a markup host" rule the chain surface has
> everywhere. Two ways through it, both fine:
>
> ```csharp
> My.Features.Home.HomePage.Go();          // qualify the receiver (the namespace must still be imported)
> navigator.NavigateTo(Routes.HomePage()); // or use the Routes formatter, which never collides
> ```
>
> `HomePage.Go()` unqualified is at its best from code that is *not* a markup host — a service, a handler
> class, `Program.cs`.

> **These need the page's namespace imported.** They are C# 14 static extension members, which resolve only
> when their containing namespace is in scope — a fully-qualified `My.Features.HomePage.Go()` with no `using`
> does not compile. The `using` that lets you name the page type is the same one that brings `Url`/`Go` along,
> so this only bites when you fully qualify. They also require `LangVersion` 14 or later (the .NET 10
> default); below that they are not emitted and the older `Routes.SomePage(...)` formatter is what you use.

`RouteUrl` is a small `readonly record struct` carrying `Path` and an optional `QueryString`. It converts implicitly
to and from `string`, so you can pass it straight to `NavLink`, `Navigator.NavigateTo`, or anywhere a path string is
expected.

`Url` returns that `RouteUrl` rather than a plain string on purpose: `Navigator.NavigateTo` has a path-only overload
that **clears the query string**, so handing it a string would silently drop `?sort=asc`. When you do want the
string, the implicit conversion (or `.ToString()`) gives it to you.

Path values are formatted through `RouteValueFormatter.Format`, so an `int`, `Guid`, `DateOnly`, etc. round-trips
correctly without a manual `.ToString()`.

> The generated navigation helpers and component chain symbols do not exist until the generator runs. If the
> IDE flags them as undefined, run `dotnet build` once and reload the solution.

For one page that has to answer more than one URL, register the extra template yourself —
`RouteRegistry.Add(new RouteRegistration(typeof(MyPage), "/alias", null))`. `Route` is deliberately singular:
one page, one canonical URL, one formatter.

## Route and query parameters

Two attributes bind URL pieces to properties on the page:

- `[RouteParam]` — binds a **path segment** (`{id}` in the template) to a property.
- `[QueryParam]` — binds a **query-string** value to a property.

```csharp
[Route("/users/{id}")]
public sealed partial class UserPage : Component
{
    [RouteParam] public int Id { get; set; }       // /users/42  → Id = 42
    [QueryParam] public string? Tab { get; set; }   // ?tab=profile → Tab = "profile"

    protected override Component? Render() => Span[$"User #{Id} — {Tab ?? "overview"}"];
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
  that isn't a routed page (carries no `[Route]`).

Route/query binding feeds the lifecycle: `OnPropsChanged*` fires on first render and whenever a bound param actually
changes value. See [lifecycle.md](lifecycle.md).

A worked example: the **data table** at `/table` holds *all* of its UI state — the search filter,
the sort column and direction, the current page and page size — in `[QueryParam]` properties, and writes each
header click and pager button back through `Navigator.SetQuery`. Because the state lives in the URL, it's
shareable and bookmarkable, and browser back/forward replay it for free. The source (the whole page, verbatim):

<!-- demo:routing-querytable -->

## Nested routes — `[ParentRoute]` + `Outlet()`

A page can declare a parent layout with `[ParentRoute]`. The child's template is joined onto the
parent's, and the parent renders the matched child wherever it places an `Outlet()`:

```csharp
[Route("/")]
public sealed partial class Layout : Component
{
    protected override Component? Render() =>
        Div[
            Nav[ /* sidebar */ ],
            Main[Outlet]        // the matched child page renders here
        ];
}

[Route("about")]
[ParentRoute(typeof(Layout))]
public sealed partial class AboutPage : Component
{
    protected override Component? Render() => H1["About"];
}

// /about now matches Layout → AboutPage, with AboutPage rendered into Layout's Outlet.
```

An empty child template (`[Route("")]`) means "the default child for this layout". The showcase app is built this
way: every page declares `[ParentRoute(typeof(ShowcaseLayout))]` and the layout hosts the `Outlet()`.

<!-- demo:routing-nested-layout -->

`Outlet()` must be called inside a `Router()` render tree (it throws otherwise). A `[ParentRoute]` cycle raises
[RASK007](diagnostics.md#rask007).

## Programmatic navigation — `Navigator`

`Navigator` is the scoped service for imperative navigation and query mutation. Inject it through the **constructor**
like any other framework service:

```csharp
public sealed partial class ProductsPage(Navigator nav) : Component
{
    protected override Component? Render() =>
        Button.OnClick(() => nav.NavigateTo("/dashboard"))["Open dashboard"];
}
```

**Event-handler only.** Every `Navigator` method throws `InvalidOperationException` if called outside an event
handler — calling it during `Render()` or the initial GET would mid-render the page out from under itself. Navigate
from button clicks, form submits, or lifecycle hooks that ran in response to an event. Navigation that must happen on
load belongs in a redirect/route, not in `Render()`.

`Navigator` mutates the shared `RouteState`; after the handler returns, the live runtime pushes (or replaces) the
resulting URL into browser history.

Try it — every button mutates this page's own query string through the scoped `Navigator`; watch the address bar and
the readout update over the live diff:

<!-- demo:routing-navigator -->

### Methods

```csharp
// Path navigation — CLEARS any existing query string:
nav.NavigateTo("/users/42");
nav.NavigateTo(Routes.UserPage(Id: 42));     // type-safe RouteUrl overload

// Path + a complete new query in one step (REPLACES the whole query):
nav.NavigateTo("/users/ada",
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

- `NavigateTo(path)` and `NavigateTo(RouteUrl)` **clear the query** unless the `RouteUrl` itself carries one. To navigate
  to a path and keep params, use the `NavigateTo(path, query)` overload or follow up with `SetQuery`.
- `NavigateTo(path, query)` **replaces** the entire query string with the supplied pairs. Pairs with a `null` value are
  dropped; repeated keys concatenate into a multi-value param.
- `SetQuery` / `RemoveQuery` / `ClearQuery` operate on the **current** path and leave it unchanged — they're for
  partial query updates (`?page=2&sort=asc`).

### The `replace` flag

`NavigateTo(...)` overloads take an optional `replace` parameter (default `false`). `true` replaces the current history
entry instead of pushing a new one, so it adds no extra Back-button stop:

```csharp
nav.NavigateTo("/login", replace: true);   // redirect without a back-stack entry
```

`Navigator` also exposes `Download(...)` for pushing files to the browser (same event-handler-only rule); that lives in
the Files section of the README.

### Scroll position on navigation

Forward navigation — a `NavLink` click or `NavigateTo(...)` that **pushes** a history entry — scrolls the window back to
the top of the new page, matching how a server-rendered page load behaves. `replace: true` navigations and the browser's
Back/Forward buttons do **not** force a scroll reset: the browser's native scroll restoration owns those, so returning to
a page restores where you were. If a `NavLink`'s `Href` includes a `#fragment` that matches an element on the destination
page, the runtime scrolls to that element (and keeps the fragment in the address bar) instead of jumping to the top. This
is handled entirely in the client runtime and applies to both transports.

## Reading the current URL — `RouteState`

`RouteState` is the scoped, per-session source of truth for the current location. Inject it to read the live URL:

```csharp
public sealed partial class CurrentLocation(RouteState route) : Component
{
    protected override Component? Render() =>
        Div[
            "path: ", Code[route.Path],
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
public sealed partial class PathDisplay(RouteState route) : Component
{
    protected override void OnMount() => route.Changed += StateHasChanged;
    protected override void OnUnmount() => route.Changed -= StateHasChanged;

    protected override Component? Render() =>
        Span["path: ", Code[route.Path]];
}
```

The handler is just `StateHasChanged` — the framework coalesces the resulting render with whatever the dispatcher is
already processing. **Always pair the subscribe with the unsubscribe**, or `RouteState` keeps a strong reference to the
unmounted component. (`NavLink` and `Outlet` do this subscription internally so they stay current even outside the
router subtree.)

<!-- demo:routing-route-state -->

## Not-found and auth gating

**404 / catch-all.** Mark a component `[NotFound]` to register it as the catch-all page when no route matches; the
framework falls back to a minimal built-in page if no app-defined one exists.

```csharp
[NotFound]
public sealed partial class NotFoundPage : Component
{
    protected override Component? Render() => H1["Page not found"];
}
```

Only one `[NotFound]` component is allowed ([RASK012](diagnostics.md#rask012)), and `[NotFound]` cannot be combined
with `[Route]` on the same class ([RASK013](diagnostics.md#rask013)).

On the Server host that page is served with a real **404** status. It used to answer `200`, which
told every cache, crawler and uptime check that a missing page was fine. The body is unchanged — the
page still renders and the live session still attaches — so navigating away from it still works.

An app that declares its **own** catch-all `[Route("/{**rest}")]` is deliberately serving those
paths, so it stays `200`. And a page that matches a real route but finds no data — `/products/9999` —
is not a routing fact at all: say so with `IPageResponse.SetStatus(404)`, described in
[Render modes](render-modes.md).

**Redirecting on load.** `Navigator.NavigateTo` works during a page's initial render, and the Server
host turns it into a real `302` before rendering a body:

```csharp
protected override void OnMount()
{
    if (!_tenant.IsProvisioned)
    {
        navigator.NavigateTo("/onboarding");
    }
}
```

That costs one response rather than a whole page the client immediately navigates away from, and a
crawler and a cache both understand it where a client-side hop is neither. Called from a background
render — neither a handler nor the initial render — it still throws.

**Route-level authorization.** Put `[Authorize]` (optionally `[Authorize(Roles = "admin")]`) or `[AllowAnonymous]` on
a page component; the `RouteAuthorizationGuard` enforces it before the page renders. Auth is configured entirely on
ASP.NET's own `AddCookie` / `AddJwtBearer` / `AddAuthorization` — Rask adds no parallel options. Full flows for
cookie/JWT on Server and WASM are in [authentication.md](authentication.md).
