# Routing

Rask routing is declaration-driven and source-generated. A routable component carries a `[Route]` attribute
naming the URL it answers; a module initializer (emitted by the `RoutesGenerator`) registers it at startup, and the `Router` in your `App` tree matches
the current URL against the registry and renders the matching page. The same generator also emits a **type-safe URL
builder** for every route, so links and navigation never carry stringly-typed paths that rot.

These APIs live in `Rask.Core.Routing`, which a `rask new` app already has in `GlobalUsings.cs`. A hand-wired
project or a component library adds it itself:

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

A route only matters once a `Router` is somewhere in the tree to match against it. The standard place is the page
root (`App`):

```csharp
public sealed partial class App : Component
{
    // The root renders into <body> — Rask composes the document around it.
    protected override Component? Render() =>
        Router;                   // matches RouteState.Path and renders the page
}
```

`Router` matches `RouteState.Path`, builds the route chain, instantiates each page via DI, binds URL pieces to
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

`Go` hands back a step that overwrites the current history entry instead of pushing a new one
(`UserPage.Go(42).Replacing()`). Like [`Go.To`](#programmatic-navigation--go), it may only be called **from an
event handler**.

> **`NavLink`, not `A`, for anywhere in your own app — and never a `target` on one.** The runtime intercepts
> clicks on `a[data-rask-nav]`, which `NavLink` writes — as do the kit's `Ui.Button.Href` and `Ui.Link.Href`
> when they are handed a generated route rather than a string ([UI kit](ui-kit.md#buttons-and-links-that-go-somewhere)). An
> `A.Href("/orders")` renders a perfectly valid link that the browser handles itself: a full document
> navigation that downloads and boots the whole app again, discarding every piece of client state on the way.
> Nothing warns you, because nothing is wrong with the markup — the link works, it is just not client-side.
> `target="_blank"` has the same effect from the other end: interception deliberately skips a link with a
> target, because the reader asked for a new browsing context. Reserve both for URLs that genuinely leave the
> app. This is worth being blunt about because it cost the framework's own site every internal link on its
> front door, and the symptom — a boot screen and a re-render on a link that "works" — reads as a
> performance problem rather than a markup one.

> **Inside a markup host, the bare page name is the chain's builder entry, not the type.** Every component —
> a page included — has a builder entry of the same name, and within a component class that entry wins name
> resolution and *constructs* the component. So `HomePage.Go()` written inside another page's `Render()` or
> handler does not compile: the entry hands back a `HomePage` *instance*, and `Go()` is a static extension
> on the *type*, so the compiler finds no `Go` for that receiver. This is the
> same "a component's static members need qualifying inside a markup host" rule the chain surface has
> everywhere. Two ways through it, both fine:
>
> ```csharp
> My.Features.Home.HomePage.Go();          // qualify the receiver (the namespace must still be imported)
> Routes.HomePage().Go();                  // or go through Routes, which never collides
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
to and from `string`, so you can pass it straight to `NavLink`, `Go.To`, or anywhere a path string is
expected.

`Url` returns that `RouteUrl` rather than a plain string on purpose: `Go.To` has a path-only overload
that **clears the query string**, so handing it a string would silently drop `?sort=asc`. When you do want the
string, the implicit conversion (or `.ToString()`) gives it to you.

Path values are formatted through `RouteValueFormatter.Format`, so an `int`, `Guid`, `DateOnly`, etc. round-trips
correctly without a manual `.ToString()`.

### One `Routes` class per project

The generator writes **one** `Routes` class, in the project's root namespace (`RootNamespace`, else the assembly
name), so code anywhere under it reaches every page with no `using` — a page in `Features.Shared` links to the
one in `Features.Home` directly:

```csharp
// before: Routes was per-namespace, so another folder needed an alias
using HomeRoutes = MyApp.Features.Home.Routes;
NavLink.Href(HomeRoutes.HomePage())["Home"];

// after: one Routes for the whole project
NavLink.Href(Routes.HomePage())["Home"];
```

A page whose type name is unique in the project is a flat method (`Routes.ProductPage(id)`). Pages that share a
type name nest under the folders that tell them apart — `Features.Admin.HomePage` and `Features.Shop.HomePage`
become `Routes.Admin.HomePage()` and `Routes.Shop.HomePage()`, and one whose namespace is the part they share
stays flat. A page named like one of those folders (a page called `Admin` beside `Routes.Admin`) cannot be
emitted and raises [RASK097](diagnostics.md#rask097).

> The generated navigation helpers and component chain symbols do not exist until the generator runs. If the
> IDE flags them as undefined, run `dotnet build` once and reload the solution.

For one page that has to answer more than one URL, repeat `[Route]` (see above): the first is the canonical URL
the helpers build, the rest are alternates the router matches.

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

Route/query binding feeds the lifecycle: `Updated*` fires on first render and whenever a bound param actually
changes value. See [lifecycle.md](lifecycle.md).

A worked example: the **data table** at `/table` holds *all* of its UI state — the search filter,
the sort column and direction, the current page and page size — in `[QueryParam]` properties, and writes each
header click and pager button back through `Go.With`. Because the state lives in the URL, it's
shareable and bookmarkable, and browser back/forward replay it for free. The source (the whole page, verbatim):

## Nested routes — `[ParentRoute]` + `Outlet`

A page can declare a parent layout with `[ParentRoute]`. The child's template is joined onto the
parent's, and the parent renders the matched child wherever it places an `Outlet`:

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
way: every page declares `[ParentRoute(typeof(ShowcaseLayout))]` and the layout hosts the `Outlet`.

<!-- demo:routing-nested-layout -->

`Outlet` must be called inside a `Router` render tree (it throws otherwise). A `[ParentRoute]` cycle raises
[RASK007](diagnostics.md#rask007).

### A page's title — `PageTitle` and `route.Title`

A layout usually shows what the page inside it is called: the last breadcrumb, the heading of a header bar, the
browser tab. The page **declares** it, and the layout **reads** it:

```csharp
[Route("/relations/{Id}")]
[ParentRoute(typeof(AppLayout))]
public sealed partial class RelationEditPage : Component
{
    private Relation? _relation;

    [RouteParam] public int Id { get; set; }

    protected override async Task OnUpdated() => _relation = await Relation.Find(Id);

    protected override string? PageTitle => _relation is { } r ? $"Edit {r.Name}" : null;

    protected override Component? Render() =>
        _relation is null ? Ui.Callout["No such relation"] : [Ui.Heading[PageTitle], /* the form */];
}

[Route("/")]
public sealed partial class AppLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } t ? $"{t} | Acme" : "Acme"];

    protected override Component? Render() =>
    [
        Ui.Breadcrumbs[
            Ui.BreadcrumbsItem.Href(Routes.HomePage()).Icon(Ui.IconName.Home),
            route.Title is { } t ? Ui.BreadcrumbsItem[t] : null
        ],
        Main[Outlet],
    ];
}
```

`PageTitle` is a member of every component and `null` by default; `route.Title` is the injected `RouteState`'s.
There is nothing to register, no shared service for the page to write into, and no event for the layout to
subscribe to.

**The first HTML already carries it.** The router mounts the page *before* the layout renders, so the title is
there the first time the layout asks — a direct load shows the crumb and the tab title with no script at all,
and a navigation changes the page, the crumb and `<title>` in one frame. Rask does not write `<title>` for
you: the layout's one `HeadAssets` line does, in whatever words the app wants around it.

- **It follows the page's data.** The title is read again on every render, so one built from a record loaded in
  `OnMount` / `OnUpdated` appears as soon as the record does, and a rename on the page — no navigation — moves the
  crumb and the tab in the same frame as the heading.
- **Only readers re-render, and only on a change.** A component that read `route.Title` while rendering renders
  again when the title differs, and not otherwise: a page that re-renders with the same title costs its layout
  nothing. That holds for a small component inside the layout's header as much as for the layout itself.
- **`null` is a page with no title.** The layout decides what that looks like — above, no crumb and the bare
  site name.
- **The deepest page that declares one wins.** With layouts nested through `[ParentRoute]`, the leaf's title is
  the one read; a leaf that declares none takes the title of the nearest layout above it that does.
- **It can be read above the `Router` too** — an `App` whose own `HeadAssets` writes the `<title>`. That
  component has rendered before the router has mounted anything, so when the title turns out to have changed
  Rask walks the tree once more before the frame goes out. It is right in the first HTML either way; a layout
  that reads it costs nothing extra, so prefer the layout.

Two things to know:

- **A title loaded after a real `await` arrives with the data.** The initial `GET` waits for it (see
  [the first response](render-modes.md#the-initial-get-waits-for-your-data)), so the served document is complete.
  A *navigation* in an open page paints the new page's placeholder first, exactly as the page itself does — and
  during that one frame the title is `null`. Declare a fallback (`_relation?.Name ?? "Relation"`) where an empty
  crumb would be wrong.
- **The page mounts before its layout renders.** `OnMount` / `OnUpdated` of every page in the chain run, up to
  their first `await`, before the outermost layout's `Render()` — see [Lifecycle](lifecycle.md#routed-pages). A
  page is therefore mounted even when its layout does not place the `Outlet` this render, and a `Context` value
  the layout provides is not yet in scope inside the page's *hooks* (it is in the page's `Render()`). Gate a
  whole page with `[Authorize]`, which is checked before any page is constructed.

## Programmatic navigation — `Go`

`Go` moves the user from code, with nothing injected — a typed route goes on its own (`Routes.UserPage(42).Go()`),
and a path you only have as text goes through `Go.To`:

```csharp
public sealed partial class ProductsPage : Component
{
    protected override Component? Render() =>
        Button.OnClick(() => Go.To("/dashboard"))["Open dashboard"];
}
```

**Event-handler only.** `Go.To`, `Go.With` and `Go.Without` throw `InvalidOperationException` if called outside an event
handler — calling it during `Render()` or the initial GET would mid-render the page out from under itself. Navigate
from button clicks, form submits, or lifecycle hooks that ran in response to an event. Navigation that must happen on
load belongs in a redirect/route, not in `Render()`.

`Go` changes the session's `RouteState`; after the handler returns, the live runtime pushes (or replaces) the
resulting URL into browser history.

Try it — every button changes this page's own query string with `Go.With` / `Go.Without`; watch the address bar and
the readout update over the live diff:

<!-- demo:routing-navigator -->

### Methods

```csharp
// Somewhere else — CLEARS any existing query string:
Go.To("/users/42");
Routes.UserPage(Id: 42).Go();               // type-safe, the same as Go.To(Routes.UserPage(Id: 42))

// A path with a complete new query in one step (REPLACES the whole query):
Go.To("/users/ada", [KeyValuePair.Create<string, string?>("tab", "profile")]);

// This page, with its query changed (path unchanged):
Go.With("page", "2");                       // set/update; a null value removes the key
Go.With(                                    // several at once
    KeyValuePair.Create<string, string?>("page", "2"),
    KeyValuePair.Create<string, string?>("sort", "asc"));
Go.Without("page");                         // remove one key (missing key = no-op)
Go.Without();                               // drop the whole query, keep the path
```

Key behaviours:

- `Go.To(path)` and `Go.To(RouteUrl)` **clear the query** unless the `RouteUrl` itself carries one. To go
  to a path and keep params, use the `Go.To(path, query)` overload or follow up with `Go.With`.
- `Go.To(path, query)` **replaces** the entire query string with the supplied pairs. Pairs with a `null` value are
  dropped; repeated keys concatenate into a multi-value param.
- `Go.With` / `Go.Without` operate on the **current** path and leave it unchanged — they're for
  partial query updates (`?page=2&sort=asc`).

### Replacing the history entry

`Go.To(…)` and a route's `.Go()` push a new history entry. Follow either with `.Replacing()` to replace the current
one instead, so it adds no extra Back-button stop:

```csharp
Go.To("/login").Replacing();               // redirect without a back-stack entry
Routes.LoginPage().Go().Replacing();
```

Front-end code in an [island](islands.md#navigating-from-an-island) navigates with the same two names —
`Routes.UserPage({ Id: 42 }).Go()`, `Go.With('page', '2')` — generated from these pages into `@rask/routes`.

Files go to the browser with `Download.File(…)`, under the same event-handler-only rule — see
[HTTP & files](http-and-files.md#downloading-files).

### Scroll position on navigation

Forward navigation — a `NavLink` click or `Go.To(...)` that **pushes** a history entry — scrolls the window back to
the top of the new page, matching how a server-rendered page load behaves. `.Replacing()` navigations and the browser's
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
- `route.Title` — what the current page calls itself, or `null`; see [A page's title](#a-pages-title--pagetitle-and-routetitle).

Mutate `RouteState` through `Go`, not by setting `Path`/`Query` directly, so browser history stays in sync.

### Reacting to navigation — `RouteState.Changed`

`RouteState` raises an `event EventHandler? Changed` whenever `Path` or `Query` actually changes (`Path` is compared by
value, `Query` by reference, so a no-op set doesn't fire). Components **inside** the routed page subtree usually don't
need it — the router re-renders them on navigation. But a component rendered **above** the `Router` (a sidebar,
breadcrumb, header path display) won't be re-rendered by the router, so it must subscribe explicitly. Subscribe in
`OnMount`, unsubscribe in `OnUnmount`:

```csharp
public sealed partial class PathDisplay(RouteState route) : Component
{
    protected override async Task OnMount() => route.Changed += StateHasChanged;
    protected override async Task OnUnmount() => route.Changed -= StateHasChanged;

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
[Live pages](render-modes.md#status-codes).

**Redirecting on load.** `Go.To` works during a page's initial render, and the Server
host turns it into a real `302` before rendering a body:

```csharp
protected override async Task OnMount()
{
    if (!_tenant.IsProvisioned)
    {
        Go.To("/onboarding");
    }
}
```

That costs one response rather than a whole page the client immediately navigates away from, and a
crawler and a cache both understand it where a client-side hop is neither. Called from a background
render — neither a handler nor the initial render — it still throws.

**Route-level authorization.** Put `[Authorize]` (optionally `[Authorize(Roles = "admin")]`) or `[AllowAnonymous]` on
a page component; the `RouteAuthorizationGuard` enforces it before the page renders. The session is a cookie and
`Rask.Auth` owns that scheme; roles and policies are ASP.NET's own `AddAuthorization`. Full flows on Server and
WASM are in [authentication.md](authentication.md).
