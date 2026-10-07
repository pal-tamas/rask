# Rask diagnostics (RASK001–RASK101, RASKVAL001–RASKVAL002, RASKISLAND001–018)

Every Rask diagnostic, what triggers it, and how to fix it. Errors block the build; warnings don't
but flag a real problem; the hidden ones are informational, surfaced only as an IDE suggestion.

These come from the Rask source generator and analyzers (`Rask.Generators`). The generated chain
surface doesn't exist until a build runs, so if an ID below isn't recognised by your IDE yet,
build once. The islands build has a sequence of its own, logged by MSBuild rather than by an analyzer:
[RASKISLAND001–018](#island-build-diagnostics-raskisland).

Some diagnostics ship an **IDE quick-fix** (the lightbulb / `Ctrl`+`.`):

| ID | What the lightbulb does |
|----|-------------------------|
| **RASK001** | adds the `required` modifier |
| **RASK014** | rewrites `new Widget()` into the bare entry `Widget` |
| **RASK023** | appends `.Alt("")` to the chain |
| **RASK026** | deletes the redundant `StateHasChanged()` statement |
| **RASK067** | swaps ASP.NET's `[Route]` for Rask's own |
| **RASK084** | makes the accessor `private set` / `private init`, or the field `private` |
| **RASK085** | moves the collection into a `private readonly` field and exposes `IReadOnlyCollection<T>` |
| **RASK095** | adds the missing required steps right after the chain so far (or moves one taken too late) |
| **RASK096** | declares the event as a non-nullable `Callback` and fires it with `.Invoke(…)` |

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
| [RASK001](#rask001) | Hidden | Property is a required chain step |
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
| [RASK024](#rask024) | Warning | `UseAuthentication()` must precede `MapRask()` |
| [RASK025](#rask025) | Warning | `InputType` conflicts with the bound `HTMLInputElement<T>` value type |
| [RASK026](#rask026) | Warning | Redundant `StateHasChanged` in a Rask callback |
| [RASK028](#rask028) | Error | Ambiguous request handler (more than one handler for a query/command) |
| [RASK029](#rask029) | Warning | Handler cannot be registered (open generic, no public constructor, or unnameable) |
| [RASK031](#rask031) | Warning | Two pages resolve to the same route |
| [RASK033](#rask033) | Warning | Hardcoded path for internal navigation instead of the generated route URL |
| [RASK035](#rask035) | Warning | Background job type cannot be registered |
| [RASK036](#rask036) | Warning | A chain-entry host must be `partial` |
| [RASK037](#rask037) | Warning | `using` alias is hidden by a chain entry |
| [RASK038](#rask038) | Error | Chain does not set a required property |
| [RASK039](#rask039) | Warning | Chain is split across statements, so its required properties can't be checked |
| [RASK040](#rask040) | Warning | Two components share a simple name, so neither can have a chain entry |
| [RASK041](#rask041) | Warning | The chain surface's shared pending-bit budget is exhausted |
| [RASK043](#rask043) | Warning | A component name is used in a type that has no chain entries |
| [RASK044](#rask044) | Warning | Chain sets the same property twice |
| [RASK045](#rask045) | Warning | Component built by a chain is assigned to afterwards |
| [RASK051](#rask051) | Error | Translation catalog is malformed |
| [RASK052](#rask052) | Warning | Translation catalog disagrees with the neutral catalog |
| [RASK053](#rask053) | Error | Remote message has no wire encoding |
| [RASK055](#rask055) | Error | Scoped JavaScript is no longer supported |
| [RASK056](#rask056) | Error | External component must be partial |
| [RASK057](#rask057) | Error | External component prop has no wire encoding |
| [RASK058](#rask058) | Error | External component name collision |
| [RASK059](#rask059) | Error | Module or Export override must be a constant string |
| [RASK060](#rask060) | Warning | `AddRask` is called twice on the same service collection |
| [RASK061](#rask061) | Error | Blazor island must be partial |
| [RASK062](#rask062) | Error | An island cannot render these children |
| [RASK064](#rask064) | Error | Blazor island name collision |
| [RASK066](#rask066) | Warning | Hosted Blazor component's parameters cannot be verified |
| [RASK067](#rask067) | Error | Endpoint shape has no wire encoding |
| [RASK068](#rask068) | Warning | Endpoint has no generated client method |
| [RASK069](#rask069) | Error | Two endpoints claim one client method |
| [RASK070](#rask070) | Warning | Endpoint's response type is not statically known |
| [RASK071](#rask071) | Error | ASP.NET route attribute on a Rask component |
| [RASK072](#rask072) | Warning | Entity `Configure` method will not be called |
| [RASK073](#rask073) | Warning | Strongly-typed id has no usable value |
| [RASK074](#rask074) | Warning | More than one user type |
| [RASK076](#rask076) | Warning | Grid column with no field token |
| [RASK077](#rask077) | Warning | Package island has no props snapshot |
| [RASK078](#rask078) | Error | Props snapshot cannot be read |
| [RASK079](#rask079) | Error | Props snapshot describes a different component |
| [RASK080](#rask080) | Warning | Package prop was not generated |
| [RASK082](#rask082) | Error | A type already has the generated model's name |
| [RASK083](#rask083) | Warning | Nested entity gets no generated model |
| [RASK084](#rask084) | Error | Model state can be changed from outside the type |
| [RASK085](#rask085) | Warning | Entity exposes a mutable collection of entities |
| [RASK086](#rask086) | Warning | Aggregate has no parameterless constructor, so `Create` is not generated |
| [RASK087](#rask087) | Error | Aggregate reaches across a boundary instead of holding an id |
| [RASK088](#rask088) | Warning | Child collection cannot be synced, so a save cannot add or remove one |
| [RASK089](#rask089) | Warning | Id looks like a reference but no navigation was inferred |
| [RASK090](#rask090) | Warning | Two entities want one DbContext set name, so neither is generated |
| [RASK091](#rask091) | Warning | A child cannot choose its own form writes — its root decides |
| [RASK092](#rask092) | Warning | A unit reads wrong for its count (`2.Hour`, `1.Hours`) |
| [RASK093](#rask093) | Error | An awaitable result is dropped, so the call never runs |
| [RASK094](#rask094) | Warning | A scoped TypeScript export gets no typed method on its component |
| [RASK095](#rask095) | Error | A chain skips a required step |
| [RASK096](#rask096) | Error | An event is declared as a delegate, so its chain setter is unreachable |
| [RASK097](#rask097) | Error | A route helper's name collides with a nested `Routes` class |
| [RASK098](#rask098) | Warning | A web API member is missing from a browser `<RaskBrowserTargets>` names |
| [RASK099](#rask099) | Warning | A `<RaskBrowserTargets>` entry is not `<browser> >= <version>` |
| [RASK100](#rask100) | Error | `[BlazorParameter]` names a parameter the hosted component does not declare |
| [RASK101](#rask101) | Error | An authorization attribute on a handler or event is not one Rask reads |
| [RASKVAL001](#raskval001) | Error | Two validators for the same model |
| [RASKVAL002](#raskval002) | Warning | Validator cannot be constructed automatically |

**Retired, never recycled:** RASK027, RASK030, RASK032, RASK034, RASK042, RASK046, RASK047, RASK048,
RASK049, RASK050, RASK054, RASK075 and RASK081. Each guarded a mistake that can no longer be written; a
suppression that names one is dead and can be deleted. RASK063 and RASK065 are reserved.

---

## RASK001
**Property is a required chain step** · Hidden

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

**Fix:** remove the member initializer so the `required` property becomes a chain step,
**or** remove `required`. Framework services (`RouteState`, `HttpClient`, `IJSRuntime`)
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
// ✗ new HTMLDivElement()
// ✓ Div.Class("panel")[ ... ]
```

**Fix:** name it and chain onto it — `Counter.Start(3)`, or `Counter` alone when it needs nothing. An element
is named by its tag, not its type: the message and the quick fix turn `new HTMLDivElement()` into `Div`, and a
type several tags share (`new HTMLElement()`) gets no quick fix, since only you know whether it was an `Em` or a
`Section`. In
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

Children were passed to `Head[…]`. Rask collects, dedupes, and splices head content itself, so the
`<head>` element doesn't take children.

**Fix:** override `protected override Component? HeadAssets => ...` on any component and return your
`Title`/`Meta`/`Link`/`Script` — a single tag, or a collection expression like
`HeadAssets => [Title["..."], Meta.Name("description").Content("...")]`. See [the head guide](getting-started.md).

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
    [Doctype, Html.Lang("en")[Head, Body[Router]]];

// ✓ the root renders the body's content
protected override Component? Render() => Router;
```

**Fix:** return the body's content (usually `Router`) and move the shell's pieces to the overrides
that own them — `<head>` content to `Head`, `<html lang>` to `HtmlLang`, `<html dir>` to `HtmlDir`, `<body class>` to `BodyClass`,
and a genuinely custom document to `Shell(head, body)`, which receives the framework's `<head>` and the
rendered body as parameters. Do **not** add a runtime `<script>`; it's auto-appended to `<body>`.
`Doctype`/`Html`/`Head`/`Body` stay ordinary tag components for documents you build by hand
(`ToHtml()`, an email body) — they have just left the app-authoring path. See
[the document and the `HeadAssets` override](getting-started.md#7-the-document-and-the-headassets-override).

## RASK022
**List item is missing a `Key`** · Warning

A Rask component appears in a sibling-list context (a `.Select`/`.SelectMany` projection, or `.Add`
in a loop) without a `Key`. Keyless items reconcile **by position**, which loses focus and input state
and emits untrusted structural diffs on insert/remove/move — and for a component, position also
decides which instance is reused, so the row's own state follows the slot rather than the item.

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

**Fix:** add a meaningful `.Alt(…)`, or the empty string `.Alt("")` for a purely decorative image so
assistive technology skips it (**quick-fix available** — the IDE lightbulb appends `.Alt("")`, which you
then fill in for informative images). See [accessibility](accessibility.md).

## RASK024
**`UseAuthentication()` must precede `MapRask()`** · Warning

`app.MapRask<App>()` is wired before `app.UseAuthentication()`. Rask seeds the live session from
`HttpContext.User` during the initial GET render and the WebSocket upgrade — if the authentication
middleware runs *after* `MapRask`, the principal is empty at that point and every `[Authorize]` page
challenges.

```csharp
// ✗ app.MapRask<App>();
//   app.UseAuthentication();
// ✓ app.UseAuthentication();   // populates HttpContext.User on GET + WS upgrade
//   app.UseAuthorization();
//   app.MapRask<App>();
```

**Fix:** call `app.UseAuthentication()` (and `app.UseAuthorization()`) before `app.MapRask()`. The
warning fires only when both calls are present and `UseAuthentication` is positioned after `MapRask`;
an app with no authentication middleware is left alone. See [authentication](authentication.md).

## RASK025
**`InputType` conflicts with the bound `HTMLInputElement<T>` value type** · Warning

A generic `HTMLInputElement<T>` derives its HTML input `type` from `T` (`bool`→checkbox, `int`/`decimal`→number,
`DateOnly`→date, …). The *string-only* `InputType`s — `Text`, `Search`, `Tel`, `Url`, `Email`,
`Password` — only apply to `HTMLInputElement<string>`; pairing one with `HTMLInputElement<int>`/`HTMLInputElement<bool>`/… is a mistake
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
for a statically-known string-family `InputType` on a non-`string` `HTMLInputElement<T>`; `HTMLInputElement<int>` with `Type(InputType.Number)` and any `HTMLInputElement<string>` are left alone. Suppressible like any analyzer.

## RASK026
**Redundant `StateHasChanged` in a Rask callback** · Warning

Rask re-renders the component that *owns* an event/binding callback automatically after the callback
runs — including when a child control raised it (the framework re-renders the delegate's owner, captured
from the lambda's `this`) and after a two-way bound write (the binding re-renders its authoring
component). So calling your own `StateHasChanged()` from inside `OnChange`/`OnClick`/`OnInput`/`OnAnySubmit`/…
or the `AfterBind` hook is dead weight. The tell-tale anti-pattern is reaching for
`.AfterBind(_ => StateHasChanged())` to make derived UI refresh.

```csharp
// ✗ redundant — the framework re-renders this component after OnChange runs:
Select.Of<string>().Value(_pick).OnChange(v => { _pick = v; StateHasChanged(); })
// ✓ just update state; the render is automatic:
Select.Of<string>().Value(_pick).OnChange(v => _pick = v)

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
request types). Events are exempt — an `IEvent` may have any number of
`IEventHandler`s.

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
    public Task<int> Handle(GetValue query) => Task.FromResult(1);
}
```

**Fix:** give the handler a public constructor, make it a closed (non-generic) type, or raise its
accessibility to at least `internal` and move it out of a `file`-local declaration. A request with
*no* handler at all is not flagged (the handler may live in another assembly) — it throws a clear
`InvalidOperationException` when dispatched.

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

Rask generates a type-safe route helper — `Routes.<Page>()` — for every page's **primary** `[Route]`
(see [Routing → type-safe URLs](routing.md)). Using the raw path string for internal navigation bypasses
that safety: rename or remove the `[Route]` and the string becomes a silent dead link that still compiles,
whereas `Routes.<Page>()` becomes a compile error you fix immediately. The analyzer flags a string literal
passed to internal navigation — `Go.To("…")` or any `RouteUrl` slot (`NavLink.Href(…)`,
`Ui.NavItem.Href(…)`, via the `string → RouteUrl` implicit conversion) — **only** when
the path maps to a generated parameterless route helper.

It deliberately leaves alone:
- **External URLs** — `https://…`, or anything wrapped in `RouteUrl.External("…")`.
- **Parameterised routes** — `/users/42` needs `Routes.UserPage("42")`, which can't be reconstructed from a
  bare literal.
- **Secondary `[Route]` templates** — the helper formats a page's *first* template only, so a literal like
  `/todos/new` on a page whose primary route is `todos` has no `Routes.*()` equivalent and is not flagged.

```csharp
[Route("todos")] public sealed partial class TodosPage : Component { /* … */ }

Go.To("/todos");                    // ✗ RASK033 — use Routes.TodosPage()
NavLink.Href("/todos")["Todos"];    // ✗ RASK033 — string → RouteUrl conversion

Routes.TodosPage().Go();             // ✓ type-safe; a renamed route is a compile error
Go.To("/todos/new");                // ✓ secondary template — no helper, left alone
A.Href("https://example.com")["Docs"];   // ✓ external — untouched
```

**Fix:** call the generated `Routes.<Page>()` (with arguments for any route/query params). For a genuinely
dynamic or external target, use `RouteUrl.External("…")`, or suppress with `#pragma warning disable RASK033`
/ `.editorconfig` (`dotnet_diagnostic.RASK033.severity = none`).

## RASK035
**Background job type cannot be registered** · Warning

A type implementing `IJob` was found, but the generated registry can't map its stored
name to its CLR type — so it is skipped. Enqueuing it still writes a row; the processor then fails to
rehydrate it, records `No registered job type '…'`, and retries until `MaxAttempts` before dead-lettering.
Before this diagnostic existed the type was skipped **silently**, which is exactly what made the failure
hard to place: the job looked queued, then quietly stopped.

The reasons, all about reconstructing a runtime `Type.FullName` from a name the generated file can write:

| Shape | Why |
|-------|-----|
| **Generic** — `record Reindex<T> : IJob` | A closed generic's `FullName` carries assembly-qualified type arguments, so no static key matches. |
| **Nested in a generic** — `class Outer<T> { record Job : IJob; }` | Naming it would leak `T` into the generated file. |
| **`file`-local** — `file record Job : IJob;` | Invisible outside its own file, and its `FullName` carries a synthesized `<file>F0__` segment. |
| **Inaccessible** — `private`/`protected` at any level of its containing chain | The generated registry lives in the same assembly but a different file, so it can't name the type. |

An **abstract** base carrying the marker is skipped without a warning — modelling a hierarchy that way is
normal, and its concrete derivatives register as usual.

```csharp
public class Outer<T>
{
    public sealed record Reindex(int Id) : IJob;           // ✗ RASK035: nested inside the generic type 'Outer'
}

public static class SearchJobs
{
    public sealed record Reindex(int Id) : IJob;           // ✓ nested in a non-generic type is fine
}
```

**Fix:** move the type out of the generic (or `file`-local, or inaccessible) declaration, and make it
non-generic — nesting inside a plain `static class` is the usual way to keep jobs grouped. Suppress with
`#pragma warning disable RASK035` / `.editorconfig` (`dotnet_diagnostic.RASK035.severity = none`) only if
you never enqueue that type.

## RASK036
**A chain-entry host must be `partial`** · Warning

Rask's own components (`Div`, `Span`, …) get their entries from `Rask.Core.RaskMarkup`, which
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
would have come from, so without `partial` it gets no chain entries at all.

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

The framework's own tags reach a nested component by *inheritance*, where nesting is irrelevant. A
**referenced library's** entries can only be injected — so a nested component whose enclosing chain is
not `partial` would silently lose them, which is what this reports instead.

**Fix:** add `partial`. Suppress with `#pragma warning disable RASK036` / `.editorconfig`
(`dotnet_diagnostic.RASK036.severity = none`) if you never name a component unqualified inside that type.

## RASK037
**`using` alias is hidden by a chain entry** · Warning

Every component type contributes a chain **entry** — a member named after itself,
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
**Chain does not set a required property** · Error

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

Order does not matter, and child indexing (`Card.Title("Q3")[…]`) is part of the same expression.

Properties **declared in your own compilation** are read straight off the syntax, where the member
initializer is right there. A property from a **referenced assembly** cannot be: an initializer
compiles into the constructor and leaves no trace in metadata, so `string Title` and
`string Title = ""` are the same symbol from outside. The owning assembly therefore publishes the
answer — the chain generator emits one
`[assembly: RaskRequiredProperties("Lib.Card", "Title")]` per component with such a
property — and this analyzer reads it back. A library built by an older Rask, or by no Rask at all,
publishes nothing, and its properties are then counted only when they carry the language's `required`
modifier, which metadata does preserve.

**Fix:** add the setter to the chain, or — if the property really is optional — give it a nullable
type or a member initializer, which is what marks it optional for both surfaces. Suppress with
`#pragma warning disable RASK038` / `.editorconfig` (`dotnet_diagnostic.RASK038.severity = none`).

## RASK039
**Chain is split across statements, so its required properties can't be checked** · Warning

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
**Two components share a simple name, so neither can have a chain entry** · Warning

A member name has no namespace, so the two do not separate the way the types themselves do. An
entry is keyed by **simple name**: it is a single member named after its type (an element's after its tag), and one name can only
stand for one type.

```csharp
namespace Features.Products { public sealed partial class Card : Component { } }   // ✗ RASK040
namespace Features.Orders   { public sealed partial class Card : Component { } }   // ✗ RASK040
```

Neither component gets an entry, because choosing which type `Card` means is the author's decision,
not the generator's. Both types still compile and nothing else breaks — you just cannot write
`Card` bare.

**Fix:** rename one of them (`ProductCard` / `OrderCard`). Suppress with
`#pragma warning disable RASK040` / `.editorconfig` (`dotnet_diagnostic.RASK040.severity = none`) if
you are happy to build both with `new` from inside Rask.Core.

## RASK041
**The chain surface's shared pending-bit budget is exhausted** · Warning

This one is for people *changing Rask itself*, not for app code. A chain writes only the properties
it names, so a chain entry marks its folding properties **pending** and resets whatever is still
pending when the parent's `Render()` returns — that is what makes `Div.Id("x")` on one render and a
bare `Div` on the next drop the `id`. The pending bits are split so a
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

## RASK043
**A component name is used in a type that has no chain entries** · Warning

The chain is reachable only from **inside a type that has the entries**. They are *inherited members* —
that is the whole design, because a static-imported property loses to a same-named type (CS0119) while
a member of the enclosing type wins. A component is such a type; so is anything deriving from
**`Rask.Core.RaskMarkup`**, which is `Component`'s own base and carries the framework entries and
nothing else; and so is anything marked **`[RaskMarkup]`**, which is the same opt-in for a type that
has no base slot to spend.

In a type that is none of those, the simple name binds to the component **type** instead (CS0119) — or, for an
element, whose type is named after its DOM interface (`HTMLDivElement`), to nothing at all (CS0103). Neither
compiler error says why; RASK043 does:

```csharp
internal static class Parts
{
    public static Component Loading() => Div.Class("spinner")["…"];   // ✗ RASK043 — CS0103
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

## CS0108 (a member hides a chain entry)

Not a Rask diagnostic, and — since RASKSUP001 — not something you have to answer. Because every
component contributes an entry named after itself, and the HTML/SVG tags land on `RaskMarkup` which
every component inherits, an ordinary member that happens to share a tag's name **hides** one:

```csharp
public sealed partial class ConfirmDialog : Component
{
    public Component? Footer { get; set; }        // vs the <footer> entry
    private Component Section(string t) => …;     // vs the <section> entry
    public sealed record Line(int X, int Y);      // vs the SVG <line> entry
    public required string Label { get; set; }    // vs the <label> entry
}
```

None of these needs a `new`. Inside that component the name is its own member; the element is still one word
away — **`Markup.Footer`**, `Markup.Label` — because every tag is also a static member of `Rask.Markup`.
**`RASKSUP001` suppresses CS0108 whenever the hidden member is a chain
entry** — a member named after the component it builds, declared on the markup surface. There are
about 170 such names (`Title`, `Label`, `Form`, `Data`, `Filter`, `Marker`, `Address`, `B`…), and a
framework should not spend a keyword of your source per accidental collision with one of them.

The suppression is deliberately narrow, so the warning keeps its meaning:

```csharp
public class Panel : Component { public int Count => 1; }
public class Wide : Panel { public int Count => 2; }   // ✗ CS0108 still fires — Count is nobody's tag
```

Hiding a real member of your own base type is an ordinary hiding mistake and still warns, inside a
component or outside one. Only entries are silenced. Generic components (`HTMLFormElement<T>`, `HTMLSelectElement<T>`,
`HTMLInputElement<T>`) open the chain through a `RaskSeed_*` field rather than a member typed as the component; both
shapes are recognised.

> **`dotnet format` does not honour `DiagnosticSuppressor`s.** It surfaces the diagnostic itself and
> fails on any warning-severity report, so a gate built on it sees CS0108 even where the compiler has
> already agreed to ignore it — measured on this repository: zero from the Release build, 200 from the
> same tree's format verify pass. Rask's own `.editorconfig` therefore sets
> `dotnet_diagnostic.CS0108.severity = none` for its own source. **Your project needs no such setting**:
> RASKSUP001 is what answers CS0108 for you, and it keeps the genuine case. Set the same severity only
> if you run `dotnet format` under warnings-as-errors yourself. This also replaces the old CS0108
> quick-fix, which inserted the `new` for you and is now removed.

The related `using`-alias collision cannot be fixed this way — it surfaces as a hard CS1061 after the
alias has already lost the lookup, which is what [RASK037](#rask037) exists for.

---

## RASK044
**Chain sets the same property twice** · Warning

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

It has to be an analyzer rather than a property of the type. A chain's receiver is the component itself,
which is what keeps the chain out of the way at every call site that wants the component (a property
typed as a particular component, a strongly-typed children collection, a test asserting on the result).
So the result is an ordinary component with ordinary settable properties, and nothing in the type system
is left to forbid the write.

**Fix:** move the assignment into the chain — every property a chain can reach has a step of the same
name. Suppress with `#pragma warning disable RASK045` / `.editorconfig`
(`dotnet_diagnostic.RASK045.severity = none`) where a component genuinely has to be completed later,
or where the property setter itself is what is under test.

**Scope.** It reports a local whose initializer is a chain that named at least one step, including one
closed by the children indexer. An *unqualified* entry with no steps at all — `var c = Card;` — is not
reported: unqualified entries bind to a per-host forwarder, which carries nothing that distinguishes it
from a hand-written property returning a component, and guessing there would put a warning on correct
code. A chain that named a step is where the two answers actually disagree.

---

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
a JSON object's keys are strings. A `IRaskFile` is allowed only as a *direct* property of the message:
a file is addressed by its index in the multipart body, written where the property sits in the JSON, so
one nested inside a list or a sub-object would have nowhere to be addressed from and its bytes would be
dropped.

**Fix:** give the property a concrete, supported type — or, if the message is never sent anywhere,
mark it `[LocalOnly]`:

```csharp
// A job payload, a domain event, a command only another handler publishes: never on the wire, so
// never encoded, so free to carry whatever its handler finds convenient.
[LocalOnly]
public sealed record RebuildIndex(IComparer<string> Order) : ICommand;
```

`[LocalOnly]` on an **interface** marks every message implementing it, which is how `Rask.Jobs`'
`IJob` keeps a whole family of in-process messages out of the wire vocabulary at once.

> This diagnostic only fires in a project that references a remote transport (`Rask.Cqrs.Client` or
> `Rask.Cqrs.Server`). An app using `Rask.Cqrs` purely in-process generates no codecs, so none of these
> constraints apply to its messages.

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

**External component must be partial** · Error

A `ReactComponent` or `LitComponent` is completed by a second part of the class: its name, its module
and its props writer. Without `partial` there is nowhere to put any of it. A package declaration
(`Mui : ReactPackage`) is completed the same way — its components' entries, `Mui.Button`, are generated into it.

```csharp
// ✗ RASK056 — nothing can be generated into it
public sealed class Chart : ReactComponent { }

// ✓
public sealed partial class Chart : ReactComponent { }
```

Reported here, against the declaration, rather than left to the compiler: what it would otherwise
produce is three "does not implement inherited abstract member" errors naming `ComponentName`,
`Module` and `WriteProps`, none of which the author wrote or should have to know about.

## RASK057

**External component prop has no wire encoding** · Error

Props are serialized to JSON by generated code rather than by reflection — which is what lets them
survive trimming and AOT — so a shape the generator cannot express has to be reported now rather than
arriving as `null` in the browser.

The supported set is the same wire vocabulary [RASK053](#rask053) documents, plus callbacks as
`Action`, `Action<T>`, `Func<Task>` and `Func<T, Task>`.

```csharp
// ✗ RASK057 — an interface names no single concrete type
public IFilter? Filter { get; set; }

// ✓ a concrete type has one shape
public TextFilter? Filter { get; set; }
```

A property that is not meant to reach the browser at all — an injected service, a computed helper —
should say so with `[SkipFactory]`, which keeps it out of the props entirely.

## RASK058

**External component name collision** · Error

The client runtime resolves a module by the component's simple type name, so two sharing one name
would resolve to whichever registered last — silently, and potentially differently between builds.

```csharp
// ✗ RASK058 — both are "Chart" to the browser
namespace Sales    { public sealed partial class Chart : ReactComponent { } }
namespace Support  { public sealed partial class Chart : ReactComponent { } }
```

Rename one, or give it an explicit module by overriding `Module`.

## RASK059

**Module or Export override must be a constant string** · Error

The bundler needs the module specifier at *build* time, to generate the entry that pairs the component
with its adapter — long before any of this code runs. So the override has to be a literal the
generator can read straight out of the syntax. The same holds for a package island's `Export`, which names the
component the entry imports, and for a package declaration's `Exports`, which must be a list of string literals
(`=> ["Button", "Card"]`).

```csharp
// ✗ RASK059 — the build cannot evaluate this
protected override string Module => $"./widgets/{Name}.ts";
protected override string Export => nameof(HexColorPicker);

// ✓
protected override string Module => "react-colorful";
protected override string Export => "HexColorPicker";
```

Anything computed would leave the browser resolving a name the bundle never built — markup pointing at
a chunk that does not exist, with nothing failing until someone loads the page.

## RASK060

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

## RASK061

**Blazor island must be partial** · Error

A `BlazorComponent<T>` is completed by a second part of the class — the chain steps taken from the
hosted component's `[Parameter]`s, and the reflection-free writer that maps them onto it. Without
`partial` there is nowhere to put any of it.

```csharp
// ✗ RASK061 — nothing can be generated into it
public sealed class Chart : BlazorComponent<MudChart> { }

// ✓
public sealed partial class Chart : BlazorComponent<MudChart> { }
```

Reported against the declaration rather than left to the compiler, which would otherwise report a
missing `WriteParameters` the author never wrote and should not have to know about.

## RASK062

**An island cannot render these children** · Error

An island renders what a foreign renderer owns, so what it can take as children depends on the
renderer.

**A Blazor island takes none.** It is a leaf:

```csharp
// ✗ RASK062 — 'Chart' is a Blazor island and cannot take Rask children
Chart.Series(_series)[ H2["Revenue"] ]

// ✓ compose the other way round
Div.Class("rounded-xl border p-4")[ H2["Revenue"], Chart.Series(_series) ]
```

Rask children would have to cross the diff boundary, and no crossing is right for every hosted
component: it may have no `RenderFragment` parameter at all, one under a name only it knows (`Content`,
`Body`), or several (`HeaderContent`, `RowTemplate`).

**A JS island takes children of its own runtime** — a React island accepts React islands, text,
numbers and dates — because they travel inside its props and React renders them. Rask markup, or
another runtime's island, cannot: once a front-end framework has moved Rask's nodes into its own tree,
every DOM path Rask holds into them is wrong and the content goes dead after its first paint.

```csharp
// ✗ RASK062 — 'MuiCard' is a React island; its children are React islands, text, numbers and dates
MuiCard[ Span["Revenue"] ]
MuiCard[ VueChip.Label("new") ]

// ✓
MuiCard[ "Revenue ", MuiChip.Label("new") ]
Div[ MuiCard["Revenue"], Span["updated today"] ]
```

A package island whose component takes no content accepts nothing:
`'MuiIcon' is a React island that takes no children`.

The compiler refuses these on its own — an island's children indexers are typed to its runtime, and
the ones it inherits return a type that cannot become a component — but it says so late, where the
result fails to convert, and not at all for `var x = MuiCard[Span["x"]]`. RASK062 reports the call at
its brackets instead.

**Assigning `Children`** is reported on both families: an island never renders it, so the children
would compile and silently never appear. Pass them through the indexer.

See [islands](islands.md#children) and [Blazor
components](blazor-components.md#an-island-takes-no-children).

## RASK064

**Blazor island name collision** · Error

An island's simple name identifies it in the rendered markup (`<rask-blazor name="Chart">`), so two
islands sharing one is ambiguous in the page and in anything reading it.

```csharp
// ✗ RASK064 — both are "Chart"
namespace Dashboard { public sealed partial class Chart : BlazorComponent<MudChart> { } }
namespace Reports   { public sealed partial class Chart : BlazorComponent<MudChart> { } }
```

**Fix:** rename one. The namespace does not disambiguate, because the name is written into HTML.

## RASK066

**Hosted Blazor component's parameters cannot be verified** · Warning

The island's chain steps are read from the hosted component's `[Parameter]` properties. That works
for a type from a **referenced** project or package, but not for a `.razor` in the *same* project:
that class is produced by the Razor source generator, and one source generator never sees another's
output — so Rask's generator cannot resolve its parameters and emits no chain steps for them.

The island still renders. What is lost is compile-time checking of what you pass it.

```csharp
// ⚠ RASK066 — Widget.razor is in this project
public sealed partial class WidgetIsland : BlazorComponent<Widget> { }
```

**Fix:** move the `.razor` into a Razor Class Library and reference it. That is also where a Blazor
component belongs if anything else will ever use it — and it is why hosting MudBlazor or Radzen,
which are referenced packages, is fully checked with no warning at all.

---

## RASK067

**Endpoint shape has no wire encoding** · Error

A parameter or response type of an API endpoint cannot be encoded on the wire, so no typed client
method can call it. The message names the shape and says what is wrong with it — the same walk, and
the same vocabulary, as [RASK053](#rask053) for a CQRS message.

```csharp
[HttpPost("")]
public ActionResult<Post> Create([FromBody] Stream body) => ...;   // ✗ RASK067
```

**Fix:** take a record, a class or a collection of them. A type Rask can encode is one whose public
properties are themselves encodable — the shapes a JSON body can carry. If the parameter is really a
service rather than something the caller sends, mark it `[FromServices]`, and it leaves the client's
signature instead of being reported.

---

## RASK068

**Endpoint has no generated client method** · Warning

The endpoint works over plain HTTP; it just gets no typed caller, because something about it cannot be
expressed as a method signature. The message says which:

- its route has a **catch-all** segment (`{*rest}`), which no single parameter can fill;
- its route names a token **no parameter supplies**, so the client would build a URL with a hole in it;
- it would take a **second request body**, and a request has one.

```csharp
[HttpGet("files/{*path}")]                    // ⚠ RASK068 — catch-all
public ActionResult<string> Read(string path) => ...;

[HttpGet("{id:int}")]                         // ⚠ RASK068 — nothing supplies {id}
public ActionResult<Post> Get() => ...;
```

A warning rather than an error, because one unusual endpoint should not break a build. It is reported
rather than skipped in silence for the opposite reason: a method simply missing from the client reads
as a broken generator, and the author has no way to find out why.

**Fix:** give the route a normal parameter segment, add the parameter it names, or accept that this
endpoint is called by hand.

---

## RASK069

**Two endpoints claim one client method** · Error

Two actions on the same controller generate the same client method name — an overload the generator
cannot distinguish, and a CS0111 inside generated code if it were emitted.

```csharp
[HttpGet("{id:int}")] public ActionResult<Post> Get(int id) => ...;
[HttpGet("by-slug/{slug}")] public ActionResult<Post> Get(string slug) => ...;   // ✗ RASK069
```

**Fix:** rename one of the actions. The client method takes the action's own name, so naming them for
what they do — `Get` and `GetBySlug` — fixes the client and reads better on the server too.

---

## RASK070

**Endpoint's response type is not statically known** · Warning

The action returns `IActionResult`, `ActionResult` or `IResult`, so what it answers with is decided at
run time and there is no type for a client method to return.

```csharp
[HttpGet("{id:int}")]
public IActionResult Get(int id) => Ok(new Post(id));   // ⚠ RASK070
```

**Fix:** return the type, and let ASP.NET wrap it — `ActionResult<Post>` still lets you
`return NotFound()`. Where the return type genuinely has to stay open, declare what the success case
sends and the client is generated from that:

```csharp
[HttpGet("{id:int}")]
[ProducesResponseType(typeof(Post), 200)]
public IActionResult Get(int id) => ...;               // typed client method
```

An action that answers **nothing** does not need either: return `Task` or `void` and the client method
returns a bare `Task`. An endpoint that is not part of the app's API — plumbing its own pages call — is
taken out of the client with `.ExcludeFromDescription()`, the same call that keeps it out of OpenAPI, and
this diagnostic does not fire for it.

---
## RASK071

**ASP.NET route attribute on a Rask component** · Error

Rask's route attribute and ASP.NET's share the short name `Route` and differ only by namespace. In a
server project that already has `using Microsoft.AspNetCore.Mvc;` — or in a file written by someone
arriving from Blazor, where the attribute is `Microsoft.AspNetCore.Components.RouteAttribute` — the
wrong one is one completion away, and nothing downstream notices:

- MVC reads its attribute only while building the controller application model, and a `Component` is
  never scanned.
- Blazor's is read by a renderer this framework does not run.
- Rask's `RoutesGenerator` matches on the full name, so it sees nothing to register.

The build is green and the page is simply absent from the route table. The first sign of it is a 404
in a browser, which is why this is an Error rather than a warning you could scroll past.

```csharp
using Rask.Core;
using Microsoft.AspNetCore.Mvc;

// ✗ RASK071 — binds to MVC's attribute; this page is never registered
[Route("/orders")]
public sealed partial class Orders : Component
{
    protected override Component? Render() => Div["orders"];
}
```

**Fix:** apply `Rask.Core.Routing.RouteAttribute` instead (**quick-fix available** — "Use Rask's
`[Route]`").

The quick-fix rewrites only the attribute's **name**. It adds no `using` and removes none, and it
writes the name qualified wherever the short form would bind back to ASP.NET's attribute or be
ambiguous — so on the file above, with `using Microsoft.AspNetCore.Mvc;` still present, the lightbulb
produces:

```csharp
[Rask.Core.Routing.Route("/orders")]       // qualified: a bare `Route` would still be MVC's
```

Tidying the imports by hand gives the shorter spelling:

```csharp
using Rask.Core;
using Rask.Core.Routing;                   // MVC's import dropped

[Route("/orders")]                         // Rask's — the page registers
public sealed partial class Orders : Component
{
    protected override Component? Render() => Div["orders"];
}
```

The lightbulb is **withheld** where carrying the arguments over would not compile: MVC's attribute
also has settable `Name` and `Order`, which Rask's does not, and an alias may bake its template in
and take no arguments at all. Rewriting those would answer RASK071 with a CS0117 or CS7036, so the
attribute is left for you to move over deliberately.

A page carrying **both** attributes is left alone. It registers correctly through Rask's, so the
ASP.NET one is inert rather than harmful, and failing a build that is producing the right route
table would be the worse outcome.

This does not fire on ordinary classes. A Rask server project is an ASP.NET project and may hold
genuine controllers; `[Route]` on one of those is correct and is never reported.

---
## RASK072

**Entity `Configure` method will not be called** · Warning

An entity maps itself: Rask's model generator calls a static `Configure` on every `Entity<TId>` that
declares one, for the rules that are that entity's own. The method is matched **by signature**, not by
name alone — so one that is an instance method, is private, or takes something other than
`EntityTypeBuilder<TSelf>` is simply not found.

Without this warning the build stays green, the table is created from conventions alone, and the
missing index or length turns up in production.

```csharp
public sealed class Product : Aggregate<Guid>
{
    public string Sku { get; private set; } = "";

    // ✗ RASK072 — an instance method; the generator emits `Product.Configure(...)`
    public void Configure(EntityTypeBuilder<Product> builder) =>
        builder.HasIndex(p => p.Sku).IsUnique();
}
```

**Fix:** declare it exactly as the generator calls it — `public static`, returning `void`, taking this
entity's own builder:

```csharp
public static void Configure(EntityTypeBuilder<Product> builder) =>
    builder.HasIndex(p => p.Sku).IsUnique();
```

`internal static` works too — the generated registry is emitted into the same assembly. The type
argument must be the entity itself: `EntityTypeBuilder<SomethingElse>` configures another table and is
reported rather than called.

An entity with no `Configure` at all is not reported. It is mapped by convention, which is the common
case and the intended one.

---

## RASK073

**Strongly-typed id has no usable value** · Warning

A strongly-typed id — `Aggregate<ProductId>` rather than `Aggregate<Guid>` — is stored as its underlying value
through a generated `ValueConverter`. Building one needs two things the generator can see: a single
public property holding the value, and a public constructor taking that value back.

```csharp
// ✗ RASK073 — two public properties, so which one is the stored value is ambiguous
public readonly record struct ProductId(Guid Value, string Label);
```

**Fix:** give the id one value and a matching constructor. A positional record struct is the shortest
form and gives value equality for free:

```csharp
public readonly record struct ProductId(Guid Value);
```

The conversion is then registered once for the type, in `ConfigureConventions`, so **every** property
of that type is converted — the key, a foreign key on another entity, and a nullable one — without any
of them being named individually.

This is a Warning rather than an Error because the rest of the assembly still builds, but the model
does not: EF Core refuses a key type it cannot map, and its own message names the property rather than
the reason. That is what this replaces.

Ids that need no converter are not reported. Anything the provider already maps — `Guid`, `int`,
`long`, `string` — is left alone.

---

## RASK074

**More than one user type** · Warning

Rask ships no user class. The user type is the one class in your project deriving from `Rask.Auth.Authenticatable`,
found at compile time and named to the accounts by a generated `[ModuleInitializer]` — which is what lets
`AddRaskAuth()` and `modelBuilder.AddRaskAuth()` take no type argument.

That needs there to be *one*. With two, picking either would map one users table and silently strand the other,
and the app would look wired until the first sign-in.

```csharp
public sealed class User : Authenticatable { }

// ✗ RASK074 — which of these is the user?
public sealed class LegacyUser : Authenticatable { }
```

**Fix:** keep one. A second user-shaped type does not need to derive from `Authenticatable` to be mapped — it is an
aggregate like any other, and if it is genuinely a second account store it belongs behind its own
`AddRaskAuth<TContext, TUser>()` call rather than the convention.

Auth is left unwired when this fires, rather than half-wired against a guess.

Declaring **no** user type is not reported: an app with no accounts is a legitimate app, and the auth
battery simply does not wire.

---

## RASK076

**Grid column with no field token** · Warning

A `Ui.DataGrid` identifies a column by the **field token** it was opened with. `c.Field(...)` always has
one; `c.Column()` deliberately has none, which is exactly right for an actions column or one computed
from the whole row.

The token is also the only name the column chooser, the group panel, `HiddenColumns`, `ColumnOrder` and
`Grouped` have for a column. So a token-less column under any of those can be **shown and never hidden,
moved or grouped**: the menu simply has no row for it. Nothing throws and nothing is logged — the reader
just looks for a control that was never rendered, which reads as a bug in the grid rather than in the
call site.

```csharp
Ui.DataGrid.Data(rows).RowKey(r => r.Id)
    .ColumnChooser(true)[c => [
        c.Field(r => r.Name).Title("Package"),
        c.Column().Title("Actions"),          // ⚠ RASK076 — no token, so the chooser cannot list it
    ]]
```

**Fix:** give the column a token, or say that it is deliberately fixed.

```csharp
c.Field(r => r.Actions).Title("Actions"),                    // ✓ has a token

c.Column().Title("Actions").Hideable(false).Reorderable(false),   // ✓ states the intent instead
```

Which opt-out silences it depends on which axes the grid turned on: `Hideable(false)` for the chooser
and `HiddenColumns`, `Reorderable(false)` for `ColumnOrder`, `Groupable(false)` for the group panel and
`Grouped`. A **column chooser drives both hiding and reordering** — the grid's own `ReorderEnabled` reads
`ColumnChooser is true || OrderControlled` — so a column under a chooser needs both of the first two.

A grid with none of those features on is not reported: a token-less column is completely ordinary there,
and that is the common case.

This is the successor to the retired **RASK034**, which said the same thing about `BsDataGrid`. Worth
knowing that RASK034 stopped firing when the grid moved to a chain and nothing noticed, so this one is
tested on the shape it has to catch rather than only on compiling.

---

## RASK077

**Package island has no props snapshot** · Warning

An island whose `Module` names an npm package — a *package island* — gets its props from the package's own
TypeScript, read from `{Island}.props.json` beside the class. That file is committed like a lockfile, so a fresh
clone, the IDE and a build without Node all see every step. This island has neither a snapshot beside it nor a
prop declared by hand, so there is nothing it can be told.

```csharp
// Features/Shop/MuiButton.cs
public sealed partial class MuiButton : ReactComponent
{
    // ⚠ RASK077 — no MuiButton.props.json beside this file
    protected override string Module => "@mui/material/Button";
}
```

Add `MuiButton.props.json` beside the class and commit it, or declare the props on the island in C#. An island
that declares its props by hand is not reported — see
[Using a package component directly](islands.md#using-a-package-component-directly).

## RASK078

**Props snapshot cannot be read** · Error

The snapshot is not one this Rask.External can read: malformed JSON, a required field missing, or a `"schema"`
newer than it understands. The diagnostic points at the line it breaks on, and nothing is generated for the
island rather than a partial set of steps.

A snapshot describes a package; it is not somewhere to hand-tune the island. Re-extract it, or update
Rask.External when the schema is newer than it reads.

## RASK079

**Props snapshot describes a different component** · Error

A snapshot records the runtime, the module and the export it was extracted for. The class beside it now names
another:

```csharp
public sealed partial class MuiButton : ReactComponent
{
    // ✗ RASK079 — MuiButton.props.json was extracted for the default export of '@mui/material/Button'
    protected override string Module => "@mui/material";
    protected override string Export => "Button";
}
```

The default export of `@mui/material/Button` and the `Button` export of `@mui/material` are different imports, and
the base class decides whose types were read, so the props cannot be assumed to match. Re-extract the snapshot, or
put the base class, `Module` and `Export` back to what it was taken from.

## RASK080

**Package prop was not generated** · Warning

A prop in the snapshot has no C# type Rask can generate for it, or its C# name collides with one the island
already has. The other props are generated as usual; this one gets no chain step.

| TypeScript | Why it is left out |
|---|---|
| `string \| boolean`, any union of unrelated types | no single C# type — only `string \| number` has one |
| a callback that returns a value | the package needs the value synchronously, and a call into C# is asynchronous |
| an object with no declared members, a function nested in a value | no JSON encoding |

Declare the property on the island yourself, with the type you want, and it is sent under the package's own name:

```csharp
public sealed partial class MuiToggle : ReactComponent
{
    protected override string Module => "@mui/material/ToggleButton";

    /// <summary>
    ///     The package takes <c>string | boolean</c>; this app only ever passes a string.
    /// </summary>
    public string? Value { get; set; }
}
```

## RASK082

**A type already has the generated model's name** · Error

Every aggregate gets a generated form model emitted beside it, in the same namespace, as `{Aggregate}Model`. A
hand-written, non-`partial` type of that name, often a request model written before the generator existed,
would collide with it as `CS0101`, a message that names neither the generator nor the way out. So the
generator stands down for that aggregate and says why: no `ProductModel` is generated, and neither are the
`Create`, `Update` and `Delete` that take it.

```csharp
public sealed class Product : Aggregate<Guid> { /* … */ }

public sealed class ProductModel                  // ✗ RASK082: the generated model's name
{
    public string Name { get; set; } = "";
}
```

**Fix:** rename the hand-written type, or make it `partial` so it extends the generated one.

```csharp
public sealed class ProductForm { /* … */ }       // ✓ rename it and keep both

public sealed partial class ProductModel          // ✓ extend the generated one instead
{
    public string Slug => (Name ?? "").ToLowerInvariant().Replace(' ', '-');
}
```

A `partial` declaration is not reported: it merges into the generated class, which is the supported way
to add members, interfaces such as `IValidatableObject`, or computed display values to a model. Every
generated property is nullable, so a member you add reads them as such.

---

## RASK083

**Nested entity gets no generated model** · Warning

The generated model is a sibling of the aggregate in its namespace. An aggregate declared inside another
type has no such place to put it, so it is still mapped (it gets its table like any other) but no form
model is generated for it.

```csharp
public static class Catalog
{
    public sealed class Product : Aggregate<Guid> { }  // ⚠ RASK083: nested in Catalog
}
```

**Fix:** declare the aggregate at namespace level:

```csharp
namespace Shop.Catalog;

public sealed class Product : Aggregate<Guid> { }      // ✓ gets ProductModel
```

---

## RASK084

**Model state can be changed from outside the type** · Error

An aggregate, an entity and every value object they hold change only through their own methods, so their
rules and their domain events stay in one place. A public setter lets any caller skip those methods. Nothing
in Rask needs one: EF Core materialises through private setters, and the generated form model writes through
them too (`Product.Create(model)`, `Product.Update(id, model)`). So it is an error, not a hint.

Checked: every class deriving from `Rask.Data.Entity<TId>` (and so every `Aggregate<TId>`), including your own
abstract bases between them, and every value object one of them holds. A value object carries no marker: it
is any composite (a class, record or struct) that an entity holds and that is not itself an entity, so it is
found through the entity that holds it.

Reported, at the accessor or the field:

- a property `set` accessor that is public (`{ get; set; }` on a public property);
- a public `init` accessor you wrote yourself;
- a public instance field that is neither `readonly` nor `const`.

```csharp
public sealed class Product : Aggregate<Guid>
{
    public string Name { get; set; } = "";            // ✗ RASK084: 'Product.Name' has a public setter
    public int Stock;                                  // ✗ RASK084: a public field that is not readonly
    public Address Warehouse { get; private set; } = new();
}

public sealed record Address
{
    public string City { get; init; } = "";           // ✗ RASK084: a public init accessor
}
```

**Fix:** make the member private, change the state through the type's own methods, and create it in a
static factory (**quick-fix available**: `set` → `private set`, `init` → `private init`, a public field →
`private`):

```csharp
public sealed class Product : Aggregate<Guid>
{
    public string Name { get; private set; } = "";    // ✓
    public int Stock { get; private set; }             // ✓

    public static Product Create(string name) => new() { Name = name };

    public void Rename(string name) => Name = name;    // ✓ the aggregate changes itself
}
```

`private`, `protected` and `internal` accessors are all fine, and `protected set` is the natural choice on an
abstract base whose derived entities write the property. Members inherited from `Entity<TId>` and
`Aggregate<TId>` (`Id`, `CreatedAt`, `UpdatedAt`, `Version`, `DeletedAt`) are never reported, and neither is
an `override`: it cannot narrow what it overrides, so the base declaration is where it is reported.

**Positional records are exempt.** The properties a positional record parameter declares get public
`init` accessors from the compiler, and that is exactly the immutable value object:

```csharp
public sealed record Money(decimal Amount, string Currency);          // ✓ not reported
public readonly record struct Weight(decimal Grams);                  // ✓ not reported
public record struct Height(decimal Centimetres);                     // ✗ RASK084: a real set
```

A positional parameter of a record struct that is not `readonly` gets a real `set`, so it is reported at
the parameter; declare it `readonly record struct`. The quick-fix is not offered where `private` would not
compile (a `required` or `abstract` property, an accessor whose sibling already has a modifier such as
`{ private get; set; }`, or a setter an interface you implement demands), and the error stays for you to
resolve by hand.

---

## RASK085

**Entity exposes a mutable collection of entities** · Warning

A private setter does not protect a list: `order.Lines.Add(line)` changes the order without calling any
of its methods. Reported for a publicly readable property of an entity whose type is a mutable collection
(it implements `ICollection<T>`: `List<T>`, `IList<T>`, `ICollection<T>`, `HashSet<T>`, `ISet<T>`,
`Collection<T>`) of other **entities**. A collection of strings or of value objects is not a navigation
and is not reported; neither are `ReadOnlyCollection<T>` and the immutable collections.

```csharp
public sealed class Order : Aggregate<Guid>
{
    public List<OrderLine> Lines { get; private set; } = new();   // ⚠ RASK085
}

public sealed class OrderLine : Entity<Guid> { /* … */ }
```

**Fix:** keep the collection in a private readonly field, expose `IReadOnlyCollection<T>`,
`IReadOnlyList<T>` or `IEnumerable<T>`, and add to it through the aggregate (**quick-fix available**: it
writes the field and the read-only property, and points this instance's own references (`Lines`, `this.Lines`
in an instance member) at the field. An access through a lambda parameter such as `Configure`'s
`b.HasMany(o => o.Lines)`, another instance's `other.Lines` and `nameof(Lines)` keep naming the property;
references outside the type are yours to move onto a method):

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyCollection<OrderLine> Lines => _lines;         // ✓

    public void Add(OrderLine line) => _lines.Add(line);           // ✓ the order changes itself
}
```

EF Core maps this with no configuration: it finds the `_lines` backing field by naming convention and
reads and writes the navigation through it, so `Include(o => o.Lines)` and lines added through `Add` both
round-trip. Keep the field named `_` plus the camel-cased property name.

The quick-fix keeps a `HashSet<T>` as a `HashSet<T>` field and uses `List<T>` otherwise. It is not offered
when the rewrite could not keep the meaning: an initializer with elements or arguments, a property with
accessor bodies, a `required` property, a property assigned inside the type (the field is `readonly`), a
type split across several files, or a member that already has the field's name.

---

## RASK086

**Aggregate has no parameterless constructor, so `Create` is not generated** · Warning

Every `Aggregate<TId>` gets a generated form model (`ProductModel` for `Product`) and the writes that take it
([data guide](data.md#writing-create-update-delete)). `Product.Create(model)` and
`Product.Create(p => …)` both start from a new, empty aggregate, and so does `new ProductModel()`, which
holds the aggregate's own defaults. That needs a constructor that takes nothing, and an aggregate that declares
no constructor has one for free. Declaring one that takes arguments removes it.

```csharp
public sealed class Product : Aggregate<Guid>
{
    public Product(string name) => Name = name;   // ⚠ RASK086: the only constructor takes a name

    public string Name { get; private set; }
}
```

**Fix:** declare no constructor, and put domain creation in a static factory:

```csharp
public sealed class Product : Aggregate<Guid>
{
    public string Name { get; private set; } = "";

    public static Product Create(string name) => new() { Name = name };   // ✓
}
```

An aggregate built by its factory is inserted with `Product.Create(Product.Create("Anvil"))`. Until the
constructor goes, everything that works on a row that already exists is still generated: `ProductModel`,
`Product.Update(id, model)`, `Product.Update(id, p => …)` and `Product.Delete(id)`.

This rule was RASK081 before the generated writes were dropped and brought back; a retired id is never
recycled, so it returned under a new one.

---

## RASK087

**Aggregate reaches across a boundary instead of holding an id** · Error

An aggregate is a consistency boundary, and **the border is what one aggregate can see of another**. An
aggregate may hold another's *id* and nothing else: that is what makes crossing a boundary by accident
impossible, because there is nothing to walk.

A property whose type is another **`Aggregate<TId>`** — one of them, or a collection of them — is that border
failing. That type has its own version, its own soft delete and its own reads and writes, so a form post on
this one must never add to it and must never delete from it.

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<Shipment> _shipments = [];

    public Customer Customer { get; private set; }                  // ✗ RASK087: Customer is an Aggregate
    public IReadOnlyCollection<Shipment> Shipments => _shipments;   // ✗ RASK087: Shipment is an Aggregate
}
```

**Fix, when it really is part of the order** — derive it from `Entity<TId>`, and it becomes a child that *is*
loaded, saved and deleted with its root ([data guide](data.md#children)):

```csharp
public sealed class OrderLine : Entity<Guid>   // ✓ a part: no version, no soft delete, no writes of its own
{
    public string Product { get; private set; } = "";
}
```

**Fix, when it is its own aggregate** — hold the id. The id is the reference, on whichever side owns it: a
single reference becomes a column on *this* aggregate, and a collection becomes a column on the *other* one.

```csharp
public sealed class Order : Aggregate<Guid>
{
    public Guid CustomerId { get; private set; }   // ✓ one customer: the id lives here
}

public sealed class Shipment : Aggregate<Guid>
{
    public Guid OrderId { get; private set; }      // ✓ many shipments: the id lives there
}
```

**Nothing is lost.** The join you wanted is on the read face, where there are no borders, and it is inferred
from exactly that id — so it is still one expression:

```csharp
await Order.Where(o => o.Customer.Country == "HU");
await Shipment.Where(s => s.Order.Status == OrderStatus.Open);
```

It is an error rather than a warning because the alternative is worse than either fix: a navigation that
*looks* like a child, renders like one, and silently is not saved with its parent — or, worse, one that a
`SaveChanges` drags across a boundary that other code owns.

---

## RASK088

**Child collection cannot be synced, so a save cannot add or remove one** · Warning

A child collection on a form model is editable: what the posted list holds is what the aggregate holds after
the save ([data guide](data.md#children)). Writing it needs something to add to and remove from — either the
property's own type is an `ICollection<T>`, or there is exactly one field behind it holding the children.

With neither, Rask would have to guess, so it generates the model without the sync: the children still render,
and a save keeps whatever was stored — which looks exactly like a form that did not submit.

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _paid = [];
    private readonly List<OrderLine> _unpaid = [];

    // ⚠ RASK088: not a writable collection, and two fields could be behind it
    public IEnumerable<OrderLine> Lines => _paid.Concat(_unpaid);
}
```

**Fix:** keep the children in one collection, and project in a separate member:

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _lines = [];

    public IReadOnlyCollection<OrderLine> Lines => _lines;                       // ✓ synced

    [NotMapped]
    public IEnumerable<OrderLine> Unpaid => _lines.Where(l => !l.Paid);          // ✓ a view, not a collection
}
```

Exposing the collection as `ICollection<OrderLine>` works too, at the cost of letting any caller add to it
without going through the aggregate.

---

## RASK089

**Id looks like a reference but no navigation was inferred** · Warning

An aggregate references another by id and never by navigation — that is the border, and it is what stops a
write crossing one by accident ([data guide](data.md)). The join you lose on the write side comes back on the
read side: Rask infers a navigation on the generated read face from each `{X}Id` whose type is the key of an
aggregate `X`, so `Order.CustomerId` gives you `OrderRead.Customer`.

Inference is by name and by key type, both. A property that matches an aggregate's name but not its key type,
or that matches two aggregates at once, produces no navigation at all — and the only symptom would be a join
the author expected and never got.

```csharp
public sealed class Customer : Aggregate<Guid> { }

public sealed class Order : Aggregate<Guid>
{
    public Guid CustomerId { get; private set; }   // ✓ OrderRead.Customer

    // ⚠ RASK089: names Customer, but Customer's key is Guid
    public int BillingCustomerId { get; private set; }
}
```

**Fix:** give the id the aggregate's own key type, so the navigation appears:

```csharp
public Guid BillingCustomerId { get; private set; }   // ✓ OrderRead.BillingCustomer
```

Or rename it, if it was never meant to point at a `Customer` — an id that matches no aggregate is an ordinary
column and says nothing:

```csharp
public int BillingReference { get; private set; }     // ✓ just an int
```

The second case is ambiguity: two aggregates whose names both end where the property does — `Customer` and
`KeyCustomer` against a `PrimeKeyCustomerId`. Rask refuses to guess; rename the property so one match is
longest.

---

## RASK090

**Two entities want one DbContext set name, so neither is generated** · Warning

Every mapped entity gets a named set on `DbContext` beside the `Set<T>()` that always worked — `db.Orders`,
`db.OrderLines` — from one documented rule:

| Type name ends with | Set name | Example |
|---|---|---|
| `s`, `x`, `z`, `ch`, `sh` | `+ es` | `Address` → `db.Addresses` |
| a consonant then `y` | `y` → `ies` | `Category` → `db.Categories` |
| anything else | `+ s` | `Order` → `db.Orders` |

Nothing cleverer, on purpose: an irregular-plural dictionary would be right more often and wrong
unpredictably, and a name you cannot guess from the type is worse than one you can. A `Person` becomes
`db.Persons` and a `Quiz` becomes `db.Quizes` — both wrong as English, both exactly what the table above
says, and that is the trade.

The rule cannot serve two entities that land on the same name, so Rask generates **neither** accessor rather
than picking a winner or inventing a name nobody could predict:

```csharp
namespace Shop      { public sealed class Order : Aggregate<Guid> { } }
namespace Warehouse { public sealed class Order : Aggregate<Guid> { } }   // ⚠ RASK090: both want db.Orders
```

The same applies to a name `DbContext` already declares — `db.Models`, say — because a member on the type
itself always wins over an extension member, so the accessor would compile and quietly mean something else.

**Fix:** rename one of the entities, or reach them explicitly, which is unambiguous and always available:

```csharp
db.Set<Shop.Order>()
db.Set<Warehouse.Order>()
```

---

## RASK091

**A child cannot choose its own form writes — its root decides** · Warning

[`ModelWrites`](data.md#turning-the-form-surface-off) narrows the form surface of an **aggregate**. A child
entity has no writes of its own to narrow — it is created, changed and removed through its root — and its
model is part of the root's, which is what a form actually posts.

```csharp
public sealed class Order : Aggregate<Guid>
{
    private readonly List<OrderLine> _lines = [];
    public IReadOnlyCollection<OrderLine> Lines => _lines;
}

public sealed class OrderLine : Entity<Guid>
{
    public const ModelWrites Writes = ModelWrites.None;   // ⚠ RASK091: ignored
}
```

The const is **reported and then ignored**, not half-obeyed: `OrderModel.Lines` is a
`List<OrderLineModel>`, so honouring it would leave the root's model holding a list of a type that was never
generated.

**Fix:** put the const on the aggregate that holds it, where it means something:

```csharp
public sealed class Order : Aggregate<Guid>
{
    public const ModelWrites Writes = ModelWrites.None;   // ✓ the order takes no form — nor do its lines
}
```

---

## RASKVAL001

**Two validators for the same model** · Error

A form asks for the validator of its model type and gets exactly one, so two
`AbstractValidator<T>` for the same `T` in one compilation is ambiguous. Which one ran would depend
on compilation order, and the rules in the other would silently never run — which reads as a
validator that does not work rather than one that was never reached.

```csharp
public sealed class OrderValidator     : AbstractValidator<Order> { }   // ✗ RASKVAL001
public sealed class OrderRulesValidator : AbstractValidator<Order> { }
```

**Fix:** combine the rules into one `AbstractValidator<Order>`. To switch between sets of rules, use
FluentValidation's own rule sets inside that single validator rather than two validator classes.

A `private` or `protected` validator is never discovered in the first place, so it does not collide —
declaring it that way is itself the statement that nothing outside constructs it, and it has to be
registered by hand with `RaskValidators.Register`.

---

## RASKVAL002

**Validator cannot be constructed automatically** · Warning

Registration builds the validator for you: a parameterless constructor is called directly, and one
taking parameters has each resolved from the render scope. Several public constructors leave no way
to choose, so nothing is generated.

```csharp
public sealed class OrderValidator : AbstractValidator<Order>   // ⚠ RASKVAL002
{
    public OrderValidator() { }
    public OrderValidator(IProductRepo repo) { }
}
```

**Fix:** leave one public constructor, or register it yourself:

```csharp
RaskValidators.Register(typeof(Order), sp => new OrderValidator(Pick(sp)));
```

This is a warning rather than an error because the validator is still usable by hand — but until it
is registered its rules never run, and a validator that silently does nothing is the failure worth
naming.

## RASK092

**A unit reads wrong for its count** · Warning · quick-fix

Durations and sizes are written the way they are said — `3.Seconds`, `1.Hour`, `50.Megabytes` — and every
whole-number unit has a singular and a plural with the same value. Both compile; only one reads.

```csharp
await Task.Delay(2.Second);        // ⚠ RASK092: '2.Second' reads wrong — write '2.Seconds'
o.MaxFileSize = 1.Megabytes;       // ⚠ RASK092: '1.Megabytes' reads wrong — write '1.Megabyte'
```

It looks only at a literal count: `count.Hours` has nothing to read, and a fraction (`1.5.Hours`) has no
singular.

**Fix:** take the lightbulb, which writes the form that matches the count:

```csharp
await Task.Delay(2.Seconds);
o.MaxFileSize = 1.Megabyte;
```

## RASK093

**An awaitable result is dropped** · Error · quick-fix

A call that hands back something awaitable has not run when it returns — awaiting it is what runs it.
Rask's timing steps are built this way, so nothing is queued, stored or written until the `await`:

```csharp
void Register(User user)
{
    Mail.Send(Email.To(user.Email).Subject("Welcome"));   // ❌ RASK093: does nothing
    Jobs.Enqueue(new SendOnboardingTips(user.Id)).In(24.Hours);   // ❌ RASK093: does nothing
}
```

Inside an `async` method the compiler already refuses this (CS4014, an error under
warnings-as-errors). This rule covers the half it leaves open: anywhere that is not async, where the same
line compiles clean, sends no mail, queues no job and reports nothing at all.

**The one that bites hardest is an event handler.** Every event prop takes either a plain `Action` or a
`Func<Task>`, so a lambda whose body is a builder binds to `Action` — the value is discarded, and the line
looks exactly like the one that works:

```csharp
Button.OnClick(() => Mail.Send(email))["Send"]          // ❌ RASK093: binds to Action, sends nothing
Button.OnClick(async () => await Mail.Send(email))["Send"]   // ✅ what you meant
```

It fires on any awaitable — Rask's builders, a `Task`, a `ValueTask`, one of your own — and on all three
shapes that drop a value:

```csharp
Mail.Send(email);                      // ❌ a statement
void Register() => Mail.Send(email);   // ❌ an expression body returning void
El.OnClick(() => Mail.Send(email));    // ❌ a lambda bound to a void delegate
```

**Fix:** take the lightbulb, which offers the two honest intentions.

*Await it* — you meant to wait. A `void` method becomes `async Task`, because `async void` swallows the
exception and takes the process down with it:

```csharp
async Task Register(User user)
{
    await Mail.Send(Email.To(user.Email).Subject("Welcome"));
    await Jobs.Enqueue(new SendOnboardingTips(user.Id)).In(24.Hours);
}
```

*Discard it* — you meant to let it run unwatched, which is what a fire-and-forget continuation is:

```csharp
_ = Task.Run(() => Drain(queue));
```

The discard is not a way to silence the rule; it is the rule's point. Unwatched work is a real choice, and
`_ =` is how C# writes it down so the next reader can see it was one.

A builder held in a variable is left alone, because awaiting it later is a legitimate shape:

```csharp
var sending = Mail.Send(email).In(24.Hours);
await sending;
```


## RASK094

**A scoped TypeScript export gets no typed method** · Warning

Every exported function — `export function`, or an arrow in an `export const` — and every `export class` in a
component's scoped `.ts` becomes a typed private member
of that component — `export function width(el: HTMLElement): number` is `await Width(_box)` — see
[Calling your script from C#](js-interop.md#calling-your-script-from-c). An export that cannot is left
out, the rest still generate, and this names it with the reason:

```ts
export function pairs(): [number, string][] { … }   // ⚠ RASK094: 'pairs' … it returns a tuple inside other data
export const PI = 3.14;                              // ⚠ RASK094: 'PI' … it is a value, not a function
```

The reasons, and what to write instead:

| Reason | Fix |
|---|---|
| The component is not `partial` | `public sealed partial class Card : Component` |
| A type with no C# counterpart — a tuple inside other data, a tuple with an optional or rest element, an intersection, a generic, `Map` | Return an exported `interface` instead; it becomes a nested record. A tuple on its own (`[number, string]`) is fine: it becomes `(double, string)` |
| A value (`export const PI = 3.14`) | Nothing to call — export a function that returns it. A function in a const (`export const f = (x: number) => …`) is fine |
| An inline object type (`(): { x: number }`) | Name it: `export interface Point { x: number }` |
| An element (`HTMLElement`) as a return value | Elements only go *in*; return what you need from one (its size, its text) |
| A callback that returns a value, or takes more than two arguments | A C# callback only runs; pass one object for many values |
| Overloads | Keep one signature — optional parameters cover most |
| A name the component already declares, or an interface of the same C# name (`Viewport` / `viewport()`) | Rename one of them |

A name the component only *inherits* is not a clash: `export function stop()` becomes `Stop()` and hides the
SVG `<stop>` entry inside that component, where `Markup.Stop` still reaches the tag.

The string call still works for anything left out: `js.InvokeAsync<T>("Rask.Card.pairs")`.

## RASK095

**A chain skips a required step** · Error · quick-fix

A non-nullable property with no initializer is a **required step** ([RASK001](#rask001)): the chain is not
the component until every one is taken. The chain's type already enforces that, but the compiler says so
in the names of generated types nobody wrote — and a half-built chain used as a **child** is not a compile
error at all: the children indexer takes it and it throws while rendering. This says what is missing, in
the chain's own words, at the same place:

```csharp
public sealed partial class Card : Component
{
    public string Title { get; set; }          // required
    public string Body  { get; set; }          // required
    public string? Note { get; set; }          // optional
}

Card.Note("x")            // ❌ RASK095: 'Card' needs 'Title' and 'Body' before anything else — write Card.Title(…).Body(…).Note(…)
return Card.Title("Q3");  // ❌ RASK095: 'Card' needs 'Body' — write Card.Title(…).Body(…)
Div[Card]                 // ❌ RASK095: 'Card' needs 'Title' and 'Body' — write Card.Title(…).Body(…)
```

Before, the same three lines read `CS1929 'RaskSeed_Card' does not contain a definition for 'Note'`,
`CS0029 Cannot implicitly convert type 'RaskPending_Card_Title' to 'Component'`, and — for the child —
nothing until the page threw.

The required steps can come in any order and `Key` can go anywhere; a chain stored in a local
(`var card = Card;`) is left alone until it is used as something other than itself.

**Fix:** take the missing steps first (**quick-fix available** — the lightbulb inserts them right after the
chain so far, with a placeholder for you to replace: `""` for a string, `default` for a value type,
`default!` otherwise; a required step the chain takes *later* is moved up with its own argument instead,
since the finished component has no setter for it):

```csharp
Card.Note("x")                  →  Card.Title("").Body("").Note("x")
Card.Note("x").Title("Q3")      →  Card.Title("Q3").Body("").Note("x")
```

The fix is not offered when a missing step is overloaded or generic, where a placeholder would be
ambiguous. A form control's bare entry (`Input`, owing `Bind` *or* `Value`) is not reported: its openings are
alternatives, not a list of steps to take.

## RASK096

**An event is declared as a delegate** · Error · quick-fix

Every event on a component is a `Callback`, `Callback<T>` or `Callback<T1, T2>` — a struct, not a delegate.
That is what keeps its chain step reachable: the chain receives on the component, so `Editor.OnSave(fn)`
has to fall through member lookup to the generated setter, and a delegate-typed property is *invocable* —
lookup stops at it and the call binds as an invocation of the property (CS1593).

```csharp
public sealed partial class Editor : Component
{
    public Action<Order>? OnSave { get; set; }        // ❌ RASK096: Declare 'OnSave' as Callback<Order>, not Action<Order>
    public Func<Order, Task>? OnSubmit { get; set; }  // ❌ RASK096: Declare 'OnSubmit' as Callback<Order>, not Func<Order, Task>
    public Func<Order, Component>? Row { get; set; }  // ✓ a template, not an event
    public Func<Order, bool>? Filter { get; set; }    // ✓ a selector
}
```

It reports a public settable property of a component whose type is `Action`, `Action<T>`,
`Action<T1, T2>`, or a `Func` of up to two arguments returning a bare `Task` or `ValueTask`. A delegate
that returns anything else is a template or a selector and is left alone, as is one with more than two
arguments (no `Callback` takes them — pass one object).

**Fix:** declare it non-nullable — an unset `Callback` is an empty slot whose `Invoke` does nothing — and
fire it with `await OnSave.Invoke(order)` (**quick-fix available** — the lightbulb rewrites the type and,
inside the same type declaration, `OnSave?.Invoke(x)` / `OnSave(x)` to `OnSave.Invoke(x)`, adding `await`
where the statement is in an `async` method or lambda):

```csharp
public Callback<Order> OnSave { get; set; }

async Task Save() => await OnSave.Invoke(order);
```

A caller's handler is unchanged: `Editor.OnSave(o => …)` and `Editor.OnSave(async o => …)` both bind.
Null checks (`OnSave != null`) are not rewritten — use `OnSave.HasValue`, or just call `Invoke`.

## RASK097

**Route helper name collides** · Error

The project has one `Routes` class, and pages that share a type name nest under the folders that tell them
apart — `Features.Admin.HomePage` becomes `Routes.Admin.HomePage()`. A page whose own type name is one of
those folder names would need `Routes.Admin()` *and* the class `Routes.Admin` side by side, which C# does not
allow (CS0102), so the generator reports this instead of emitting code that cannot compile. The same holds
for a helper or nested class that would share its enclosing class's name (CS0542).

```csharp
namespace MyApp.Features.Admin    { [Route("/admin")]    public sealed partial class HomePage : Component { } }
namespace MyApp.Features.Shop     { [Route("/shop")]     public sealed partial class HomePage : Component { } }
namespace MyApp.Features.Settings { [Route("/settings")] public sealed partial class Admin : Component { } }
// ❌ RASK097: The route helper for 'MyApp.Features.Settings.Admin' cannot be generated as 'MyApp.Routes.Admin()' …
```

**Fix:** rename the page (`AdminSettingsPage`) or the folder, or give the clashing pages distinct type names
so they stay flat.

## RASK098

**Web API member is missing from a supported browser** · Warning

Opt-in. A project that sets `<RaskBrowserTargets>` names the oldest browser of each engine it supports, and
every call to a generated web API member (Rask.Web's globals and objects, an element ref's MDN members) is checked
against MDN's browser-compat data for it: a member a named browser never shipped, or shipped only in a later
version, is reported where it is called. Unset, nothing is checked.

```xml
<PropertyGroup>
  <RaskBrowserTargets>safari >= 16; firefox >= 115</RaskBrowserTargets>
</PropertyGroup>
```

```csharp
var device = await Navigator.Usb.RequestDevice(new() { Filters = [] });
// ⚠️ RASK098: 'RequestDevice' is not in Safari >= 16 (never shipped), Firefox >= 115 (never shipped) …

if (await Navigator.Usb.IsSupported)
{
    var device = await Navigator.Usb.RequestDevice(new() { Filters = [] });   // ✅ asked first
    await device.Open();                                                     // ✅ reached from a guarded object
}
```

A call is not reported inside the body of an `if` whose condition awaits an `IsSupported` (alone or with `&&`),
on the right of `await X.IsSupported && …`, in the true branch of `await X.IsSupported ? … : …`, or after an
early exit `if (!await X.IsSupported) return;` in the same block. Any `IsSupported` counts, so what a guarded
object hands back needs no guard of its own. A check stored in a variable first is not followed: write it in the
condition, or `#pragma warning disable RASK098` around the call.

The browsers are `chrome`, `edge` (Chromium's version numbers), `firefox` and `safari`; mobile data counts for its
engine where only the mobile browser ships a member. Entries are separated by `;` or `,`.

**Fix:** guard the call with `IsSupported` and give that browser another path, or raise the target.

## RASK099

**Supported browser not understood** · Warning

An entry of `<RaskBrowserTargets>` that is not a known browser, `>=`, and a dotted version number is reported
once per build, since it would otherwise check nothing.

```xml
<RaskBrowserTargets>safari >= 16; opera 90</RaskBrowserTargets>
<!-- ⚠️ RASK099: <RaskBrowserTargets> entry 'opera 90' is not '<browser> >= <version>' … -->
```

**Fix:** write `chrome`, `edge`, `firefox` or `safari`, then `>=`, then the version: `safari >= 16`.

## RASK100

**`[BlazorParameter]` names a parameter the hosted component does not declare** · Error

`[BlazorParameter("X")]` feeds a property of the island into the hosted component's parameter `X`. When the
component has no public, settable `[Parameter]` called `X`, the property is still a chain step — but nothing
writes it, so the call site sets a value the component never receives.

```csharp
public sealed partial class Chart : BlazorComponent<MudChart>
{
    [BlazorParameter("ChartSeris")]                     // ❌ RASK100: 'Chart.Series' is mapped to 'ChartSeris',
    public List<ChartSeries>? Series { get; set; }      //    but 'MudChart' declares no [Parameter] of that name …

    [BlazorParameter("ChartSeries")]                    // ✅ spelled as MudChart declares it
    public List<ChartSeries>? Series { get; set; }
}
```

Not reported when the hosted component cannot be read at all — a `.razor` in the same project, which is
[RASK066](#rask066).

**Fix:** spell the name exactly as the component declares it (it is case-sensitive, and a library upgrade may
have renamed it), or remove the attribute if the property is not meant to reach the component.

## RASK101

**Authorization attribute is not read** · Error

Who may send a [`Rask.Cqrs`](cqrs.md#who-may-send-it) message is read off its handler at compile time:
`[Authorize]` and `[AllowAnonymous]` on the handler class or a base class, and on an event or subscription
record for who may subscribe. An attribute that derives from `AuthorizeAttribute`, or implements
`IAuthorizeData`, sets its roles and policy in code the build cannot run, and an attribute on the `Handle`
method is never looked at. Either would leave the handler open while reading as protected, so both are
refused.

```csharp
public sealed class AdminOnlyAttribute : AuthorizeAttribute
{
    public AdminOnlyAttribute() => Roles = "admin";
}

[AdminOnly]                                          // ✗ RASK101 — derives from AuthorizeAttribute
public sealed class PurgeLogsHandler : ICommandHandler<PurgeLogs>
{
    [Authorize(Roles = "admin")]                     // ✗ RASK101 — on Handle, not on the class
    public Task Handle(PurgeLogs command) => /* … */;
}

[Authorize(Roles = "admin")]                         // ✅
public sealed class PurgeLogsHandler : ICommandHandler<PurgeLogs> { /* … */ }
```

**Fix:** write `[Authorize(Roles = …, Policy = …)]` on the handler class itself. To share one rule between
handlers, name a policy (`[Authorize(Policy = "admin")]`) or put the `[Authorize]` on a common base class.

## Island build diagnostics (RASKISLAND)

These come from the [islands](islands.md) build — `Rask.External`'s MSBuild targets and tasks — rather than from
an analyzer, so they appear in `dotnet build` output and not as squiggles, and have no quick-fix. Every message
starts `Rask islands:` and ends with what to do.
A warning can be demoted per project with `MSBuildWarningsAsMessages` — not `NoWarn`, which is the compiler's
switch and does not reach a build task:

```xml
<MSBuildWarningsAsMessages>$(MSBuildWarningsAsMessages);RASKISLAND004</MSBuildWarningsAsMessages>
```

| Code | Severity | Summary |
| --- | --- | --- |
| [`RASKISLAND001`](#raskisland001) | error | Node.js is missing or too old to build the islands |
| [`RASKISLAND002`](#raskisland002) | error | The bundler finished but wrote no manifest |
| [`RASKISLAND003`](#raskisland003) | error | The bundle was published but no endpoint serves it |
| [`RASKISLAND004`](#raskisland004) | warning | A declared island's front-end file is not being bundled |
| [`RASKISLAND005`](#raskisland005) | error | A package island declaration the build cannot act on |
| [`RASKISLAND006`](#raskisland006) | error | No props snapshot, and this build cannot extract one |
| [`RASKISLAND007`](#raskisland007) | error | A package component's props could not be read |
| [`RASKISLAND008`](#raskisland008) | error | A locked build found an out-of-date props snapshot |
| [`RASKISLAND009`](#raskisland009) | warning | A package island the build did not see before compiling |
| [`RASKISLAND010`](#raskisland010) | warning | The props snapshot is from another package version than the lockfile pins |
| [`RASKISLAND011`](#raskisland011) | error | Two front-end files would register under one island name |
| [`RASKISLAND012`](#raskisland012) | error | An island names a runtime Rask has no adapter for |
| [`RASKISLAND013`](#raskisland013) | error | `RaskExternalDevServerUrl` is not an http(s) origin |
| [`RASKISLAND014`](#raskisland014) | error | React and Preact islands in one project |
| [`RASKISLAND015`](#raskisland015) | error | Two runtimes that compile the same extension share a folder tree |
| [`RASKISLAND016`](#raskisland016) | error | A package island's plugin cannot be kept off another runtime's files |
| [`RASKISLAND017`](#raskisland017) | error | The generated prop types could not be read from the compiled assembly |
| [`RASKISLAND018`](#raskisland018) | warning | The islands' declared runtimes could not be read from the compiled assembly |

### RASKISLAND001

**Node.js is missing or too old** · error

A project with islands and a `package.json` bundles them with Vite, which needs Node. The build probes
`node --version` before `npm` runs and stops here when it did not run, or reported less than
`RaskExternalMinimumNode` (22.12.0).

**Fix:** install the current LTS (`nvm install --lts`, `brew install node`, `winget install OpenJS.NodeJS.LTS`)
and build again. `-p:RaskExternalMinimumNode=…` moves the bar if you have a reason to.

### RASKISLAND002

**The bundler wrote no manifest** · error

Vite exited without an error but `manifest.json` is not where the build expects it. The browser resolves every
island's chunk through that file, so none would mount.

**Fix:** read the bundler's own output just above the error — it names the file that failed. If
`RaskExternalBuildCommand` is overridden, it has to run Vite with the generated config it is handed.

### RASKISLAND003

**The bundle was published but nothing serves it** · error

`dotnet publish` produced the chunks, but the static-web-assets endpoints manifest has no route under
`RaskExternalPublicBase`, so every chunk request would fall through to the page and come back as HTML.

**Fix:** keep `RaskExternalOutputDir` under `wwwroot`, and do not skip the `_RaskExternalStaticWebAssets` target.

### RASKISLAND004

**A declared island's front-end file is not being bundled** · warning

The class says its markup comes from `Gauge.tsx`; the files the build will bundle do not include it. The page
renders, the chunk does not exist, and the browser reports `'Gauge' is not in the manifest`.

```
Features/Gauge.cs        public sealed partial class Gauge : ReactComponent { }
Features/Gauge.tsx       ← missing, misnamed, or excluded from the island globs
```

**Fix:** put the file beside the class under the same name, or declare it with
`<RaskExternal Include="…"/>`. A fixture island with no module on purpose can demote the code — see
[islands](islands.md#a-lit-island-and-scoped-typescript-in-one-project).

### RASKISLAND005

**A package island declaration the build cannot act on** · error

Reported at the class's `Module` line. One of: the class also has a front-end file beside it; `Export` is not an
identifier (or a dotted path of them, or for Lit a tag name); `Export` is overridden but `Module` names no
package; the export is still written after a `#` in `Module`; or a Lit element is named by its class and no
snapshot records its tag.

```csharp
protected override string Module => "react-colorful#HexColorPicker";   // ✗ RASKISLAND005

protected override string Module => "react-colorful";                  // ✓
protected override string Export => "HexColorPicker";
```

**Fix:** the message gives the exact overrides to write for the case it found.

### RASKISLAND006

**No props snapshot, and this build cannot extract one** · error

A package island's chain steps are generated from `{Island}.props.json`. There is none, and this build was told
not to read the package (`RaskExternalBuild=false`, `RaskExternalPropsExtract=false`) — the message says which.

**Fix:** run `npm install`, build once without that switch, and commit the `{Island}.props.json` it writes.

### RASKISLAND007

**A package component's props could not be read** · error

The props extractor ran and could not describe the component. The bracketed reason in the message is one of
`module-not-found`, `export-not-found`, `not-a-component`, `lit-tag-unknown`, `runtime-unsupported` or
`extractor-crashed`. Two rarer variants: the extractor wrote no result at all (its own output, just above, says
why), or it did not report on one island.

**Fix:** run `npm install` so the package is in `node_modules`, check `Module` and `Export` against what the
package actually exports, and build again. For an island the extractor skipped, `dotnet build --no-incremental`.

### RASKISLAND008

**A locked build found an out-of-date snapshot** · error

Under `ContinuousIntegrationBuild=true` or `-p:RaskExternalPropsLocked=true` the build reads the package but
never rewrites a snapshot; one that no longer matches fails instead.

**Fix:** build once locally without the lock (`dotnet build`), and commit the refreshed `{Island}.props.json`.

### RASKISLAND009

**A package island the build did not see before compiling** · warning

The compiled assembly declares a package island the pre-compile source scan missed, so its props were not read
and its snapshot was not refreshed. The scan reads source text, and only finds a constant.

```csharp
protected override string Module => Packages.Button;                // ⚠ RASKISLAND009 — not a literal here
protected override string Module => "@mui/material/Button";         // ✓
```

**Fix:** return `Module` as a constant string from the class's own body.

### RASKISLAND010

**The snapshot is from another package version than the lockfile pins** · warning

Reported only on a build that does not extract. `{Island}.props.json` records the version it was read from, and
`package-lock.json` now pins a different one; the props may or may not still be right.

**Fix:** run `npm install`, then `dotnet build` without `RaskExternalBuild=false` or
`RaskExternalPropsExtract=false`, and commit the rewritten snapshot.

### RASKISLAND011

**Two front-end files would register under one island name** · error

The island's name — the file name, unless the item says otherwise — is the key the browser resolves a chunk by.
Two files with one name would overwrite each other in the manifest, differently depending on build order.

```
Features/Sales/Chart.tsx
Features/Stock/Chart.tsx      ✗ RASKISLAND011 — both are 'Chart'
```

**Fix:** rename one file, and its C# class with it (`StockChart.tsx` beside `StockChart.cs`).

### RASKISLAND012

**An island names a runtime Rask has no adapter for** · error

Only a hand-written `<RaskExternal>` item can do this; a class picks its runtime by its base class.

```xml
<RaskExternal Include="Widgets/Chart.vue" Runtime="vue3"/>   <!-- ✗ RASKISLAND012 -->
<RaskExternal Include="Widgets/Chart.vue" Runtime="vue"/>    <!-- ✓ -->
```

**Fix:** use one of `react`, `preact`, `solid`, `vue`, `svelte`, `angular`, `lit` — the message lists them.

### RASKISLAND013

**`RaskExternalDevServerUrl` is not an http(s) origin** · error

`rask dev` serves islands from Vite, and the config pins the port named here. A value that is not an absolute
`http(s)` URL, or one that carries a path, cannot say which port that is.

```
-p:RaskExternalDevServerUrl=http://localhost:5174/islands   ✗
-p:RaskExternalDevServerUrl=http://localhost:5174           ✓
```

**Fix:** stop at the port. `rask dev` sets this itself; it only needs fixing when set by hand.

### RASKISLAND014

**React and Preact islands in one project** · error

Not a Rask rule: `@vitejs/plugin-react` resolves Babel 8 and `@preact/preset-vite` pins `@babel/core` 7, so npm
refuses to install both. The message names the islands on each side.

**Fix:** pick one runtime for the project. See
[islands](islands.md#react-and-preact-cannot-share-a-project).

### RASKISLAND015

**Two runtimes that compile the same extension share a folder tree** · error

React, Preact and Solid all compile `.tsx`, and each Vite plugin is scoped to the folders its own islands live
in. Overlapping folders leave one plugin compiling the other's island with the wrong transform.

```
Features/Islands/Counter.tsx            ✗ RASKISLAND015 — React here…
Features/Islands/Solid/Spark.tsx          …and Solid nested inside it

Features/Islands/React/Counter.tsx      ✓ a folder each
Features/Islands/Solid/Spark.tsx
```

**Fix:** give each runtime a folder of its own, and do not nest one inside the other. See
[islands](islands.md#two-runtimes-that-share-an-extension-need-separate-folders).

### RASKISLAND016

**A package island's plugin cannot be kept off another runtime's files** · error

A package island whose runtime has to compile the package itself — Solid, in practice — needs that runtime's
Vite plugin, and the project also has islands of a runtime that compiles the same files (React or Preact). Plugins are kept apart by folder, and a package has no folder.

**Fix:** keep that runtime's package islands and the other runtime's islands in separate projects.

### RASKISLAND017

**The generated prop types could not be read** · error

After the compile, the build reads each island's TypeScript prop types out of the assembly to write the `.d.ts`
files the front end is checked against. The assembly was there and could not be read; the message carries the
underlying error. Failing is deliberate — the alternative is a front end type-checked against the last build's
props.

**Fix:** `dotnet build --no-incremental`. If it persists, report it with the bracketed error.

### RASKISLAND018

**The islands' declared runtimes could not be read** · warning

Each island's runtime is read from the compiled assembly, because the file extension only names a family —
`.tsx` is React, Preact or Solid. When the assembly cannot be read the build falls back to the extension, which
is right for a project with one runtime per extension and silently wrong otherwise.

**Fix:** `dotnet build --no-incremental` so the assembly is written afresh.

## Build errors from MSBuild

These come from Rask's other build targets rather than from an analyzer, so they have no severity to configure
and no quick-fix.

| Code | What happened | Fix |
| --- | --- | --- |
| `RASKSPA001` | The front end needs building and `node --version` did not run. | Install Node.js, or pass `-p:RaskSpaBuild=false` to build the server without the front end. |
| `RASKSPA002` | The front end's build finished but wrote no `index.html` where the host expects it. | Set `RaskSpaDistDir` to the bundler's real output directory. |
| `RASKSPA003` | `RaskSpaClientDir` names a directory with no `package.json`. | Point it at the front-end project directory. |
| `RASKSPA004` | The host declares remote messages and its front end has no TypeScript configuration to check the generated contracts with. | Use the framework's TypeScript template, name the config with `RaskSpaTypeScriptConfig`, or set `RaskEmitTypeScript=false`. |
| `RASKSPA005` | Node is older than `RaskSpaMinimumNode`. | Install the current LTS, or set `RaskSpaMinimumNode`. |
| `RASKSPA006` | A host references more than one WebAssembly client. | A host serves one client — give each its own host. |
| `RASKSPA007` | A host has both a WebAssembly client and a front-end `client` folder. | `MapRaskSpa` serves one app per host — remove one, or give it its own host. |
| `RASKSPA008` | The WebAssembly client reported no target framework, so its bundle cannot be located. | Give the client project a `<TargetFramework>`. |
| `RASKSPA009` | The WebAssembly client targets several frameworks. | Give it exactly one, such as `net10.0-browser`. |
| `RASKDOM001` | Contributors only: the MDN snapshot could not be turned into element types. | The message names the member that collides. |
| `RASKDOM002` | Contributors only: an event name has a part the emitter cannot split into words. | Add the word to `DomEventEmitter.Words`. |
