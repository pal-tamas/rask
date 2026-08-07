# Rask diagnostics (RASK001–RASK035)

Every Rask diagnostic, what triggers it, and how to fix it. Errors block the build; warnings don't
but flag a real problem; the hidden ones are informational, surfaced only as an IDE suggestion.

These come from the Rask source generator and analyzers (`Rask.Generators`). The generated
factories don't exist until a build runs, so if an ID below isn't recognised by your IDE yet,
build once.

Some diagnostics ship an **IDE quick-fix** (the lightbulb / `Ctrl`+`.`):

| ID | What the lightbulb does |
|----|-------------------------|
| **RASK001** | adds the `required` modifier |
| **RASK014** | rewrites `new Widget()` into the generated `Widget()` factory call |
| **RASK023** | inserts `Alt: ""` |
| **RASK026** | deletes the redundant `StateHasChanged()` statement |
| **RASK027** | removes the `OnXAsync` argument, keeping the sync one |

These are delivered by `Rask.Generators.CodeFixes`, packed alongside the analyzers in the
`Rask.Server` / `Rask.Wasm` packages — no extra reference needed.

A fix is offered only when the rewrite means exactly what you wrote. **RASK014's is withheld when the
construction has arguments or an object initializer**: the factory's parameters are generated from the
component's public properties in an order that is not the constructor's, so moving positional arguments
across would compile and mean something else — and an object initializer is only legal after `new`. In
those cases the error stands with its message, which already names the factory to call.

Every RASK diagnostic reports under the single category **`Rask`**, so one `.editorconfig` line covers
the family:

```ini
dotnet_analyzer_diagnostic.category-Rask.severity = warning
```

| ID | Severity | Summary |
|----|----------|---------|
| [RASK001](#rask001) | Hidden | Property is treated as a required factory parameter |
| [RASK002](#rask002) | Warning | `required` property cannot be honored by the generated factory |
| [RASK003](#rask003) | Error | Malformed route template |
| [RASK004](#rask004) | Error | Route segment has no matching property |
| [RASK005](#rask005) | Error | Property type does not match the route constraint |
| [RASK006](#rask006) | Error | `[QueryParam]` applied to a path-segment property |
| [RASK007](#rask007) | Error | `[ParentRoute]` cycle |
| [RASK008](#rask008) | Error | `[RouteParam]` without a matching path segment |
| [RASK009](#rask009) | Error | `[RouteParam]` on a non-routed class |
| [RASK010](#rask010) | Error | `[QueryParam]` on a non-routed class |
| [RASK011](#rask011) | Error | Route/query param type must implement `IParsable<T>` |
| [RASK012](#rask012) | Error | Multiple `[NotFound]` components |
| [RASK013](#rask013) | Error | `[NotFound]` cannot be combined with `[Route]` |
| [RASK014](#rask014) | Error | Components must be created via factory methods |
| [RASK015](#rask015) | Error | Orphan scoped-CSS file |
| [RASK016](#rask016) | Error | Ambiguous scoped-CSS match |
| [RASK017](#rask017) | Error | Orphan scoped-JS file |
| [RASK018](#rask018) | Error | Ambiguous scoped-JS match |
| [RASK019](#rask019) | Error | `<head>` is a framework-managed slot |
| [RASK020](#rask020) | Warning | Scoped-JS simple-name collision |
| [RASK021](#rask021) | Warning | Root component must render a complete page shell |
| [RASK022](#rask022) | Warning | List item is missing a `Key` |
| [RASK023](#rask023) | Warning | `Img` is missing `Alt` text |
| [RASK024](#rask024) | Warning | `UseAuthentication()` must precede `UseRask()` |
| [RASK025](#rask025) | Warning | `InputType` conflicts with the bound `Input<T>` value type |
| [RASK026](#rask026) | Warning | Redundant `StateHasChanged` in a Rask callback |
| [RASK027](#rask027) | Error | Both the sync and async handler are set for one event |
| [RASK028](#rask028) | Error | Ambiguous request handler (more than one handler for a query/command) |
| [RASK029](#rask029) | Warning | Handler cannot be registered (open generic, no public constructor, or unnameable) |
| [RASK030](#rask030) | Hidden | Prefer named arguments on a factory call with 3+ positional args |
| [RASK031](#rask031) | Warning | Two pages resolve to the same route |
| [RASK032](#rask032) | Error | Native component nested in the HTML tree |
| [RASK033](#rask033) | Warning | Hardcoded path for internal navigation instead of the generated route URL |
| [RASK034](#rask034) | Warning | BsDataGrid column has no Field, so the column chooser can't show/hide or reorder it |
| [RASK035](#rask035) | Warning | Background job or outbox event type cannot be registered |

---

## RASK001
**Property is treated as a required factory parameter** · Hidden

A non-nullable reference-type property with no initializer becomes a **required** parameter on the
generated factory. This is informational: the generator already enforces it positionally, but the
property isn't marked `required` at the language level.

```csharp
public sealed class Badge : Component
{
    public string Label { get; set; }   // RASK001 suggestion: add `required`
}
```

**Fix (optional):** add `required` for language-level enforcement (**quick-fix available** — the IDE
lightbulb inserts it), or make the property nullable
(`string? Label`) if it should be optional. HTML-attribute props are intentionally declared nullable
to stay ergonomic. See [factory generation rules](getting-started.md).

## RASK002
**`required` property cannot be honored by the generated factory** · Warning

A property is marked `required`, but the generated factory can't set it. This fires in exactly one
shape: the component has **both** a dependency-injected constructor **and** a parameterless
constructor, **and** the `required` property carries a member initializer. The factory then builds
the component with `new C() { … }`, but an initializer-carrying property is excluded from the factory
parameters, so the object initializer never assigns it and the consumer build fails with `CS9035`.

> A DI constructor with **no** parameterless constructor is fine: the factory builds the component
> with `ActivatorUtilities.CreateInstance` (which runs your DI constructor, so injected services are
> set) and then post-assigns each factory param — so a `required` property with no member initializer
> is honored. RASK002 does **not** fire in that case.

**Fix:** remove the member initializer so the `required` property becomes a plain factory parameter,
**or** remove `required`. Framework services (`RouteState`, `Navigator`, `HttpClient`, `IJSRuntime`)
should come through the constructor, never as settable properties.

## RASK003
**Malformed route template** · Error

A `[Route("...")]` template can't be parsed (unbalanced braces, empty segment, illegal constraint).

**Fix:** correct the template, e.g. `[Route("/users/{id:int}")]`.

## RASK004
**Route segment has no matching property** · Error

A `{segment}` in the route has no public settable property to bind to.

**Fix:** add a matching property and annotate it: `[RouteParam] public int Id { get; set; }`.

## RASK005
**Property type does not match the route constraint** · Error

The route constraint (e.g. `{id:int}`) is incompatible with the bound property's CLR type.

**Fix:** align the types — `{id:int}` ↔ `int Id`, `{slug}` ↔ `string Slug`.

## RASK006
**`[QueryParam]` applied to a path-segment property** · Error

A property is bound by a `{path}` segment **and** marked `[QueryParam]`. A value can't come from both.

**Fix:** use `[RouteParam]` for path segments; reserve `[QueryParam]` for query-string values.

## RASK007
**`[ParentRoute]` cycle** · Error

`[ParentRoute(typeof(...))]` links form a cycle, so no root can be established.

**Fix:** break the cycle so the parent chain terminates at a top-level route.

## RASK008
**`[RouteParam]` without a matching path segment** · Error

A property is `[RouteParam]` but no `{segment}` in the template (or an ancestor's, via
`[ParentRoute]`) matches its name.

**Fix:** add the segment to the template, or rename the property/segment to match.

## RASK009
**`[RouteParam]` on a non-routed class** · Error

`[RouteParam]` sits on a class that isn't a valid route target (no `[Route]`, not reachable via
`[ParentRoute]`).

**Fix:** add `[Route]` to the class, or remove the attribute.

## RASK010
**`[QueryParam]` on a non-routed class** · Error

As RASK009, for `[QueryParam]`. Query binding only applies to routed pages.

**Fix:** add `[Route]`, or remove `[QueryParam]`.

## RASK011
**Route/query param type must implement `IParsable<T>`** · Error

A `[RouteParam]`/`[QueryParam]` property's type can't be parsed from a URL string. Bound types must
be `string` or implement `System.IParsable<T>` (every built-in numeric, `Guid`, `DateTime`, `bool`,
enums via custom parsing, etc. qualify).

**Fix:** use a parsable type, or accept the value as `string` and convert inside the page.

## RASK012
**Multiple `[NotFound]` components** · Error

More than one `[NotFound]` catch-all page exists in the assembly; only one is allowed.

**Fix:** keep a single `[NotFound]` page and remove the duplicate.

## RASK013
**`[NotFound]` cannot be combined with `[Route]`** · Error

A class carries both `[NotFound]` and `[Route]`. `[NotFound]` is the catch-all and matches no
specific path.

**Fix:** remove `[Route]` from the not-found page.

## RASK014
**Components must be created via factory methods** · Error

`new SomeComponent()` was used outside `Rask.Core`. Components are constructed through the generated
factories so keys, children, and DI wiring are handled consistently.

```csharp
// ✗ new Div()
// ✓ Div(Class: "card")[ ... ]
```

**Fix:** call the generated factory (`Div(...)`, `Counter(...)`). In test files that deliberately
construct components directly, opt out per file with `#pragma warning disable RASK014`.

## RASK015
**Orphan scoped-CSS file** · Error

A `{Name}.css` sibling has no matching `Component` subclass named `{Name}` in the same folder, so it
can't be scoped to anything.

**Fix:** rename the file to match its component, move it next to the right component, or exclude it
with `<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>` if it's a global stylesheet.
`bin/`, `obj/`, `node_modules/` and `wwwroot/` are already excluded, so a global stylesheet under
`wwwroot/` — the placement [js-interop.md](js-interop.md#scoped-css) recommends — never trips this.

## RASK016
**Ambiguous scoped-CSS match** · Error

A `{Name}.css` file matches more than one component class named `{Name}` (e.g. two `Card` types in
different namespaces but the same folder).

**Fix:** disambiguate by moving one component/file so each `.css` has exactly one match.

## RASK017
**Orphan scoped-JS file** · Error

As RASK015, for a `{Name}.js` sibling with no matching component.

**Fix:** rename/move the file, or opt the file out of auto-inclusion.

## RASK018
**Ambiguous scoped-JS match** · Error

As RASK016, for `{Name}.js` matching multiple component classes.

**Fix:** disambiguate so each `.js` file maps to one component.

## RASK019
**`<head>` is a framework-managed slot** · Error

Children were passed to `Head()`. Rask collects, dedupes, and splices head content itself, so the
`<head>` element doesn't take children.

**Fix:** override `protected override Component? Head => ...` on any component and return your
`Title`/`Meta`/`Link`/`Script` — a single tag, or a collection expression like
`Head => [Title(...)["..."], Meta(...)]`. See [the head guide](getting-started.md).

## RASK020
**Scoped-JS simple-name collision** · Warning

Two or more components with scoped JS share the same simple type name. The browser-side namespace
key `window.Rask["Name"]` is shared, and the last registration silently wins.

**Fix:** rename one component, move it to a differently-named sibling, or expose its exports under a
sub-namespace inside the JS file. Promote to an error with
`<WarningsAsErrors>RASK020</WarningsAsErrors>`.

## RASK021
**Root component must render a complete page shell** · Warning

The root `TApp` doesn't render a full shell. A root `Render()` must produce
`Doctype()`, `Html(...)[ Head(), Body()[ ... ] ]`. A runtime backstop (`ValidateRootShell`) also
enforces this, so an incomplete shell that slips past the analyzer still fails at render.

**Fix:** make the root render the complete shell — typically `[ Doctype(), Html(...)[...] ]`.
Do **not** add a runtime `<script>`; it's auto-appended to `<body>`.

## RASK022
**List item is missing a `Key`** · Warning

A Rask factory call appears in a sibling-list context (a `.Select`/`.SelectMany` projection, or
`.Add` in a loop) without a `Key:`. Keyless items reconcile **by position**, which loses focus and
input state and emits untrusted structural diffs on insert/remove/move.

```csharp
// ✗ items.Select(i => Li()[ i.Name ])
// ✓ items.Select(i => Li(Key: i.Id)[ i.Name ])
```

**Fix:** pass a stable `Key:` (an entity id, not the loop index). See
[keyed lists](getting-started.md) and the [live-rendering architecture](architecture/live-rendering.md)
for why identity beats position.

## RASK023
**`Img` is missing `Alt` text** · Warning

An `Img(...)` factory call supplies no `Alt`. Without a text alternative, screen readers fall back to
announcing the file name (or nothing), failing [WCAG 1.1.1](https://www.w3.org/WAI/WCAG21/Understanding/non-text-content).

```csharp
// ✗ Img(Src: "/logo.png")
// ✓ Img(Src: "/logo.png", Alt: "Rask logo")
// ✓ Img(Src: "/divider.png", Alt: "")   // decorative: empty alt hides it from assistive tech
```

**Fix:** pass a meaningful `Alt:`, or the empty string `Alt: ""` for a purely decorative image so
assistive technology skips it (**quick-fix available** — the IDE lightbulb inserts `Alt: ""`, which you
then fill in for informative images). See [accessibility](accessibility.md).

## RASK024
**`UseAuthentication()` must precede `UseRask()`** · Warning

`app.UseRask<App>()` is wired before `app.UseAuthentication()`. Rask seeds the live session from
`HttpContext.User` during the initial GET render and the WebSocket upgrade — if the authentication
middleware runs *after* `UseRask`, the principal is empty at that point and every `[Authorize]` page
challenges.

```csharp
// ✗ app.UseRask<App>();
//   app.UseAuthentication();
// ✓ app.UseAuthentication();   // populates HttpContext.User on GET + WS upgrade
//   app.UseAuthorization();
//   app.UseRask<App>();
```

**Fix:** call `app.UseAuthentication()` (and `app.UseAuthorization()`) before `app.UseRask()`. The
warning fires only when both calls are present and `UseAuthentication` is positioned after `UseRask`;
an app with no authentication middleware is left alone. See [authentication](authentication.md).

## RASK025
**`InputType` conflicts with the bound `Input<T>` value type** · Warning

A generic `Input<T>` derives its HTML input `type` from `T` (`bool`→checkbox, `int`/`decimal`→number,
`DateOnly`→date, …). The *string-only* `InputType`s — `Text`, `Search`, `Tel`, `Url`, `Email`,
`Password` — only apply to `Input<string>`; pairing one with `Input<int>`/`Input<bool>`/… is a mistake
(the entered value could never round-trip to `T`).

```csharp
// ✗ Age is int — a number input can't be an email field:
Input(() => model.Age, Type: InputType.Email)
// ✓ let the type derive from T (int → number):
Input(() => model.Age)
// ✓ or use a string-only type on a string field:
Input(() => model.Email, Type: InputType.Email)
```

**Fix:** drop the explicit `Type` (it's inferred from `T`), or bind a `string`. The warning fires only
for a statically-known string-family `InputType` on a non-`string` `Input<T>`; `Input<int>(Type:
InputType.Number)` and any `Input<string>` are left alone. Suppressible like any analyzer.

## RASK026
**Redundant `StateHasChanged` in a Rask callback** · Warning

Rask re-renders the component that *owns* an event/binding callback automatically after the callback
runs — including when a child control raised it (the framework re-renders the delegate's owner, captured
from the lambda's `this`) and after a two-way bound write (the binding re-renders its authoring
component). So calling your own `StateHasChanged()` from inside `OnChange`/`OnClick`/`OnInput`/`OnSubmit`/…
or the `AfterBind`/`AfterBindAsync` hooks is dead weight. The tell-tale anti-pattern is reaching for
`AfterBind: _ => StateHasChanged()` to make derived UI refresh.

```csharp
// ✗ redundant — the framework re-renders this component after OnChange runs:
Select<string>(Value: _pick, OnChange: v => { _pick = v; StateHasChanged(); })
// ✓ just update state; the render is automatic:
Select<string>(Value: _pick, OnChange: v => _pick = v)

// ✗ AfterBind only to force a re-render of a sibling readout:
RadioGroup(() => model.Plan, options, AfterBind: _ => StateHasChanged())
// ✓ a two-way write already re-renders the binding's owner — derived UI updates on its own:
RadioGroup(() => model.Plan, options)
```

**Fix:** remove the `StateHasChanged()` call. If derived UI still isn't updating, the problem is the
*owner* of the callback or binding (write the lambda where it captures the right `this`, or bind the
model the consumer reads), not the render count. The warning fires only for a self-call
(`StateHasChanged()` / `this.StateHasChanged()`) lexically inside a Rask callback; a `StateHasChanged` in
a lifecycle hook, async loop, or event subscription (`feed.Updated += StateHasChanged`), or a call on a
*different* component, is left alone. Suppressible like any analyzer.

## RASK027
**Both the sync and async handler are set for one event** · Error

Every DOM event on a component maps to a single handler slot. The typed `OnX` (sync) and `OnXAsync`
(async) properties are two views over that one slot, so wiring **both** for the same event — e.g.
`Button(OnClick: ..., OnClickAsync: ...)` — is a mistake: the runtime keeps the sync handler and silently
ignores the async one, which is rarely what the author intended. Set exactly one handler per event.

```csharp
// ✗ both set — OnClickAsync is silently dropped at runtime:
Button(OnClick: () => Toggle(), OnClickAsync: async () => await SaveAsync())["Save"]
// ✓ pick one — the async handler, since it awaits:
Button(OnClickAsync: async () => await SaveAsync())["Save"]
// ✓ passing null for the sibling is allowed (a deliberate "at most one" conditional):
Button(OnClick: useAsync ? null : Sync, OnClickAsync: useAsync ? Async : null)["Save"]
```

**Fix:** remove one of the two handlers (keep the async `OnXAsync` if it awaits, else the sync `OnX`).
The error fires only when both siblings are passed as non-`null` arguments to the same factory call;
passing `null` for one (a conditional "set at most one") is left alone. Applies to every paired event,
including form callbacks (`OnInput`/`OnInputAsync`, `OnChange`/`OnChangeAsync`, …). Suppressible like any
analyzer.

## RASK028
**Ambiguous request handler** · Error

A query (`IQuery<T>`) or command (`ICommand` / `ICommand<T>`) must have **exactly one** handler — the
[`Rask.Cqrs`](cqrs.md) dispatcher maps each request type to a single handler, so two handlers for the
same request would make dispatch non-deterministic. The generator reports this on each competing
handler.

```csharp
public sealed record GetValue : IQuery<int>;
public sealed class HandlerOne : IQueryHandler<GetValue, int> { /* ... */ } // ✗ RASK028
public sealed class HandlerTwo : IQueryHandler<GetValue, int> { /* ... */ } // ✗ RASK028
```

**Fix:** keep a single handler for the request type (merge the logic, or split into two distinct
request types). Notifications are exempt — an `INotification` may have any number of
`INotificationHandler`s.

## RASK029
**Handler cannot be registered** · Warning

A discovered handler can't be registered in DI, so it is skipped and dispatching its request would throw
at runtime. The causes are an **open generic** handler (its type parameters can't be closed at
registration time), a handler with **no public constructor** (the container can't build it), and a handler
the generated registry can't **name** — one declared `file`-local, or `private`/`protected` at any level of
its containing chain. That last group used to emit code that didn't compile (CS0234 / CS0122) instead of
being skipped.

```csharp
public sealed record GetValue : IQuery<int>;
public sealed class PrivateHandler : IQueryHandler<GetValue, int>
{
    private PrivateHandler() { }                                          // ✗ RASK029: no public ctor
    public Task<int> Handle(GetValue query, CancellationToken ct) => Task.FromResult(1);
}
```

**Fix:** give the handler a public constructor, make it a closed (non-generic) type, or raise its
accessibility to at least `internal` and move it out of a `file`-local declaration. A request with
*no* handler at all is not flagged (the handler may live in another assembly) — it throws a clear
`InvalidOperationException` when dispatched.

## RASK030
**Prefer named arguments on Rask factories** · Hidden

A Rask factory call passes **three or more leading positional arguments**. Beyond one or two, positional
calls both read poorly and are fragile: Rask orders generated factory parameters by inheritance depth,
then by file ordinal + span, so a later edit — adding a property to a base class, renaming a partial
file — can reorder parameters and silently rebind such a call. The first one or two positional arguments
(the primary content — `A(href)`, `Div(id, class)`) are left alone as idiomatic.

```csharp
// ✗ Div("main", "container", "color:red")                 // three positional — order-fragile, hard to read
// ✓ Div(Id: "main", Class: "container", Style: "color:red") // explicit, refactor-proof
```

**Fix:** name the arguments (`Prop: value`). Hidden by default (no build output, no effect on the
warnings-as-errors build) — the IDE surfaces it as a suggestion. Suppress per call with
`#pragma warning disable RASK030`, or globally in `.editorconfig`
(`dotnet_diagnostic.RASK030.severity = none`) if you prefer a positional style.

## RASK031
**Two pages resolve to the same route** · Warning

Two different top-level pages resolve to the same route, so both match the same URL and which one renders
is arbitrary. Templates are compared the way the **runtime router** matches them, not by raw string — so
these all collide: `/Products` ↔ `/products` (literals match case-insensitively), `/products` ↔
`products/` (surrounding slashes trimmed), and `/item/{id:int}` ↔ `/item/{id:guid}` ↔ `/item/{slug}`
(the router ignores parameter names and `:constraints`). The check covers pages **without** a
`[ParentRoute]` (whose template is the full path); parent-composed paths are not resolved here, so
nested-route collisions are not flagged (the check under-reports rather than risk a false positive).

```csharp
[Route("/products")] public sealed class ProductList : Component { }   // first — canonical
[Route("/Products")] public sealed class ProductGrid : Component { }   // ✗ RASK031: same URL as ProductList
```

A warning, not an error — a collision is a real bug, but the app still runs (it just picks arbitrarily),
so upgrading Rask never hard-breaks a build that compiled before.

**Fix:** give one page a distinct route, or merge the two. Reported on every colliding page after the
first (ordered by fully-qualified name), naming the page it collides with.

## RASK032
**Native component nested in the HTML tree** · Error

A native chrome component — a `Rask.Native.Components.NativeComponent` subclass such as `NativeHeaderBar`,
`NativeTabBar`, `NativeToolbar`, or `NativeBarButton` — describes a real platform bar for the `Rask.Native`
mobile host, not HTML. Bars are composed at the native page's **layout level**, as siblings of a
`NativeWebView` (which hosts the HTML). Nested inside the HTML — as an element child, or inside
`NativeWebView`'s content — a bar would serialize to nothing, so the mistake is caught at compile time. The
analyzer flags a native component passed to any element-children indexer.

```csharp
// ✗ RASK032 — native chrome as an element child
protected override Component? Render() => Div()[NativeHeaderBar(Title: "Home")];

// ✗ RASK032 — native chrome inside the NativeWebView's HTML content
protected override Component? Render() => NativeWebView()[NativeHeaderBar(Title: "Home")];

// ✓ correct — bars are siblings of NativeWebView at the layout level
protected override Component? Render() =>
[
    NativeHeaderBar(Title: "Home"),
    NativeWebView()[Doctype(), Html("en")[Head(), Body()[Router()]]],
    NativeTabBar(Tabs: [...], Selected: 0)
];
```

**Fix:** move the native component out of the HTML — compose it at the layout level, as a sibling of
`NativeWebView`. Native chrome renders only under the native host and is inert on Server/WASM.

## RASK033
**Hardcoded path for internal navigation instead of the generated route URL** · Warning

Rask generates a type-safe `RouteUrl` factory — `Routes.<Page>()` — for every page's **primary** `[Route]`
(see [Routing → type-safe URLs](routing.md)). Using the raw path string for internal navigation bypasses
that safety: rename or remove the `[Route]` and the string becomes a silent dead link that still compiles,
whereas `Routes.<Page>()` becomes a compile error you fix immediately. The analyzer flags a string literal
passed to internal navigation — `Navigator.NavigateTo("…")` or any `RouteUrl` slot (`NavLink(Href: …)`,
`BsNavItem(Href: …)`, `NativeTab(To: …)`, via the `string → RouteUrl` implicit conversion) — **only** when
the path maps to a generated parameterless factory.

It deliberately leaves alone:
- **External URLs** — `https://…`, or anything wrapped in `RouteUrl.External("…")`.
- **Parameterised routes** — `/users/42` needs `Routes.UserPage("42")`, which can't be reconstructed from a
  bare literal.
- **Secondary `[Route]` templates** — the factory formats a page's *first* template only, so a literal like
  `/todos/new` on a page whose primary route is `todos` has no `Routes.*()` equivalent and is not flagged.

```csharp
[Route("todos")] public sealed class TodosPage : Component { /* … */ }

nav.NavigateTo("/todos");            // ✗ RASK033 — use Routes.TodosPage()
NavLink(Href: "/todos")["Todos"];    // ✗ RASK033 — string → RouteUrl conversion

nav.NavigateTo(Routes.TodosPage());  // ✓ type-safe; a renamed route is a compile error
nav.NavigateTo("/todos/new");        // ✓ secondary template — no factory, left alone
A("https://example.com", "_blank")["Docs"]; // ✓ external — untouched
```

**Fix:** call the generated `Routes.<Page>()` (with arguments for any route/query params). For a genuinely
dynamic or external target, use `RouteUrl.External("…")`, or suppress with `#pragma warning disable RASK033`
/ `.editorconfig` (`dotnet_diagnostic.RASK033.severity = none`).

## RASK034
**BsDataGrid column has no Field, so the column chooser can't show/hide or reorder it** · Warning

A [`BsDataGrid`](data-grid.md) that turns on the column chooser or reordering — `ColumnChooser`, or the
controlled `HiddenColumns`/`ColumnOrder` (and their callbacks) — addresses each column by the token read off
its [`Field`](data-grid-advanced.md#field-names-the-column) expression (`Field = r => r.Region` → `"region"`). A
column with **no `Field`** has no token, so it can never be hidden or reordered: it stays pinned in the table
with no menu row, silently. The analyzer flags any `BsColumn` in an **inline** `Columns:` list that sets no
`Field` while the chooser is in use.

It leaves alone:
- A grid that uses **neither** the chooser nor a controlled `HiddenColumns`/`ColumnOrder` — a missing `Field`
  costs nothing there.
- A column that is a **deliberate fixture** — `Hideable = false` *and* `Reorderable = false` — since it opts
  out of both axes and needs no token.
- A `Columns:` passed as a **variable** rather than an inline collection expression (its contents are out of
  reach of the call-site check).

```csharp
BsDataGrid(Data: deals, ColumnChooser: true, Columns:
[
    new BsColumn<Deal> { Title = "Region", Value = d => d.Region },              // ✗ RASK034 — no token
    new BsColumn<Deal> { Title = "Amount", Field = d => d.Amount },              // ✓ named "amount"
    new BsColumn<Deal> { Title = "", Template = d => Actions(d),
        Hideable = false, Reorderable = false },                                 // ✓ pinned fixture
]);
```

**Fix:** add `Field = r => r.X` to name the column (the same token feeds grouping and a controlled sort, so
name it once), or mark it a fixture with `Hideable = false` and `Reorderable = false`. Suppress with
`#pragma warning disable RASK034` / `.editorconfig` (`dotnet_diagnostic.RASK034.severity = none`).

## RASK035
**Background job or outbox event type cannot be registered** · Warning

A type implementing `IJob` or `IOutboxEvent` was found, but the generated registry can't map its stored
name to its CLR type — so it is skipped. Enqueuing it still writes a row; the processor then fails to
rehydrate it, records `No registered job type '…'`, and retries until `MaxAttempts` before dead-lettering.
Before this diagnostic existed the type was skipped **silently**, which is exactly what made the failure
hard to place: the job looked queued, then quietly stopped.

The reasons, all about reconstructing a runtime `Type.FullName` from a name the generated file can write:

| Shape | Why |
|-------|-----|
| **Generic** — `record Reindex<T> : IJob` | A closed generic's `FullName` carries assembly-qualified type arguments, so no static key matches. |
| **Nested in a generic** — `class Outer<T> { record Ev : IOutboxEvent; }` | Naming it would leak `T` into the generated file. |
| **`file`-local** — `file record Ev : IOutboxEvent;` | Invisible outside its own file, and its `FullName` carries a synthesized `<file>F0__` segment. |
| **Inaccessible** — `private`/`protected` at any level of its containing chain | The generated registry lives in the same assembly but a different file, so it can't name the type. |

An **abstract** base carrying the marker is skipped without a warning — modelling a hierarchy that way is
normal, and its concrete derivatives register as usual.

```csharp
public class Outer<T>
{
    public sealed record Raised(int Id) : IOutboxEvent;    // ✗ RASK035: nested inside the generic type 'Outer'
}

public static class OrderEvents
{
    public sealed record Raised(int Id) : IOutboxEvent;    // ✓ nested in a non-generic type is fine
}
```

**Fix:** move the type out of the generic (or `file`-local, or inaccessible) declaration, and make it
non-generic — nesting inside a plain `static class` is the usual way to keep events grouped. Suppress with
`#pragma warning disable RASK035` / `.editorconfig` (`dotnet_diagnostic.RASK035.severity = none`) only if
you never enqueue that type.
