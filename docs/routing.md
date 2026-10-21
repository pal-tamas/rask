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

In a Web SDK project without it, `[Route]` finds ASP.NET's `Microsoft.AspNetCore.Routing.Route` class instead
and the build stops with `CS0616: 'Route' is not an attribute class` — the fix is the line above. A hand-wired host
puts it in the page files rather than in a global using when it also has MVC controllers: their `[Route]` is
`Microsoft.AspNetCore.Mvc.RouteAttribute`, and both in scope is CS0104 (see [SPA hosting](spa.md#moving-an-existing-app-onto-rask-a-page-at-a-time)).

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
(`{id:int}`, `{id:long}`, `{flag:bool}`, `{id:guid}`). **A segment that is not what its route says does not
match**: `/users/abc` and `/users/99999999999999999999` are not `/users/{id:int}`, so the next route is tried
and the [not-found page](#not-found-and-auth-gating) answers — with a 404 on a first request — when none takes it. The same
holds without a constraint, for the type of the property the segment binds to: `/on/2026-13-45` is not a
`DateOnly`. A number is read as .NET reads it, culture-invariant: `007` and `+5` are numbers, `1,000` and
`1.5` are not an `int`. A `bool` reads `true`/`false` in any case and `1`/`0` (as Laravel's `boolean` rule
does) — never `on`/`off` or `yes`/`no` — and a generated URL always writes `true`/`false`. The generator validates the template at compile time:

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

A `[QueryParam]` property is an optional argument after the path's, whatever its type, and **a query argument
that is not given is not written**:

```csharp
[Route("/orders")]
public sealed partial class OrdersPage : Component
{
    [QueryParam] public int Page { get; set; } = 1;
    [QueryParam] public string? Sort { get; set; }
}
// OrdersPage.Url()                       → /orders                 (the page shows Page = 1)
// OrdersPage.Url(Page: 3)                → /orders?Page=3
// OrdersPage.Url(Page: 1)                → /orders?Page=1          (given, so written — nothing is compared)
// OrdersPage.Url(Page: 3, Sort: "date")  → /orders?Page=3&Sort=date
// OrdersPage.Url(Sort: "")               → /orders?Sort=           (an empty string is a value)
```

A key left out lands on [the property's initializer](#route-and-query-parameters), so the link to a list is
its clean address and only a link that says a page carries one. `Routes.OrdersPage(…)`, `Url(…)` and `Go(…)`
are one formatter and agree, and so does the TypeScript `Routes` an island imports.

Use `Url` where a link's target belongs and `Go` where a handler navigates:

```csharp
NavLink.Href(UserPage.Url(Id: 42))["View user"];
Button.OnClick(() => UserPage.Go(42))["View user"];
```

`Go` hands back a step that overwrites the current history entry instead of pushing a new one
(`UserPage.Go(42).Replacing()`). Like [`Go.To`](#programmatic-navigation--go), it is called from an
event handler, or from a page's lifecycle hook to
[send the reader on](#redirecting-from-a-lifecycle-hook).

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
`bool`, `Guid`, `DateOnly`, `DateTime`, and your own `IParsable<T>` types). An enum is not `IParsable<T>`: bind
it as a `string` and convert inside the page. A non-parsable type raises
[RASK011](diagnostics.md#rask011). When a route template constrains a segment (`{id:int}`), the bound property's CLR
type must match the constraint, or you get [RASK005](diagnostics.md#rask005).

**An absent query parameter keeps the property's initializer.** Write the default where the property is
declared:

```csharp
[QueryParam] public int Page { get; set; } = 1;       // /orders → 1 · ?page=3 → 3 · ?page=abc → 1
[QueryParam] public string? Sort { get; set; }        // no initializer: null without ?sort=
```

A page instance is reused from one address to the next, and each bind starts over from the address: going
from `?page=3` to no `page` puts `Page` back to `1` — not the `3` of the address before, and not `0`. What
comes back is the value the property held when the page was built (an initializer, or what the constructor
gave it), so a value your own code assigned to the property since is not kept either. A property with no
initializer gets its type's default — `null`, `0`, `false`. An initializer that is an object (a type of your
own implementing `IParsable<T>`) comes back as that same object, not a copy: keep such a type immutable.
The generated `Routes.OrdersPage(…)` knows nothing of the initializer — it writes the parameters it is
given and leaves out the ones it is not, so `Routes.OrdersPage()` is `/orders` and
`Routes.OrdersPage(Page: 1)` is `/orders?Page=1`.

**An optional segment left out keeps the initializer too** — it is one rule: what is not in the URL gets the
value the page was built with.

```csharp
[Route("/items/{count?}")]
public sealed partial class ItemsPage : Component
{
    [RouteParam] public int Count { get; set; } = 5;     // /items → 5 · /items/7 → 7 · back to /items → 5
}
```

The same holds for a segment that only some of a page's routes have: on `[Route("/todos")]` +
`[Route("/todos/{id:guid}/edit")]`, going from `/todos/…/edit` to `/todos` puts `Guid? Id` back to `null` —
it has no initializer — which is what closes the edit dialog. A segment that is *there* and cannot be read is
not absent: it [does not match](#registering-routes).

**A query value the page cannot read is read as absent.** `?weightFrom=abc`, `?weightFrom=1.5`, `?weightFrom=`
or digits past what an `int` holds bind `[QueryParam("weightFrom")] public int? WeightFrom` exactly as a URL
with no `weightFrom` does, and the page renders — a link with filters in it is mailed around, kept for a year
and edited by hand, so its values are a visitor's input and never an error. That holds for every bindable
type, nullable or not, on a first request and on a live navigation alike, and absent means what it means
above: the property's initializer, or its type's default when it has none. The readable parameters beside an unreadable one are bound as usual, a
repeated key (`?page=two&page=2`) is read from its first value only, and the address stays as the visitor
wrote it. In Development the ignored parameter is named once on the `Rask.Routing` log category, without its
value. (A path *segment* the page cannot read is different: it [does not match](#registering-routes).)

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

**Only the routed page is asked.** A component the page renders — a form shared between a "new" and an "edit"
page, say — cannot name the page. So the routed page owns the load that names it: load the record in the page's
`OnUpdated`, state `PageTitle` there, and hand the record down to the form as a prop. A page whose record is
missing answers `null`: the tab shows the layout's fallback and the trail ends at the list.

**If your app already has a component called `PageTitle`,** rename it: inside a component the bare name now
means this member, not your component's chain entry.

**The first HTML already carries it.** A page is mounted where its layout places the `Outlet`, so the layout
has rendered by the time the page's title is known. When the title turns out to have changed, Rask renders
whatever read it once more before anything is sent: a direct load shows the crumb and the tab title with no
script at all, and a navigation changes the page, the crumb and `<title>` in one frame. Rask does not write
`<title>` for you: the layout's one `HeadAssets` line does, in whatever words the app wants around it.

- **It follows the page's data.** The title is read again on every render, so one built from a record loaded in
  `OnMount` / `OnUpdated` appears as soon as the record does, and a rename on the page — no navigation — moves the
  crumb and the tab in the same frame as the heading.
- **The layout renders once more when the title changes, and never otherwise.** A component that read
  `route.Title` while rendering renders again when the title differs — a navigation to a page called something
  else, a rename — and a page that re-renders with the same title costs its layout nothing. The page itself is
  not rendered again, and none of its hooks re-run. That holds for a small component inside the layout's header
  as much as for the layout itself, and reading the title from the smallest component that shows it keeps
  that extra render small.
- **`null` is a page with no title.** The layout decides what that looks like — above, no crumb and the bare
  site name.
- **The deepest page that declares one wins.** With layouts nested through `[ParentRoute]`, the leaf's title is
  the one read; a leaf that declares none takes the title of the nearest layout above it that does.
- **It can be read above the `Router` too** — an `App` whose own `HeadAssets` writes the `<title>` — on the
  same terms.

Two things to know:

- **A title loaded after a real `await` arrives with the data.** The initial `GET` waits for it (see
  [the first response](render-modes.md#the-initial-get-waits-for-your-data)), so the served document is complete.
  A *navigation* in an open page waits for it too — see [the page is sent when its first reads are
  back](#the-page-is-sent-when-its-first-reads-are-back) — so the page above never shows "No such relation" for
  a relation that exists, and the crumb, the tab title and the form arrive in one frame. Only a read that
  outlasts the navigation budget is painted before it is back, and during that time the title is `null`:
  declare a fallback (`_relation?.Name ?? "Relation"`) where an empty crumb would be wrong.
- **The page keeps its place.** A layout that renders a breadcrumb only once there is a title changes shape
  around its `Outlet` when the title arrives. The page is remembered by its place in the route, not by the
  outlet that shows it, so it is not created again and what it loaded stays loaded. Nothing else about a routed
  page's life changed: it is constructed and mounted where the layout places the `Outlet` and only there, so a
  layout that withholds it (`Authorize.Role("admin")[Outlet]`) keeps the page's hooks from running.

### The page is sent when its first reads are back

A page reached **in place** — a `NavLink`, Back, `Go()` from a handler, a redirect, the page a sign-in returns
to — is not sent the moment it is mounted. Rask renders it, waits for what its `OnMount` and `OnUpdated`
started, renders it again with what they read, and sends it **once**: the list with its rows, the edit page
with its record and its title. It is the first request's wait
([the initial `GET` waits for your data](render-modes.md#the-initial-get-waits-for-your-data)), for a page
that is already open, on the Server host and in WebAssembly.

```csharp
protected override async Task OnUpdated() => (_rows, _total) = await Relation.Page(Page);   // waited for

private async Task Save()
{
    await _relation.Save();
    Routes.RelationListPage().Go();     // one frame: the list, its rows and its address
}
```

- **Meanwhile the reader keeps the page they are on.** Nothing of the next page is painted until it is whole.
  The control they pressed is marked busy after 200 ms (`data-loading`, `aria-busy`, for a button) and the
  progress bar runs across the top after 300 ms — both as before, and both gone when the page arrives.
- **The budget is 150 ms.** A page whose reads are back within it is never seen without them. One whose reads
  take longer is sent as it stands when the budget is spent — its heading, its placeholder — and the rest
  follows when it arrives, exactly as before. Nothing is cancelled. So a page still says what it shows while
  it loads; it is only seen when the load is slow.
- **A page with nothing to wait for is sent at once.** The wait is for work the render started; with none
  pending there is no delay at all, not even a timer.
- **A second link ends the wait for the first.** The reader who presses another link while a page is still
  loading gets that page; nothing of the first is sent.
- **A page that sends the reader on is never shown**, whether it calls `Go()` before its first `await` or after
  it: the wait reaches the redirect, and the destination arrives in its place.
- **The page's `autofocus` field has focus when it arrives**, as it has when the address is loaded: the first
  one on screen, in place of the move to the page's `<main>`, with the route announced all the same
  ([accessibility](accessibility.md#autofocus-on-a-page-reached-in-place)). A page with none is focused as before.
- **A save's own repaint waits for the address.** A handler that has called `Go()` is not repainted while it is
  still running, so the destination never appears before the address bar says it.
- **A page that reads through `Rask.Query` is waited for the same way.** A query read while the page renders
  (`orders.IsLoading ? Spinner() : Table(orders.Data)`) is waited for when it has nothing to show, and when a
  save has just invalidated it — so `await Save(); Routes.OrderListPage().Go()` lands on the fresh rows, not
  on the ones from before the save. A query already in the session's cache is shown at once, with no wait and
  no fetch; one that is merely old is shown as it is and refreshed behind it.
- **Work blocked on JavaScript is not waited for**, and neither is work you detach (`_ = Poll()`): the same two
  exceptions as the first request.

```jsonc
{ "Rask": { "Server": { "NavigationQuiescenceTimeout": "00:00:00.150" } } }   // default; "00:00:00" sends at once
```

In a WebAssembly app it is `host.Configure(o => o.NavigationQuiescenceTimeout = 150.Milliseconds)`. Raise it
for an app whose pages read from a slow store and would rather wait than flash; set it to zero to send every
page the moment it is mounted.

### A handler's work reaches the screen once

The same holds for a page that stays where it is. **Nothing changes on screen until the handler has
answered** — as in Livewire — so paging, sorting and filtering arrive as one frame, the pager and the rows
together, however the page loads them:

```csharp
// the handler awaits the read itself
.OnPage(async page => { _page = page; _rows = await Orders.Page(page); })

// the handler only moves the number, and a child reads again in OnUpdated
.OnPage(page => _page = page)          // … OrderRows.Page(_page)

// or a query's key moves
Query<Page<Order>> Orders => field ??= QueryClient.Query(() => new GetOrders(_page));

// or the address does: sort and filter that live in the query string
.OnSort(() => Go.With("sort", "date"))
```

- **What a handler sets between its awaits is not sent while it runs.** `_loading = true; _rows = null;`
  before a read that is back in time is never seen: the reader keeps the rows they have, and gets the new
  ones. A handler that finishes within the budget is one frame, its last.
- **The render after the handler waits for the reads its change started** — a child's `OnUpdated`, a
  `Rask.Query` fetch whose key moved or that a save invalidated — exactly as a navigation waits for its page.
- **The budget is 150 ms**: for the handler, counted from when it starts, and then again for the reads the
  render after it started. Spending it cancels nothing: the page is sent as it stands at that moment, and the
  rest follows when it arrives. So a slow page change shows the
  pager ahead of the old rows for a while, and a long save shows its "Saving…" — at 150 ms rather than at
  once. Past the budget every later `await` repaints as it lands, as before.
- **The reader is told the event is done only when the rows are there.** The pressed control's busy mark
  (200 ms) and the bar across the top (300 ms) end with the server's acknowledgement, and that is sent after
  the reads the page went without — so a slow sort by `Go.With` is busy until its rows arrive, where the mark
  used to end at the budget. The next event is not held up by it, and the wait lasts **at most two seconds**
  from that frame: a read that takes longer, or never returns, is told done then, and what it brings later is
  an ordinary frame.
- **A handler with nothing to wait for is answered at once.** A keystroke, a toggle, a counter: there is no
  delay, and not even a timer.
- **Work blocked on JavaScript is not waited for**, nor is work you detach, nor a render nobody dispatched (a
  timer's `StateHasChanged`, a subscription's value): those are sent as they stand.

```jsonc
{ "Rask": { "Server": { "HandlerQuiescenceTimeout": "00:00:00.150" } } }   // default; "00:00:00" repaints at every await
```

In a WebAssembly app it is `host.Configure(o => o.HandlerQuiescenceTimeout = 150.Milliseconds)`. Zero turns it
off: every intermediate state is sent the moment the handler awaits, and nothing is waited for. The kit's table dims
its rows while a page change is slow — see [tables](ui-kit.md#tables).

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

**Where it works.** In an event handler — a click, a submit — and in a page's `OnMount` / `OnUpdated`, where it
is a redirect ([below](#redirecting-from-a-lifecycle-hook)). Anywhere no session is handling something for a
reader — a background task, a component rendered with no host — `Go.To`, `Go.With` and `Go.Without` throw
`InvalidOperationException`, and the message says where to call them instead.

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
  partial query updates (`?page=2&sort=asc`). A move that changes only the query **replaces** the current history
  entry, as Livewire's `#[Url]` does, so a live search adds no Back stop at every pause; follow it with `.Pushing()`
  (`Go.With("page", "2").Pushing()`) when the move is a place the reader should be able to go Back to.

### Replacing the history entry

`Go.To(…)` and a route's `.Go()` push a new history entry. Follow either with `.Replacing()` to replace the current
one instead, so it adds no extra Back-button stop (`Go.With` and `Go.Without` replace by default, and take
`.Pushing()` to add one):

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

The built-in page says three things — a heading that is also the tab's title, a sentence with the path that was
asked for, and a link home — in the visitor's language: `RaskString.NotFoundTitle`, `NotFoundBody` (the path is its
`{0}`) and `NotFoundBackHome` ([localization](localization.md)). The path is written as text, and nothing else of
the request is shown.

Only one `[NotFound]` component is allowed ([RASK012](diagnostics.md#rask012)), and `[NotFound]` cannot be combined
with `[Route]` on the same class ([RASK013](diagnostics.md#rask013)).

On the Server host that page is served with a real **404** status. It used to answer `200`, which
told every cache, crawler and uptime check that a missing page was fine. The body is unchanged — the
page still renders and the live session still attaches — so navigating away from it still works.

An app that declares its **own** catch-all `[Route("/{**rest}")]` is deliberately serving those
paths, so it stays `200`. And a page that matches a real route but finds no data — `/products/9999` —
is not a routing fact at all: say so with `IPageResponse.SetStatus(404)`, described in
[Live pages](render-modes.md#status-codes).

### Redirecting from a lifecycle hook

A page that decides the reader belongs elsewhere says so with the `Go()` it would call from a handler:

```csharp
protected override async Task OnMount()
{
    if (!_tenant.IsProvisioned)
    {
        Go.To("/onboarding");                  // before the first await
        return;
    }

    _record = await Record.Find(Id);
    if (_record is null)
    {
        Routes.RecordListPage().Go();          // after it: record not found, back to the list
    }
}
```

It means the same however the page was reached — its address typed, a `NavLink`, Back, `Go()` from a handler,
the page a sign-in returns to, a session rebuilt after a deploy — on the Server host and in WebAssembly:

- **Before the first `await`** (and in `OnUpdated`, when a parameter changes on a page that stays mounted) the
  reader sees nothing of the page that redirected. A first request is answered with a real `302`, which a
  crawler and a cache understand; in an open session they get one frame, the destination's, with its address
  and its title.
- **After an `await`** it is the same for as long as the page is still being waited for: a first request
  (`Rask:Server:QuiescenceTimeout`) is answered with the `302`, and a navigation in an open session
  (`NavigationQuiescenceTimeout`, [above](#the-page-is-sent-when-its-first-reads-are-back)) is one frame, the
  destination's, under the address and the history step of the navigation that led there. Only a load that
  outlasts the wait had its placeholder on screen; the destination then takes its place and its address.
- **Back never returns to the page that redirected.** It takes no place in the history — the destination
  replaces it — so Back is the page the reader was on before.
- **The destination is guarded.** Its `[Authorize]` runs before it is built, exactly as for a link.
- **A page the reader has already left sends them nowhere**: a `Go()` from a hook of an unmounted component is
  dropped.
- **Ten in a row is the limit.** The eleventh page to redirect gets `InvalidOperationException: Too many
  redirects`, shown by the page's error boundary — two pages that send the reader to each other end there
  rather than for ever. Give one of them a condition under which it shows itself.

### Leaving the app — `Go.Out`

An app mapped under a path base (`app.MapRask<App>(pathBase: "/new")`) owns the addresses under it, and
`Go.To("/tenants")` means `/new/tenants`. To send the reader to a page of the same site that the app does
**not** render — the old application it runs beside — say so:

```csharp
protected override Task OnMount()
{
    if (!_partner.IsChosen)
    {
        Go.Out("/tenants");                    // the old application's page, not /new/tenants
    }

    return Task.CompletedTask;
}
```

The address is used as written, and the browser loads it as a page: a `302` on a first request, a full-page
navigation from a handler or a lifecycle hook (before or after an `await`, as
[above](#redirecting-from-a-lifecycle-hook)), on the Server host and in WebAssembly. The app sent the reader
there, as a handler's `Go.To` does, so a form with [`ConfirmLeave`](forms.md) is not asked about.

**Only a path on this site.** `Go.Out` takes what the app wrote: one `/` and then the path. Another site, a
scheme (`https:`, `javascript:`), `//host`, `/\host` and anything with a control character throw
`ArgumentException` — the rule of `LocalUrl.Sanitize`, applied where the address is given and again where it
is sent. Never hand it a value that arrived from outside (a `returnUrl` from the query string) without
passing it through `LocalUrl.Sanitize` first. A plain link out needs none of this: a string `Href` is a
plain `<a>` and already leaves.

**Route-level authorization.** Put `[Authorize]` (optionally `[Authorize(Roles = "admin")]`) or `[AllowAnonymous]` on
a page component; the `RouteAuthorizationGuard` enforces it before the page renders. The session is a cookie and
`Rask.Auth` owns that scheme; roles and policies are ASP.NET's own `AddAuthorization`. Full flows on Server and
WASM are in [authentication.md](authentication.md).
