# Rask diagnostics (RASK001–RASK056)

Every Rask diagnostic, what triggers it, and how to fix it. Errors block the build; warnings don't
but flag a real problem; the hidden ones are informational, surfaced only as an IDE suggestion.

These come from the Rask source generator and analyzers (`Rask.Generators`). The generated chain
surface doesn't exist until a build runs, so if an ID below isn't recognised by your IDE yet,
build once.

Some diagnostics ship an **IDE quick-fix** (the lightbulb / `Ctrl`+`.`):

| ID | What the lightbulb does |
|----|-------------------------|
| **RASK001** | adds the `required` modifier |
| **RASK014** | rewrites `new Widget()` into the bare entry `Widget` |
| **RASK023** | appends `.Alt("")` to the chain (or `Alt: ""` on a factory call) |
| **RASK026** | deletes the redundant `StateHasChanged()` statement |
| **RASK027** | removes the `OnXAsync` argument, keeping the sync one |
| **CS0108** | adds `new` to a member that [hides a builder entry](#cs0108-a-member-hides-a-builder-entry) |

These are delivered by `Rask.Generators.CodeFixes`, packed alongside the analyzers in the
`Rask.Server` / `Rask.Wasm` packages — no extra reference needed.

A fix is offered only when the rewrite means exactly what you wrote. **RASK014's is withheld when the
construction has arguments or an object initializer**: a chain sets each property by name in its own
step, so moving positional constructor arguments across would compile and mean something else — and an
object initializer is only legal after `new`. In those cases the error stands with its message, which
already spells out the chain to write.

Every RASK diagnostic reports under the single category **`Rask`**, so one `.editorconfig` line covers
the family:

```ini
dotnet_analyzer_diagnostic.category-Rask.severity = warning
```

| ID | Severity | Summary |
|----|----------|---------|
| [RASK001](#rask001) | Hidden | Property is treated as a required factory parameter |
| [RASK002](#rask002) | Warning | `required` property cannot be honored by the chain |
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
| [RASK014](#rask014) | Error | Components must be built through a chain |
| [RASK015](#rask015) | Error | Orphan scoped-CSS file |
| [RASK016](#rask016) | Error | Ambiguous scoped-CSS match |
| [RASK017](#rask017) | Error | Orphan scoped-TS file |
| [RASK018](#rask018) | Error | Ambiguous scoped-TS match |
| [RASK019](#rask019) | Error | `<head>` is a framework-managed slot |
| [RASK020](#rask020) | Warning | Scoped-TS simple-name collision |
| [RASK021](#rask021) | Warning | Root component must not render the page shell |
| [RASK022](#rask022) | Warning | List item is missing a `Key` |
| [RASK023](#rask023) | Warning | `Img` is missing `Alt` text |
| [RASK024](#rask024) | Warning | `UseAuthentication()` must precede `UseRask()` |
| [RASK025](#rask025) | Warning | `InputType` conflicts with the bound `Input<T>` value type |
| [RASK026](#rask026) | Warning | Redundant `StateHasChanged` in a Rask callback |
| [RASK027](#rask027) | Error | Both the sync and async handler are set for one event |
| [RASK028](#rask028) | Error | Ambiguous request handler (more than one handler for a query/command) |
| [RASK029](#rask029) | Warning | Handler cannot be registered (open generic, no public constructor, or unnameable) |
| [RASK031](#rask031) | Warning | Two pages resolve to the same route |
| RASK032 | — | *Retired* — native chrome cannot sit inside an HTML tree |
| [RASK033](#rask033) | Warning | Hardcoded path for internal navigation instead of the generated route URL |
| [RASK034](#rask034) | Warning | BsDataGrid column has no Field, so the column chooser can't show/hide or reorder it |
| [RASK035](#rask035) | Warning | Background job or outbox event type cannot be registered |
| [RASK036](#rask036) | Warning | A builder-entry host must be `partial` |
| [RASK037](#rask037) | Warning | `using` alias is hidden by a builder entry |
| [RASK038](#rask038) | Error | Builder chain does not set a required property |
| [RASK039](#rask039) | Warning | Builder chain is split across statements, so its required properties can't be checked |
| [RASK040](#rask040) | Warning | Two components share a simple name, so neither can have a builder entry |
| [RASK041](#rask041) | Warning | The builder surface's shared pending-bit budget is exhausted |
| [RASK042](#rask042) | — | *Retired* — delegate-typed property cannot receive a builder setter |
| [RASK043](#rask043) | Warning | A component name is used in a type that has no builder entries |
| [RASK044](#rask044) | Warning | Builder chain sets the same property twice |
| [RASK045](#rask045) | Warning | Component built by a chain is assigned to afterwards |
| [RASK046](#rask046) | Warning | Key must open a component's chain |
| RASK047 | — | *retired* — routes are `[Route]` attribute arguments, constant by construction |
| RASK048 | — | *Retired* — HTML cannot sit inside a native screen |
| RASK049 | — | *Retired* — a `NativeWebView` set a `Url` and took children |
| RASK050 | — | *Retired* — a native head was named on a web-only host |
| [RASK051](#rask051) | Error | Translation catalog is malformed |
| [RASK052](#rask052) | Warning | Translation catalog disagrees with the neutral catalog |
| [RASK053](#rask053) | Error | Remote message has no wire encoding |
| [RASK054](#rask054) | Info | Page cannot run in the browser |
| [RASK055](#rask055) | Error | Scoped JavaScript is no longer supported |
| [RASK056](#rask056) | Warning | `AddRask` is called twice on the same service collection |

---

## RASK001
**Property is treated as a required chain step** · Hidden

A non-nullable reference-type property with no initializer becomes a **required step** — one the chain
must take before it produces a component at all. This is informational: the generator already enforces
it through the chain's type, but the property isn't marked `required` at the language level.

```csharp
public sealed partial class Badge : Component
{
    public string Label { get; set; }   // RASK001 suggestion: add `required`
}
```

**Fix (optional):** add `required` for language-level enforcement (**quick-fix available** — the IDE
lightbulb inserts it), or make the property nullable
(`string? Label`) if it should be optional. HTML-attribute props are intentionally declared nullable
to stay ergonomic. See [what becomes a step](getting-started.md#6-why-homepage-already-chains-the-generated-surface).

## RASK002
**`required` property cannot be honored by the chain** · Warning

A property is marked `required`, but the chain can't set it. This fires in exactly one
shape: the component has **both** a dependency-injected constructor **and** a parameterless
constructor, **and** the `required` property carries a member initializer. The entry then builds
the component with `new C()`, but an initializer-carrying property is not one the steps can
set, so nothing ever assigns it and the consumer build fails with `CS9035`.

> A DI constructor with **no** parameterless constructor is fine: the entry builds the component
> with `ActivatorUtilities.CreateInstance` (which runs your DI constructor, so injected services are
> set) and the steps assign afterwards — so a `required` property with no member initializer
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
**Components must be built through a chain** · Error

`new SomeComponent()` was used outside `Rask.Core`. Components are built by naming them and chaining
onto them, which is what routes the first step through `GetOrCreate` — the identity the runtime
reconciles across renders — and what wires keys, children, and DI consistently. `new` skips all of it,
and can also produce a component whose required properties were never set.

```csharp
// ✗ new Div
// ✓ Div.Class("card")[ ... ]
```

**Fix:** name it and chain onto it — `Counter.Start(3)`, or `Counter` alone when it needs nothing. In
test files that deliberately construct components directly, opt out per file with
`#pragma warning disable RASK014`.

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
**Orphan scoped-TS file** · Error

As RASK015, for a `{Name}.ts` sibling with no matching component.

**Fix:** rename/move the file, or opt the file out of auto-inclusion with
`<RaskScopedTsAutoInclude>false</RaskScopedTsAutoInclude>`.

## RASK018
**Ambiguous scoped-TS match** · Error

As RASK016, for `{Name}.ts` matching multiple component classes.

**Fix:** disambiguate so each `.ts` file maps to one component.

## RASK019
**`<head>` is a framework-managed slot** · Error

Children were passed to `Head()`. Rask collects, dedupes, and splices head content itself, so the
`<head>` element doesn't take children.

**Fix:** override `protected override Component? Head => ...` on any component and return your
`Title`/`Meta`/`Link`/`Script` — a single tag, or a collection expression like
`Head => [Title(...)["..."], Meta(...)]`. See [the head guide](getting-started.md).

## RASK020
**Scoped-TS simple-name collision** · Warning

Two or more components with scoped TypeScript share the same simple type name. The browser-side
namespace key `window.Rask["Name"]` is shared, and the last registration silently wins.

**Fix:** rename one component, move it to a differently-named sibling, or expose its exports under a
sub-namespace inside the TypeScript file. Promote to an error with
`<WarningsAsErrors>RASK020</WarningsAsErrors>`.

## RASK021
**Root component must not render the page shell** · Warning

The root `TApp` renders into `<body>` — Rask emits the doctype, `<html>`, `<head>` and `<body>` around
whatever it returns. A root that builds them itself nests a second document *inside* the body, which
the HTML parser silently unwraps: the page keeps rendering, and quietly loses the nested tags'
attributes. Nothing fails, so this warning is the only signal there is.

```csharp
// ✗ RASK021 — the root builds the document
protected override Component? Render() =>
    [Doctype, Html("en")[Head, Body[Router]]];

// ✓ the root renders the body's content
protected override Component? Render() => Router;
```

**Fix:** return the body's content (usually `Router()`) and move the shell's pieces to the overrides
that own them — `<head>` content to `Head`, `<html lang>` to `HtmlLang`, `<html dir>` to `HtmlDir`, `<body class>` to `BodyClass`,
and a genuinely custom document to `Shell(head, body)`, which receives the framework's `<head>` and the
rendered body as parameters. Do **not** add a runtime `<script>`; it's auto-appended to `<body>`.
`Doctype`/`Html`/`Head`/`Body` stay ordinary tag components for documents you build by hand
(`ToHtml()`, an email body) — they have just left the app-authoring path. See
[the document and the `Head` override](getting-started.md#7-the-document-and-the-head-override).

## RASK022
**List item is missing a `Key`** · Warning

A Rask component appears in a sibling-list context (a `.Select`/`.SelectMany` projection, or `.Add`
in a loop) without a `Key`. Keyless items reconcile **by position**, which loses focus and input state
and emits untrusted structural diffs on insert/remove/move — and for a component, position also
decides which instance is reused, so the row's own state follows the slot rather than the item
(see [RASK046](#rask046)).

Both chain spellings are recognised — `Li[…]` and `Li.Class("c")[…]`. The chain went unreported until
#704, when the only surface this checked was a factory call: the check matched a method named after the
component, and a chain has none.

```csharp
// ✗ items.Select(i => Li[ i.Name ])
// ✓ items.Select(i => Li.Key(i.Id)[ i.Name ])
```

**Fix:** pass a stable `.Key(…)` (an entity id, not the loop index). See
[keyed lists](getting-started.md) and the [live-rendering architecture](architecture/live-rendering.md)
for why identity beats position.

## RASK023
**`Img` is missing `Alt` text** · Warning

An `Img` is built without naming `Alt`. Without a text alternative, screen readers fall back to
announcing the file name (or nothing), failing [WCAG 1.1.1](https://www.w3.org/WAI/WCAG21/Understanding/non-text-content).

Both chain spellings are recognised — `Img.Src("/x")` and the bare `Img`. **The chain went unreported
until #704**, when the only surface this checked was a factory call: it matched a static method named
`Img`, and on a chain the outermost call is the `Src` setter — so an accessibility check that the docs'
own examples should have tripped never fired at all.

```csharp
// ✗ Img.Src("/logo.png")
// ✓ Img.Src("/logo.png").Alt("Rask logo")
// ✓ Img.Src("/divider.png").Alt("")   // decorative: empty alt hides it from assistive tech
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
Input.Bind(() => model.Age).Type(InputType.Email)
// ✓ let the type derive from T (int → number):
Input.Bind(() => model.Age)
// ✓ or use a string-only type on a string field:
Input.Bind(() => model.Email).Type(InputType.Email)
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
Select<string>().Value(_pick).OnChange(v => { _pick = v; StateHasChanged(); })
// ✓ just update state; the render is automatic:
Select<string>().Value(_pick).OnChange(v => _pick = v)

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
`Button.OnClick(...).OnClickAsync(...)` — is a mistake: the runtime keeps the sync handler and silently
ignores the async one, which is rarely what the author intended. Set exactly one handler per event.

```csharp
// ✗ both set — OnClickAsync is silently dropped at runtime:
Button.OnClick(() => Toggle()).OnClickAsync(async () => await SaveAsync())["Save"]
// ✓ pick one — the async handler, since it awaits:
Button.OnClickAsync(async () => await SaveAsync())["Save"]
// ✓ passing null for the sibling is allowed (a deliberate "at most one" conditional):
Button.OnClick(useAsync ? null : Sync).OnClickAsync(useAsync ? Async : null)["Save"]
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
**Retired.** It asked you to name the arguments of a factory call once three or more were positional,
because the generated parameter order could shift under an unrelated edit and silently rebind them.
A chain has no positional arguments — every step names its property — so there is nothing left to
misbind. The id is not reused.

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
[Route("/products")] public sealed partial class ProductList : Component { }   // first — canonical
[Route("/Products")] public sealed partial class ProductGrid : Component { }   // ✗ RASK031: same URL as ProductList
```

A warning, not an error — a collision is a real bug, but the app still runs (it just picks arbitrarily),
so upgrading Rask never hard-breaks a build that compiled before.

**Fix:** give one page a distinct route, or merge the two. Reported on every colliding page after the
first (ordered by fully-qualified name), naming the page it collides with.

## RASK033
**Hardcoded path for internal navigation instead of the generated route URL** · Warning

Rask generates a type-safe `RouteUrl` factory — `Routes.<Page>()` — for every page's **primary** `[Route]`
(see [Routing → type-safe URLs](routing.md)). Using the raw path string for internal navigation bypasses
that safety: rename or remove the `[Route]` and the string becomes a silent dead link that still compiles,
whereas `Routes.<Page>()` becomes a compile error you fix immediately. The analyzer flags a string literal
passed to internal navigation — `Navigator.NavigateTo("…")` or any `RouteUrl` slot (`NavLink(Href: …)`,
`BsNavItem.Href(…)`, via the `string → RouteUrl` implicit conversion) — **only** when
the path maps to a generated parameterless factory.

It deliberately leaves alone:
- **External URLs** — `https://…`, or anything wrapped in `RouteUrl.External("…")`.
- **Parameterised routes** — `/users/42` needs `Routes.UserPage("42")`, which can't be reconstructed from a
  bare literal.
- **Secondary `[Route]` templates** — the factory formats a page's *first* template only, so a literal like
  `/todos/new` on a page whose primary route is `todos` has no `Routes.*()` equivalent and is not flagged.

```csharp
[Route("todos")] public sealed partial class TodosPage : Component { /* … */ }

nav.NavigateTo("/todos");            // ✗ RASK033 — use Routes.TodosPage()
NavLink.Href("/todos")["Todos"];    // ✗ RASK033 — string → RouteUrl conversion

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
BsDataGrid.Data(deals).ColumnChooser(true).Columns([
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

## RASK036
**A builder-entry host must be `partial`** · Warning

Rask's own components (`Div`, `BsCard`, …) get their entries from `Rask.Core.RaskMarkup`, which
`Component` derives from — so every component inherits them, and so does anything else that derives
from `RaskMarkup` (a test class, a fixture, a factory of demo components). **Your** components cannot
ride there: a source generator can only add members to types in the compilation it is running in, and
`RaskMarkup` lives in a referenced assembly. `using static` is not a way out either — a static-imported
member loses to a same-named type in scope (CS0119), which is the whole reason the entries are
inherited rather than imported.

So the entry for each of your components is injected into every *other* type of yours that might name
one — every component, and every `RaskMarkup` host — which needs a `partial` to inject it into:

```csharp
public sealed class Dashboard : Component        // ✗ RASK036 — no partial to inject into
{
    protected override Component? Render() => Div[SalesCard];   // CS0103: 'SalesCard' not found
}

public sealed partial class Dashboard : Component   // ✓
{
    protected override Component? Render() => Div[SalesCard];
}

public partial class DashboardTests : RaskMarkup     // ✓ — same rule outside a component
{
    [Fact]
    public void It_renders() => Assert.Equal("<div>…</div>", Div[SalesCard].ToHtml());
}

[RaskMarkup]                                         // ✓ — and when the base slot is not yours
public static partial class Demos                    //     to spend, or there is none to spend
{
    public static Component Badge() => Div[SalesCard];
}
```

For a host that **derives** from `RaskMarkup`, nothing else is lost: the component still renders, still
gets its own entry *elsewhere*, and the type itself is unaffected — `new SalesCard()` inside Rask.Core keeps
working from anywhere. An **`[RaskMarkup]`** host loses more, and the message says so: the generated
`partial` is where its base — or, when the base slot is already spent, the framework tags themselves —
would have come from, so without `partial` it gets no builder surface at all.

A **nested** host is injected into as well — the generated file re-opens each enclosing type as a
`partial` around it — so every one of them has to be `partial` too. When one is not, this is the warning
you get, naming the nested component that loses its entries:

```csharp
public partial class RouterTests : RaskMarkup        // ✓ — enclosing type is partial too
{
    private sealed partial class CounterPage : Component
    {
        protected override Component? Render() => Div["…"];
    }
}
```

This used not to matter: the HTML tags lived in `Rask.Core` and reached a nested component by
*inheritance*, where nesting is irrelevant. They ship from `Rask.Html` now, and a referenced library's
entries can only be injected — so a nested component whose enclosing chain is not `partial` would
silently lose the chain, which is what this reports instead.

**Fix:** add `partial`. Suppress with `#pragma warning disable RASK036` / `.editorconfig`
(`dotnet_diagnostic.RASK036.severity = none`) if you build every component through the factory.

## RASK037
**`using` alias is hidden by a builder entry** · Warning

On the builder surface every component type contributes an **entry** — a member named after itself,
inherited by every component (`Div`, `Card`, `Line`). Inside a component body a member beats a
`using` alias in simple-name lookup, so an alias that shares an entry's name quietly stops meaning
what it says:

```csharp
using B = Acme.Benchmarks;               // ✗ RASK037 — the <b> tag's entry wins

public sealed partial class Report : Component
{
    protected override Component? Render() =>
        Div[B.Summary.Render()];         // CS1061: 'B' does not contain a definition for 'Summary'
}
```

The compiler's own message is **CS1061** at the *use*, naming a `B` nobody wrote and pointing nowhere
near the alias. It is also unreachable by a quick-fix: by the time the error exists the alias has
already lost the lookup. RASK037 reports it at the alias instead, before it is ever used.

The analyzer flags an alias only when an entry actually claims the name — either on a component
declared in the same file, or (for a `global using` alias) on `Component` itself. Aliases in files
that declare no component are left alone.

**Fix:** rename the alias to something no tag or component uses (`using Bench = Acme.Benchmarks;`).
The two-letter tag names are the ones that bite: `A`, `B`, `I`, `P`, `Td`, `Tr`. Suppress with
`#pragma warning disable RASK037` / `.editorconfig` (`dotnet_diagnostic.RASK037.severity = none`) if
the alias is only ever used outside a component body.

## RASK038
**Builder chain does not set a required property** · Error

A non-nullable property with no member initializer is **required** — see [RASK001](#rask001). Most
required properties are enforced by the chain's own type: they are steps the component does not exist
until you take. This analyzer covers what that cannot reach — a chain the compiler cannot follow end to
end (see [RASK039](#rask039)), where the property is set by a setter somewhere along the way and leaving
it out compiles cleanly, rendering with a `null` it was never supposed to hold.

```csharp
public sealed partial class Card : Component
{
    public string Title { get; set; }          // required: non-nullable, no initializer
    public string? Note  { get; set; }         // optional
}

Card.Note("later")                             // ✗ RASK038 — 'Title' is never set
Card.Title("Q3").Note("later")                 // ✓
```

Order does not matter, and child indexing (`Card.Title("Q3")[…]`) is part of the same expression. A
property whose setter drops an `On` prefix (`OnSave` → `.Save(…)`) counts under either spelling.

Properties **declared in your own compilation** are read straight off the syntax, where the member
initializer is right there. A property from a **referenced assembly** cannot be: an initializer
compiles into the constructor and leaves no trace in metadata, so `string Title` and
`string Title = ""` are the same symbol from outside. The owning assembly therefore publishes the
answer — the factory generator emits one
`[assembly: RaskRequiredProperties("Rask.Bootstrap.BsIcon", "Name")]` per component with such a
property — and this analyzer reads it back. A library built by an older Rask, or by no Rask at all,
publishes nothing, and its properties are then counted only when they carry the language's `required`
modifier, which metadata does preserve.

**Fix:** add the setter to the chain, or — if the property really is optional — give it a nullable
type or a member initializer, which is what marks it optional for both surfaces. Suppress with
`#pragma warning disable RASK038` / `.editorconfig` (`dotnet_diagnostic.RASK038.severity = none`).

## RASK039
**Builder chain is split across statements, so its required properties can't be checked** · Warning

[RASK038](#rask038) is only sound while the chain is a single expression. Store it in a local or a
field and the remaining setters can be applied anywhere — in a branch, a loop, another method — so
claiming a property is missing would be a guess. Rask reports the gap in the analysis instead of a
wrong answer:

```csharp
var card = Card.Note("later");          // ✗ RASK039 — 'Title' may or may not be set below
if (highlight) card = card.Title("!");  //   …and here it depends on a runtime value
return card;
```

The warning only appears when something is still missing at the end of the visible chain: a stored
chain that is already complete says nothing.

**Fix:** keep the chain in one expression, or set the required properties before storing it.
Suppress with `#pragma warning disable RASK039` / `.editorconfig`
(`dotnet_diagnostic.RASK039.severity = none`) if you assemble components across statements by design.

## RASK040
**Two components share a simple name, so neither can have a builder entry** · Warning

A member name has no namespace, so the two do not separate the way the types themselves do. An
entry is keyed by **simple name**: it is a single member named after its type, and one name can only
stand for one type.

```csharp
namespace Features.Products { public sealed partial class Card : Component { } }   // ✗ RASK040
namespace Features.Orders   { public sealed partial class Card : Component { } }   // ✗ RASK040
```

Neither component gets an entry, because choosing which type `Card` means is the author's decision,
not the generator's. Both stay reachable through their generated factories, so nothing stops
compiling — you just cannot write `Card` bare.

**Fix:** rename one of them (`ProductCard` / `OrderCard`). Suppress with
`#pragma warning disable RASK040` / `.editorconfig` (`dotnet_diagnostic.RASK040.severity = none`) if
you are happy to build both with `new` from inside Rask.Core.

## RASK041
**The builder surface's shared pending-bit budget is exhausted** · Warning

This one is for people *changing Rask itself*, not for app code. A chain writes only the properties
it names, so a builder entry marks its folding properties **pending** and resets whatever is still
pending when the parent's `Render()` returns — that is what makes `Div.Id("x")` on one render and a
bare `Div` on the next drop the `id`, exactly as the factory does. The pending bits are split so a
component compiled against one `Rask.Core` cannot collide with a shared property added in a later
one: the shared `Element`/`Component` surface owns the low 32 (`BuilderRuntime.OwnPendingBit`), each
component's own properties get the rest.

Those 32 are handed out in ordinal **name** order, which is the trap: adding one folding property too
many to `Element` does not push *itself* off the end — it pushes whichever alphabetically-later
property was last (`Title`, `TabIndex`) onto the eager reset path, which reports that property changed
on every render and defeats the render cache for it. Nothing fails to compile and no test goes red,
which is why the generator counts them.

**Fix:** raise `Rask.Core.BuilderRuntime.OwnPendingBit` and the generator's mirrored `OwnPendingBit`
constant **together** (they are a wire format between an app and the Rask it was built against), or
make the new property non-folding. The budget was raised 16 → 32 when the global attributes landed
(#693) and the shared surface reached 19 folding properties; because a component compiled against the
old value numbered its own properties from 16, everything must be rebuilt against the new pair.

## RASK042
**Retired** — delegate-typed property cannot receive a builder setter

Reported while a chain's receiver was the component itself: a delegate-typed property is *invocable*, so
`.OnClick(Save)` bound to the property instead of to the same-named setter, and the setter was
unreachable dead code. The fix at the time was to wrap the delegate in a carrier.

A chain now receives on `Build<TComponent>`, so the property is not on the receiver, the lookup that
caused this cannot happen, and a callback property is an ordinary `Action` / `Func<…>`. The ID is not
reused.

## RASK043
**A component name is used in a type that has no builder entries** · Warning

The chain is reachable only from **inside a type that has the entries**. They are *inherited members* —
that is the whole design, because a static-imported property loses to a same-named type (CS0119) while
a member of the enclosing type wins. A component is such a type; so is anything deriving from
**`Rask.Core.RaskMarkup`**, which is `Component`'s own base and carries the framework entries and
nothing else; and so is anything marked **`[RaskMarkup]`**, which is the same opt-in for a type that
has no base slot to spend.

In a type that is none of those, the simple name binds to the component **type** instead:

```csharp
using Rask.Core.Components;

internal static class Parts
{
    public static Component Loading() => Div.Class("spinner")["…"];   // ✗ RASK043 — CS0119
}
```

```csharp
using Rask.Core;

// ✓ a static class can derive from nothing, so the attribute is the way in — it stays static and the
//   framework entries are injected as its own members. Prefer `: RaskMarkup` when the base slot is
//   free; the attribute takes it for you in that case anyway, and only injects when it cannot.
[RaskMarkup]
internal static partial class Parts
{
    public static Component Loading() => Div.Class("spinner")["…"];
}
```

The compiler's own report is **CS0119** ("'Div' is a type, which is not valid in the given context"),
often with a **CS0021** on the `[…]` that would have carried the children, or a **CS0120** in a static
context — none of which mentions Rask or the one line that fixes it.

**Fix:** derive the enclosing type from `Rask.Core.RaskMarkup`, or mark it `[RaskMarkup]` when its base
slot is taken or it is a `static class` — or, if it was really a component all along, make it one. A
`static class` cannot derive from anything, so the attribute is the way in there; nesting it inside a
host works too, since simple-name lookup walks out through enclosing types. Suppress with
`#pragma warning disable RASK043` / `.editorconfig`
(`dotnet_diagnostic.RASK043.severity = none`).

## CS0108 (a member hides a builder entry)

Not a Rask diagnostic, but a Rask quick-fix. Because every component type contributes an entry named
after itself, any member that shares a tag's or a component's name now **hides** one, and the
compiler asks for `new`:

```csharp
public sealed partial class BsModal : Component
{
    public new Component? Footer { get; set; }        // vs the <footer> entry
    private new Component Section(string t) => …;     // vs the <section> entry
    public new sealed record Line(int X, int Y);      // vs the SVG <line> entry
}
```

The lightbulb inserts `new` where `csharp_preferred_modifier_order` wants it (after the accessibility,
before `sealed` / `readonly`), and is offered **only** inside a component — hiding in your own class
hierarchy is your design decision, not the framework's.

> Deliberately a code fix and not a `DiagnosticSuppressor`. A suppressor satisfies the compiler, but
> `dotnet format` does not honour suppressors and applies the underlying fix anyway, so the format
> gate never settles.

The related `using`-alias collision cannot be fixed this way — it surfaces as a hard CS1061 after the
alias has already lost the lookup, which is what [RASK037](#rask037) exists for.

---

## RASK044
**Builder chain sets the same property twice** · Warning

A setter writes its property and hands the component back, so a chain that names one twice simply
overwrites it. The last call wins, the earlier one has no effect, and the compiler is perfectly happy —
which is why this needs saying out loud.

```csharp
Card.Title("Coffee").Note("Dark roast").Title("Tea")   // ✗ RASK044 — renders "Tea"
Card.Title("Coffee").Note("Dark roast")                // ✓
```

Two writes to one property are always either a merge artefact or a copied line that was not adjusted.
Nothing about the shape is legitimate: if the value really is conditional, compute it once and pass it.

```csharp
Card.Title(featured ? "Coffee" : "Tea")                // ✓
```

**Reported once per chain**, naming the property, not once per extra call.

Two *separate* chains are not a duplicate — `Div[Card.Title("a"), Card.Title("b")]` is two components
that each name `Title` once, which is ordinary markup.

**Why an analyzer and not the type.** The chain already makes some mistakes unwritable: a required
property cannot be omitted, and `Bind` and `Value` cannot both be used, because each step returns a type
that offers only what is still legal. Extending that to *every* setter would mean one state per subset of
the surface — 2^n over roughly ninety properties — where the required-property machinery pays 2^k over
the few that are required. So this one is reported rather than prevented.

Silence it per line with `#pragma warning disable RASK044`, or per project in `.editorconfig`
(`dotnet_diagnostic.RASK044.severity = none`).

## RASK045
**Component built by a chain is assigned to afterwards** · Warning

A chain states everything a component was given, in one expression, where the reader of the call site
can see it. An assignment after the chain has ended is invisible from there, and nothing reconciles the
two — a chain step and a later write to the same property simply disagree, and the write wins.

```csharp
Card c = Card.Note("a");
c.Note = "b";                       // ✗ RASK045 — the chain says "a", the reader has to find this line

Card.Note("b")                      // ✓ one expression, one answer
```

Only a component a **chain** produced is held to this. One built any other way — `new`, a factory, a
field the component assigned itself — is not reported: the surface it came through is what decides, and
only a chain promises to be the whole story.

It has to be an analyzer rather than a property of the type. `Build<T>` converts implicitly to the
component it built, which is what keeps the chain out of the way at every call site that wants the
component itself (a property typed as a particular component, a strongly-typed children collection, a
test asserting on the result). Once it has converted, the result is an ordinary component with ordinary
settable properties, and nothing in the type system is left to forbid the write.

**Fix:** move the assignment into the chain — every property a chain can reach has a step of the same
name. Suppress with `#pragma warning disable RASK045` / `.editorconfig`
(`dotnet_diagnostic.RASK045.severity = none`) where a component genuinely has to be completed later.

---

## RASK046
**Key must open a component's chain** · Warning

A keyed child is identified by its **key** rather than by its position among its siblings, so that the
state a row holds itself — a private field, an edit buffer, an `OnMount` subscription — moves with the
item rather than with the slot when the list changes shape. Settling that identity means handing back
the instance the key owns and discarding the one the entry just built, so any step written **before**
`Key` is applied to a component that is about to be thrown away:

```csharp
TodoRow.Item(item).Key(item.Id)   // ✗ RASK046 — Item is written to the instance Key then discards
TodoRow.Key(item.Id).Item(item)   // ✓ identity first, then everything else
```

It compiles and it renders. The value goes missing only once the list changes shape — an insert at the
top, a reorder — which is the worst possible time to discover it.

**Elements are exempt, and that is not a carve-out.** An element is re-specified in full on every
render: whatever its chain does not name, the deferred reset puts back. Its instance therefore carries
nothing, it is never claimed, and its DOM identity comes from `data-rask-key` in the diff codec rather
than from the parent's child map. So the common spelling stays exactly as it reads:

```csharp
Div.Class("row").Key(index)[cells]   // ✓ an element is never claimed
```

**Fix:** move `.Key(…)` to the front of the chain. See [composition → keys](composition.md#children--fragments)
and the reconciliation note in [the live-rendering codec](architecture/live-rendering-codec.md).

---

## RASK047
*Retired.* It reported a `Page.Route` override that was not a compile-time constant. Routes are declared with
`[Route("...")]`, whose argument is an attribute argument and therefore constant by construction, so the failure
it guarded can no longer be written. The id is retired, not reused.

## RASK032, RASK048, RASK049, RASK050
*Retired.* All four guarded the native hosting model: native chrome inside an HTML tree (RASK032), HTML
inside a native screen (RASK048), a `NativeWebView` that set a `Url` *and* took children (RASK049), and a
native head named on a web-only host (RASK050). Rask is a web framework — `Rask.Native` and every type
those rules mentioned are gone, so none of the mistakes they caught can be written any more. The ids are
retired, not reused.

## RASK051

**Translation catalog is malformed** · Error

A translation catalog is a JSON object whose values are text or further objects, named
`Resources/{Family}.{culture}.json`. This fires when one cannot be read, or when it describes strings
that would fail at runtime.

```jsonc
// Resources/Strings.en.json
{
  "Greeting": "Hello, {name}!",
  "Home": { "Title": "Dashboard" }
}
```

The reported cause names the file and the problem:

| Cause | Why it is an error |
|---|---|
| a JSON syntax error, a duplicate key, a value that is not text or an object | nothing can be generated |
| a key that is not a usable C# identifier | the member it would generate cannot be written |
| an unclosed `{`, a stray `}`, a mix of `{0}` and `{name}` | the message cannot be turned into a format string |
| a translation whose **placeholder set** differs from the neutral catalog's | `string.Format` throws `FormatException` the first time that string renders — in that one language only |
| no catalog for the neutral language | nothing defines which keys exist |

The placeholder rule is about the *set*, not the order: other languages reorder arguments, and naming
placeholders is what makes that safe.

```jsonc
// Resources/Strings.hu.json — fine, the same names in a different order
{ "M": "{b} majd {a}" }

// ✗ RASK051 — {nev} is not {name}, so this would throw when a Hungarian visitor sees it
{ "Greeting": "Szia, {nev}!" }
```

### Plural sets

A key whose text depends on a count is written as an object carrying `$plural`:

```jsonc
{ "Cart": { "$plural": "count", "one": "{count} item", "other": "{count} items" } }
```

RASK051 also fires when such a set cannot produce correct grammar:

| Cause | Why |
|---|---|
| Rask does not carry that language's plural rules | applying English rules would produce text that reads as broken to a native speaker, and nothing at runtime would say so |
| the language's **residual** form is missing | it is the arm every unmatched count lands on |
| a form the language never selects (`few` in English) | that text could never be shown |
| a form that is not a CLDR category at all | it is a typo |
| the key is a plural set in one language and a single string in another | they generate different members |

**The residual is not always `other`.** Polish integers never select `other` — CLDR routes the residual
to `many` — so a Polish catalog supplies `one`/`few`/`many` and requiring `other` there would mean
writing text no visitor could ever see.

```jsonc
// Resources/Strings.pl.json — complete, and correctly has no "other"
{ "Cart": { "$plural": "n", "one": "{n} plik", "few": "{n} pliki", "many": "{n} plików" } }
```

**Fix:** correct the file the message names. A JSON file in `Resources/` that is *not* a catalog needs
no action — one without a culture tag in its name is ignored.

## RASK052

**Translation catalog disagrees with the neutral catalog** · Warning

The neutral catalog defines which keys exist; a translation supplies their text. This fires when a
translation is missing a key, or carries one the neutral catalog does not define.

```jsonc
// Resources/Strings.en.json
{ "Save": "Save", "Cancel": "Cancel" }

// Resources/Strings.hu.json
{ "Save": "Mentés" }        // ⚠ RASK052 — no translation for 'Cancel'
```

A missing translation is a **warning**, not an error, because a partly translated app is the normal
state of every real project: the neutral text is used until it is filled in, so the page works. The
opposite case — a key only a translation has — is also a warning: it generates nothing and is almost
always a rename that was applied to one file.

A plural set is checked the same way: a translation missing a category **its own language**
distinguishes is reported, and the residual form carries the page until it is filled in.

```jsonc
// Resources/Strings.ru.json — ⚠ RASK052, Russian also distinguishes "few"
{ "Cart": { "$plural": "n", "one": "{n} файл", "many": "{n} файлов" } }
```

**Fix:** add the key, or delete it. To gate a release on complete translations, promote it:

```ini
# .editorconfig
dotnet_diagnostic.RASK052.severity = error
```

Or silence it while translation is in progress with `= none`.

## RASK053

**Remote message has no wire encoding** · Error

A message reaches a handler in another process by being *encoded*, and Rask generates that encoder at
compile time rather than discovering it by reflection — which is what lets a remote dispatch publish
clean under the WASM/AOT trimmer. The cost of that choice is that the set of shapes a message may take
is fixed, and a shape outside it has to be reported now rather than failing on the wire.

```csharp
// ✗ RASK053 — an interface names no single concrete type, so the receiver cannot know what to build
public sealed record Search(IFilter Filter) : IQuery<Hit[]>;

// ✓ a concrete type has one shape, so both sides agree on it
public sealed record Search(TextFilter Filter) : IQuery<Hit[]>;
```

**Supported shapes.** `bool`, the numeric types, `char`, `string`, `Guid`, `DateTime`,
`DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Uri`, enums (encoded as their number, so
renaming a member does not break the wire), `byte[]` (base64), `Nullable<T>` of any of those, arrays
and `List`/`IReadOnlyList`/`IEnumerable`/`ICollection`/`IList` of them, `Dictionary` keyed by `string`,
and records or classes composed of the same. A composite needs either a public constructor whose
parameters all match properties — which every positional record has — or a public parameterless
constructor with settable properties.

**Not supported, and why.** An interface or abstract type names no single thing to construct.
`object` has no shape at all. An open generic has no single wire shape. A type that refers back to
itself has no finite encoding. A dictionary keyed by anything but `string` has no key encoding, because
a JSON object's keys are strings. A `RaskFile` is allowed only as a *direct* property of the message:
a file is addressed by its index in the multipart body, written where the property sits in the JSON, so
one nested inside a list or a sub-object would have nowhere to be addressed from and its bytes would be
dropped.

**Fix:** give the property a concrete, supported type — or, if the message is never sent anywhere,
mark it `[LocalOnly]`:

```csharp
// A job payload, an outbox event, a command only another handler publishes: never on the wire, so
// never encoded, so free to carry whatever its handler finds convenient.
[LocalOnly]
public sealed record RebuildIndex(IComparer<string> Order) : ICommand;
```

`[LocalOnly]` on an **interface** marks every message implementing it, which is how `Rask.Jobs`'
`IJob` and `Rask.Outbox`' `IOutboxEvent` keep whole families of in-process messages out of the wire
vocabulary at once.

> This diagnostic only fires in a project that references a remote transport (`Rask.Cqrs.Client` or
> `Rask.Cqrs.Server`). An app using `Rask.Cqrs` purely in-process generates no codecs, so none of these
> constraints apply to its messages.

## RASK054

**Page cannot run in the browser** · Info

A routed page injects something that only exists in the server process, so it stays server-live and
will not move into WebAssembly.

**This is not a fault.** The page is correct, and for most apps this describes every data page. It is
Info rather than a warning for exactly that reason — a warning on each of them would be noise nobody
reads. The diagnostic exists so that *"why did this page not move?"* has an answer at the call site
rather than only in a runtime log.

```csharp
using Microsoft.EntityFrameworkCore;

[Route("/orders")]
// ℹ RASK054 — a DbContext cannot exist in a browser, so this page stays server-live
public sealed partial class Orders(IDbContextFactory<AppDb> db) : Component
{
    protected override Component? Render() => /* … */;
}
```

**To make the page eligible**, reach the same data through something that already crosses the wire —
a `Rask.Query` query or a CQRS message, both of which are dispatched remotely by default:

```csharp
[Route("/orders")]
public sealed partial class Orders(IQueryClient client) : Component   // no diagnostic
{
    private readonly Query<Order[]> _orders = client.Query(new GetOrders());

    protected override Component? Render() => /* … */;
}
```

**What it looks at.** Constructor parameters of a component carrying `[Route]`. It does not report a
shared component that injects a server-only type — that is a property of whichever page uses it, and
pointing at the component would name a file whose author cannot see which page is affected.

**The list of server-only types is short and deliberately not exhaustive:** Entity Framework's
`DbContext` and `IDbContextFactory<T>`, and anything from the `Rask.Server` assembly. It names what
this framework hands people rather than everything that could fail in a browser, because the analyzer
compiles against the server half and has no view of what a browser build references.

---

## RASK055

**Scoped JavaScript is no longer supported** · Error

A `.js` file sits beside a component of the same name. Scoped component assets are TypeScript, and
Rask neither compiles nor registers a `.js` sibling.

```
Features/Counter.cs
Features/Counter.js     ✗ RASK055
Features/Counter.ts     ✓
```

**Fix:** rename the file. TypeScript is a superset of JavaScript, so an existing ES module is already
valid TypeScript — the body needs no change, and `tsgo` compiles it before the browser sees it. Add
type annotations at whatever pace suits you.

**Why this is an error and not a quiet skip.** A scoped script that stops being registered does not
fail. `window.Rask["Counter"]` simply has no methods on it, so every call from C# resolves to nothing
and the component renders a control that does nothing — with no error at build time, none at
startup, and none in the console. There is no useful place for that to surface later, so it surfaces
here.

**It fires only for a real scoped asset.** The rule is "a `.js` beside a non-abstract, non-generic
`Component` subclass of that name" — exactly the set of files that worked as scoped JavaScript
before. A `Helpers.js` next to an ordinary static `Helpers.cs`, or any vendored script, is somebody
else's file and is left alone.

> Files under `wwwroot/`, `Resources/` and `Browser/` are outside the scoped-asset convention
> entirely and are never considered. A plain site-wide script belongs in `wwwroot` and is linked from
> your `Head`, exactly as before.

## RASK056
**`AddRask` is called twice on the same service collection** · Warning

A second `AddRask` does not add to the first. Its options are registered with `TryAddSingleton`, which
keeps the registration already there — so everything the later call configures is discarded, while the
call itself compiles and reads exactly as though it worked.

The visible casualty is `configureCulture`. The second call builds a fresh `RaskCultureOptions`, runs
your callback over it, and then loses the registration race, so an app that named its languages ships
with **none**. It is worse than a plain no-op: `AddRaskCulture` still flips the process-wide
`RaskCulture.IsEnabled`, so culture negotiation turns on over an empty catalog.

```csharp
// ✗ builder.Services.AddRask();
//   builder.Services.AddRask(configureCulture: c => c.SupportedCultures.Add("hu"));   // silently dropped

// ✓ builder.Services.AddRask(configureCulture: c =>
//   {
//       c.SupportedCultures.Add("en");   // the first entry is the default
//       c.SupportedCultures.Add("hu");
//   });
```

**Fix:** pass every option to a single `AddRask` call. The warning fires only for two calls **in the
same method body on the same receiver as written**, so a test file that builds one `ServiceCollection`
per case — or a method configuring two collections side by side — is left alone. See
[configuration](configuration.md) and [localization](localization.md).
