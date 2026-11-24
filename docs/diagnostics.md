# Rask diagnostics (RASK001–RASK133, RASK139–RASK144, RASK150, RASK165, RASK170–RASK178, RASK180–RASK188, RASKVAL001–RASKVAL002)

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
| **RASK023** | appends `.Alt("")` to the chain |
| **RASK026** | deletes the redundant `StateHasChanged()` statement |
| **RASK043** | marks the enclosing class `[RaskMarkup]` and makes it (and every type it is nested in) `partial` |
| **RASK071** | swaps ASP.NET's `[Route]` for Rask's own |
| **RASK084** | makes the accessor `private set` / `private init`, or the field `private` |
| **RASK085** | moves the collection into a `private readonly` field and exposes `IReadOnlyCollection<T>` |
| **RASK092** | rewrites the unit to the form its count reads with (`2.Hour` → `2.Hours`) |
| **RASK093** | awaits the call (making the method `async`), or assigns it to a discard `_ =` |
| **RASK095** | adds the missing required steps right after the chain so far (or moves one taken too late) |
| **RASK096** | declares the event as a non-nullable `Callback` and fires it with `.Invoke(…)` |
| **RASK104** | turns the static field or get-only auto-property into an expression-bodied property (`static Component Empty => Div;`) |
| **RASK118** | marks the enclosing class `[RaskMarkup]` and makes it (and every type it is nested in) `partial` |
| **RASK119** | deletes the redundant `StateHasChanged()` statement |
| **RASK121** | makes the `[JSInvokable]` method `public` |
| **RASK127** | rewrites `new KmRange(a, b)` into `KmRange.Create(a, b)`, and `km with { End = 5 }` into `KmRange.Create(km.Start, 5)` |
| **RASK129** | adds the missing `sealed` / `partial` to the value object's declaration |
| **RASK130** | declares a value object from the property's rules (`[StringLength(200)]` or `RuleFor(s => s.Name).MaximumLength(200)` → `public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200);`), types the property as it, and takes off the rules its base arguments now say |
| **RASK131** | rewrites `[DisplayName("X")]` as `[Display(Name = "X")]`, adding `using System.ComponentModel.DataAnnotations;` where it is missing |
| **RASK133** | calls the factory's generated entry: `await using var db = …; db.Add(Product.Place(n)); await db.SaveChangesAsync(t);` → `await Product.Place(n, cancellationToken: t);` |
| **RASK140** | reads Rask's `Clock` instead of the machine's: `DateTime.Now` → `Clock.Wall`, `DateTime.UtcNow` → `Clock.Now.UtcDateTime`, `x.ToLocalTime()` → `TimeZoneInfo.ConvertTimeFromUtc(x, Clock.Zone)` |
| **RASK150** | starts the required value object as `= null!` instead of a made-up value |
| **RASK184** | refuses instead of throwing: `if (c) throw new InvalidOperationException(m);` → `Refuse.When(c, m);` (`if (!c)` → `Refuse.Unless(c, m)`; an `ArgumentException(m, nameof(x))` → `Refuse.When(c, () => x, m)`), any other throw → `throw new RaskValidationException(m)` |
| **RASK185** | moves the aggregate's load into the `Db.Transaction` block, as its first statement |
| **RASK186** | calls the operation's generated entry: `Supply.Update(id, s => s.Change(d), version: v)` → `Supply.Change(id, v, d)` |
| **RASK187** | calls the aggregate's own set write: `db.Pairings.Where(p => …).ExecuteDeleteAsync()` → `Pairing.DeleteWhere(p => …)`, `ExecuteUpdateAsync(s => …)` → `Pairing.UpdateWhere(p => …, s => …)` |

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
| [RASK022](#rask022) | Error | List item is missing a `Key` |
| [RASK023](#rask023) | Warning | `Img` is missing `Alt` text |
| [RASK024](#rask024) | Warning | `UseAuthentication()` must precede `MapRask()` |
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
| [RASK060](#rask060) | Warning | `AddRask` is called twice on the same service collection |
| [RASK067](#rask067) | Error | Endpoint shape has no wire encoding |
| [RASK068](#rask068) | Warning | Endpoint has no generated client method |
| [RASK069](#rask069) | Error | Two endpoints claim one client method |
| [RASK070](#rask070) | Warning | Endpoint's response type is not statically known |
| [RASK071](#rask071) | Error | ASP.NET route attribute on a Rask component |
| [RASK072](#rask072) | Warning | Entity `Configure` method will not be called |
| [RASK073](#rask073) | Warning | Strongly-typed id has no usable value |
| [RASK074](#rask074) | Warning | More than one user type |
| [RASK082](#rask082) | Error | A type already has the generated model's name |
| [RASK083](#rask083) | Warning | Nested entity gets no generated model |
| [RASK084](#rask084) | Error | Model state can be changed from outside the type |
| [RASK085](#rask085) | Warning | Entity exposes a mutable collection of entities |
| [RASK086](#rask086) | Warning | Aggregate has no parameterless constructor, so `Create` is not generated |
| [RASK087](#rask087) | Error | Aggregate reaches across a boundary instead of holding an id |
| [RASK088](#rask088) | Warning | Child collection cannot be synced, so a save cannot add or remove one |
| [RASK089](#rask089) | Warning | Id looks like a reference but no navigation was inferred |
| [RASK090](#rask090) | Warning | Two entities want one DbContext set name, so neither is generated |
| [RASK092](#rask092) | Warning | A unit reads wrong for its count (`2.Hour`, `1.Hours`) |
| [RASK093](#rask093) | Error | An awaitable result is dropped, so the call never runs |
| [RASK094](#rask094) | Warning | A scoped TypeScript export gets no typed method on its component |
| [RASK095](#rask095) | Error | A chain skips a required step |
| [RASK096](#rask096) | Error | An event is declared as a delegate, so its chain setter is unreachable |
| [RASK097](#rask097) | Error | A route helper's name collides with a nested `Routes` class |
| [RASK098](#rask098) | Warning | A web API member is missing from a browser `<RaskBrowserTargets>` names |
| [RASK099](#rask099) | Warning | A `<RaskBrowserTargets>` entry is not `<browser> >= <version>` |
| [RASK101](#rask101) | Error | An authorization attribute on a handler or event is not one Rask reads |
| [RASK103](#rask103) | Error | A constructor-built value object holds a value object of several values |
| [RASK104](#rask104) | Warning | A static member keeps a component built from a chain entry, sharing one render's slot across pages |
| [RASK105](#rask105) | Error | A value object compares by reference |
| [RASK106](#rask106) | Error | A key of several columns cannot be built, or an aggregate's form cannot carry a row keyed by one |
| [RASK107](#rask107) | Error | An aggregate or a child entity is made with `new` outside the type that guards it |
| [RASK108](#rask108) | Warning | A read face's collection of the rows that point at it would take a name already used |
| [RASK109](#rask109) | Warning | A call to an `[Authorize]` component method cannot be checked where it is written |
| [RASK110](#rask110) | Error | More than one class implements `IRaskModel` |
| [RASK111](#rask111) | Warning | A collection of value objects with no list behind it is a NULL column |
| [RASK112](#rask112) | Error | A relationship configured in `Configure` has no free name for its read navigation |
| [RASK113](#rask113) | Error | More than one aggregate says `ITenant` or `IUser` |
| [RASK114](#rask114) | Error | The tenant's or the user's key cannot be the current one |
| [RASK115](#rask115) | Warning | Tenant-scoped tables and no tenant aggregate |
| [RASK116](#rask116) | Warning (off unless `<RaskStrict>`) | Test is not named as a sentence |
| [RASK117](#rask117) | Error | A cached fragment holds something interactive |
| [RASK118](#rask118) | Warning | A chain step is called on a component type outside a markup host |
| [RASK119](#rask119) | Warning | Redundant `StateHasChanged` in `OnMount`/`OnUpdated` |
| [RASK120](#rask120) | Warning | An event callback captures a `for` loop's variable |
| [RASK121](#rask121) | Warning | A `[JSInvokable]` method is not `public` |
| [RASK122](#rask122) | Warning | The signed-in user is kept without following `IUserProvider.Changed` |
| [RASK123](#rask123) | Error | An `[OffThread]` message cannot run in a Web Worker |
| [RASK124](#rask124) | Warning | An event's handlers disagree on who may publish it remotely |
| [RASK125](#rask125) | Warning | An action with several `[Route]`s gets a client method for the first |
| [RASK126](#rask126) | Warning | Lazy-loading proxies on a context that does not allow lazy loading |
| [RASK127](#rask127) | Error | A value object declared from a base record is made past its rules |
| [RASK128](#rask128) | Warning | A column is configured differently from its value object |
| [RASK129](#rask129) | Error | A value object declared from a base record is not `sealed partial` |
| [RASK130](#rask130) | Info | An aggregate stores a bare value where a value object would hold its rules |
| [RASK131](#rask131) | Warning | A property is named by `[DisplayName]`, which Rask never reads |
| [RASK132](#rask132) | Warning | A value object's `Validate` is never asked: declare it from a base record |
| [RASK133](#rask133) | Info | An aggregate is written on a DbContext around its own operations |
| [RASK139](#rask139) | Warning | A `Rules` method is not the in-type FluentValidation convention |
| [RASK140](#rask140) | Info | Domain code reads the machine's clock |
| [RASK141](#rask141) | Info | Domain code looks up a service |
| [RASK142](#rask142) | Info | Database work run side by side inside a transaction |
| [RASK143](#rask143) | Info | `[Key]` on an aggregate or an entity |
| [RASK144](#rask144) | Info | `[Compare]` names a property the model does not have |
| [RASK150](#rask150) | Warning | A required value object starts from a placeholder |
| [RASK165](#rask165) | Warning | A configuration call the generators read is written where they cannot see it |
| [RASK170](#rask170) | Warning | An aggregate calls an operation's generated entry by its bare name |
| [RASK171](#rask171) | Warning | An operation's parameter takes a name its generated entry needs |
| [RASK172](#rask172) | Error | The roles of an operation are written where its generated entry cannot read them |
| [RASK173](#rask173) | Warning | A factory is public, so its generated entry cannot be called |
| [RASK181](#rask181) | Warning | A FluentValidation rule rules a value object |
| [RASK182](#rask182) | Warning | A FluentValidation rule disagrees with its column |
| [RASK183](#rask183) | Error | A value object declares its rules twice |
| [RASK174](#rask174) | Info | Two operations take the same parameters, so neither gets a `Form.For` |
| [RASK175](#rask175) | Error | An aggregate stores a password |
| [RASK176](#rask176) | Error | A password is sent back |
| [RASK177](#rask177) | Info | Code written for one database |
| [RASK178](#rask178) | Info | A transaction writes two aggregates without saying why |
| [RASK180](#rask180) | Error | Children are added or removed outside their aggregate |
| [RASK184](#rask184) | Info | An aggregate refuses with an exception no form can show |
| [RASK185](#rask185) | Info | An aggregate is loaded outside the transaction that changes it |
| [RASK186](#rask186) | Info | An update runs an operation that has a generated entry |
| [RASK187](#rask187) | Info | A raw ExecuteUpdate or ExecuteDelete writes an aggregate's rows |
| [RASK188](#rask188) | Info | An operation gets no Form.For, as one of its parameters is no field a form holds |
| [RASKVAL001](#raskval001) | Error | Two validators for the same model |
| [RASKVAL002](#raskval002) | Warning | Validator cannot be constructed automatically |

**Retired, never recycled:** RASK025, RASK027, RASK030, RASK032, RASK034, RASK042, RASK046, RASK047, RASK048,
RASK049, RASK050, RASK054, RASK056, RASK057, RASK058, RASK059, RASK061, RASK062, RASK063, RASK064, RASK065, RASK066,
RASK075, RASK076, RASK077, RASK078, RASK079, RASK080, RASK081, RASK091, RASK100 and RASK102, the islands build's
RASKISLAND001–019, and the npm front-end build's RASKSPA001–005 and RASKSPA007. Each guarded a mistake that can no
longer be written; a suppression that names one is dead and can be deleted.

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
be `string` or implement `System.IParsable<T>` (every built-in numeric, `Guid`, `DateOnly`, `DateTime` and
`bool` qualify; an enum does not — bind it as a `string`). A `[QueryParam]` may also be a list of such a type —
`T[]`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or `IEnumerable<T>` with a non-nullable `T`, read from its
key repeated with brackets, `drivers[]=a&drivers[]=b` ([routing](routing.md#a-list-is-the-key-repeated-with-)); any
other collection, and a list bound to a path segment, raises it.

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
**List item is missing a `Key`** · Error (a plain element row: Warning)

A Rask component appears in a sibling-list context (a `.Select`/`.SelectMany` projection, or `.Add`
in a loop) without a `Key`. Keyless items reconcile **by position**, which loses focus and input state
and emits untrusted structural diffs on insert/remove/move.

For a **component row** — anything that renders itself (it overrides `Render()`: your own row or card,
a `Ui.*` part that draws its own markup) — position also decides which instance is reused, so the row's own state and
its own handlers follow the slot rather than the item. That position is its order among the components
of its type the same `Render()` writes. A row inserted at the top hands every row below it the state of
the row above; and an event already on its way — a click on "Delete" on the second row, sent just as the
first row was removed — runs on whichever record sits second **now**, deleting the wrong row (#1243).
Nothing about the handler moved, so the runtime cannot tell, and the build refuses the shape: **a
component row without a `Key` is an error**. A plain HTML element row (`Li[…]`) has no instance of its
own for an event to reach by mistake, so it stays a warning.

Both chain spellings are recognised — `Li[…]` and `Li.Class("c")[…]`. The chain went unreported until
#704, when the only surface this checked was a factory call: the check matched a method named after the
component, and a chain has none.

```csharp
// ✗ RASK022 (error) — the row component is reused by position
orders.Select(o => OrderRow.Order(o))
// ✓ the instance, its state and its events stay with the record
orders.Select(o => OrderRow.Order(o).Key(o.Id))

// ✗ RASK022 (warning) — items.Select(i => Li[ i.Name ])
// ✓ items.Select(i => Li.Key(i.Id)[ i.Name ])
```

**Fix:** add a stable `.Key(row.Id)` — an entity id, never the loop index (an index is the position
again). A list that is meant to be positional (a demo of exactly this, a benchmark) keeps building under
`#pragma warning disable RASK022` with a comment saying why. See [keyed lists](getting-started.md) and
the [live-rendering architecture](architecture/live-rendering.md) for why identity beats position.

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
a lifecycle hook (that is [RASK119](#rask119)'s), async loop, or event subscription
(`feed.Updated += StateHasChanged`), or a call on a *different* component, is left alone. Suppressible like any
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
`Ui.NavlistItem.Href(…)`, via the `string → RouteUrl` implicit conversion) — **only** when
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
host works too, since simple-name lookup walks out through enclosing types (**quick-fix available** — the
IDE lightbulb adds `[Rask.Core.RaskMarkup]` and `partial`). A component's own chain written there —
`OrderReceipt.OrderId(…)` — is [RASK118](#rask118). Suppress with
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

**`Validate` is the one step that adds.** `.Validate(a).Validate(b)` runs `a`, then `b` if `a` let the
value through, so a repeated `Validate` is not reported. **Neither is a repeated class step:**
`.Class("w-full").Class("mb-8")` renders `w-full mb-8`, and a kit's `ContainerClass`, `InputClass` and the
other `…Class` steps join the same way.

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
and records, record structs or classes composed of the same. A composite needs either a public constructor whose
parameters all match properties — which every positional record has — or a public parameterless
constructor with settable properties. A one-value [value object](domain.md#value-objects)
(`record DriverName(string Value) : Text(Value, MaxLength: 1000)`) and a record struct of one value
(`record struct VehicleId(int Value)`) travel as that value — see [the wire codecs](cqrs.md#wire-codecs-and-rask053).

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
- it would take a **second request body**, and a request has one;
- it is mapped on a **`MapGroup`**, whose prefix this call site cannot see.

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
endpoint is called by hand. An endpoint that is not part of the app's API at all is taken out with
`.ExcludeFromDescription()` — on the endpoint, or once on its group (a group's conventions reach every
endpoint in it, nested groups included), and neither RASK068 nor RASK070 fires for it:

```csharp
var site = app.MapGroup("").ExcludeFromDescription();
site.MapGet("/version", () => Results.Ok(Version));   // no client method, no warning
```

The group is followed through a fluent chain, a local or field it was stored in, and calls on that local
in the same file; a group handed in as a parameter cannot be traced, so its endpoints still report.

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
taken out of the client with `.ExcludeFromDescription()`, the same call that keeps it out of OpenAPI —
on the endpoint or on the `MapGroup` it is mapped on — and this diagnostic does not fire for it.

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

## RASK082

**A type already has the generated model's name** · Error

An aggregate that names `Model` (or `CreateModel` / `UpdateModel`) gets a generated form model emitted beside
it, in the same namespace, as `{Aggregate}Model`. A
hand-written, non-`partial` type of that name, often a request model written before the generator existed,
would collide with it as `CS0101`, a message that names neither the generator nor the way out. So the
generator stands down for that aggregate and says why: no `ProductModel` is generated, and neither are the
`Create`, `Update` and `Delete` that take it.

```csharp
public sealed class Product : Aggregate<Guid, Model> { /* … */ }

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
    public sealed class Product : Aggregate<Guid, Model> { }  // ⚠ RASK083: nested in Catalog
}
```

**Fix:** declare the aggregate at namespace level:

```csharp
namespace Shop.Catalog;

public sealed class Product : Aggregate<Guid, Model> { }      // ✓ gets ProductModel
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

An aggregate that names `Model` gets a generated form model (`ProductModel` for `Product`) and the writes that take it
([data guide](domain.md#writing-create-update-delete)). `Product.Create(model)` starts from a new, empty
aggregate, and so does `new ProductModel()`, which
holds the aggregate's own defaults. That needs a constructor that takes nothing, and an aggregate that declares
no constructor has one for free. Declaring one that takes arguments removes it.

```csharp
public sealed class Product : Aggregate<Guid, Model, Deletable>
{
    public Product(string name) => Name = name;   // ⚠ RASK086: the only constructor takes a name

    public string Name { get; private set; }
}
```

**Fix:** declare no constructor, and put domain creation in a static factory:

```csharp
public sealed class Product : Aggregate<Guid, Model, Deletable>
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
loaded, saved and deleted with its root ([data guide](domain.md#children)):

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
the save ([data guide](domain.md#children)). Writing it needs something to add to and remove from — either the
property's own type is an `ICollection<T>`, or there is exactly one field behind it holding the children, or the
property has a setter a new list can be written back through (`IReadOnlyList<OrderLine> Lines { get; private set; } = []`).

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

## RASK103

**A constructor-built value object holds a value object of several values** · Error

A value object of several values is stored as an EF Core **complex type**, and EF Core binds a constructor
parameter to a **column** — never to a complex type. So a value object whose every constructor takes one cannot
be built when a row is read, and EF Core refuses the whole model when the app starts (*No suitable constructor
was found … Cannot bind 'Label'*). The build says it first, at the parameter:

```csharp
public sealed record Label(string Code, int Copies);            // several values: a complex type

public sealed record Route(Label Label, string Carrier);        // ✗ RASK103, on `Label`

public sealed class Parcel : Aggregate<Guid>
{
    public Route Route { get; private set; } = …;
}
```

> 'Route' is built through its constructor and holds 'Label', a value object of several values; EF Core cannot
> bind it. Give 'Route' a private parameterless constructor and private setters.

What it names: a record, a record struct or a class whose constructors ALL take a value object of several values
(nullable or not, a class or a struct), wherever the mapping reaches it — on the aggregate, inside another value
object, or on a row inside the aggregate. It is reported once, however many places hold it.

**Not reported**, because EF Core builds them: a value object built through its members; one with a constructor
that does not take it; a record that keeps its constructor and adds a private parameterless one; and a holder of
**one-value** value objects only (`record Terms(Sku Early, int Days)`) — those are columns, and bind. A property
marked `[NotMapped]` and a type no aggregate holds are not looked at.

**Fix:** let EF Core build it through its members.

```csharp
public sealed class Route
{
    private Route() { }

    public Label Label { get; private set; } = null!;
    public string Carrier { get; private set; } = "";

    public static Route Of(Label label, string carrier) => new() { Label = label, Carrier = carrier };
}
```

A record that should stay one keeps its positional constructor and gains a private parameterless one:
`public sealed record Route(Label Label, string Carrier) { private Route() : this(null!, "") { } }`.

---

## RASK104

**A static member keeps a component built from a chain entry** · Warning

An entry (`Div`, `Ui.Button`, `Card.Note("a")`) built during a render is a **positional slot** of the component
being rendered, handed back to it on its next render. A static initializer runs once — during whichever render
first touches the type — so the static keeps that one page's slot and shares it with every page after it. The
next entry of the same type built at that position redraws the shared instance with its own attributes, for the
rest of the process. Which page wins depends on request order, so it shows in one process and not the next.

```csharp
public sealed partial class Panel : Component
{
    private static readonly Component Empty = Div;               // ⚠ RASK104
    public static Component Spacer { get; } = Div.Class("gap");  // ⚠ RASK104

    protected override Component? Render() => Items.Count == 0 ? Empty : List;
}
```

> 'Empty' is static and keeps 'Div', built once by whichever render touched it first — that render's slot, then
> shared by every page. Build it per call instead: 'static Component Empty => …'.

What it names: a `static` field or `static` auto-property, in any type, whose type holds a component (a
component, an array of them, or a generic over one) and whose initializer builds a chain — an entry, or steps and
indexers on one, anywhere in the expression it evaluates.

**Not reported:** a static method or an expression-bodied property (`static Component Empty => Div;`) — each
call builds a fresh entry in the render that calls it; an instance field; and an entry inside a lambda the
initializer stores (`static readonly Func<Component> Make = () => Div;`), which runs later.

**Fix:** build it per read (**quick-fix available** for a `static readonly` field of one variable and a get-only
auto-property):

```csharp
private static Component Empty => Div;
public static Component Spacer => Div.Class("gap");
```

## RASK105

**A value object compares by reference** · Error

What an aggregate holds that is not an entity is a [value object](domain.md#value-objects): it is its values, so two
with the same values are the same. A `record` or a `record struct` compares that way as written. A plain class
compares by reference, so the app's own code would find two equal amounts different — EF Core still saves it right,
which is why nothing else would notice.

```csharp
public sealed class Money                       // ✗ RASK105: compares by reference
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";
}

public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    public Money Total { get; private set; } = null!;
}
```

**Fix:** make it a record, or derive it from `ValueObject` and name the values that make it the same:

```csharp
public sealed record Money(decimal Amount, string Currency);   // ✓

public sealed class Money : ValueObject                         // ✓
{
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";

    protected override IEnumerable<object?> EqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}
```

A strongly-typed id another aggregate is keyed by is a column, not a value object, and is never reported.

---

## RASK106

**A key of several columns cannot be built** · Error

`Entity<(DeliveryNote, Transaction)>` keys a child row by the aggregates it joins. Rask generates a `DeliveryNoteId`
and a `TransactionId` of those aggregates' own key types, and an `Id` that is the two values together — so the class
must be `partial`, each part must have a name, and nothing else may hold those names. A grandchild names the child
that holds it — `Entity<(ReportColumn, int TotalColumnId)>` — and gets one generated property per part of that
child's key (`ReportColumnReportId`, `ReportColumnColumnId`). See
[A key of several columns](domain.md#a-key-of-several-columns).

```csharp
public sealed class DeliveryNoteTransaction : Entity<(DeliveryNote, Transaction)> { }   // ✗ RASK106: not partial
public sealed partial class DeliveryNoteLine : Entity<(DeliveryNote, int)> { }           // ✗ RASK106: the int has no name
public sealed partial class Pairing : Aggregate<(DeliveryNote, Transaction)> { }         // ✗ RASK106: a root
```

> 'DeliveryNoteLine' has a key of several columns, but its key part 'int' has no name; name it — 'int LineNo' — and
> declare that property

Reported when:

- the class is not `partial`, or is nested in another type;
- a value part has no name (`int` rather than `int LineNo`), or the entity declares no property of that name and
  type — a value part is the entity's own `public int LineNo { get; private set; }`;
- an aggregate or entity part is given a name — its columns are always named after it (`{Aggregate}Id`);
- the entity already declares `Id`, or a name an aggregate or entity part generates;
- an entity part's own key cannot be built;
- the entity is an **aggregate root** — a key of several columns is for a child its aggregate holds;
- an aggregate whose **form** (its base names `Model`, `CreateModel` or `UpdateModel`) would carry such a child it
  cannot carry. A form carries a link as a list of ids (`ActivityIds`) and a row with fields of its own as a list of
  rows (`Columns`) — see [Link tables and child rows on the form](domain.md#link-tables-and-child-rows-on-the-form) —
  and refuses, rather than leaving the child out:
  - rows that hold rows of their own (`ReportColumn` holding `Totals`);
  - a row with no parameterless constructor, so the form cannot add one;
  - a key with no part naming the aggregate that holds the rows;
  - a link whose list of ids takes a name the aggregate already declares.

> 'ReportColumn' has a key of several columns, but 'Report.Layout' holds it on a form, and its rows hold rows of their
> own ('Totals'), which a form does not carry; take 'Model' (or 'CreateModel' / 'UpdateModel') off 'Report''s base
> and fill 'Layout' through a method of 'Report' — Report.Update(id, x => x.Fill(…))

**Fix:** declare the class `partial`, name each value part and declare its property. For a child a form cannot carry,
fill the collection through a method of the aggregate rather than a form:

```csharp
public sealed class Report : Aggregate<int, PerTenant<int>>   // no Model on the base
{
    private readonly List<ReportColumn> _layout = [];

    public IReadOnlyList<ReportColumn> Layout => _layout;

    public void Lay(IEnumerable<(int ColumnId, int[] Totals)> layout)
    {
        _layout.Clear();
        _layout.AddRange(layout.Select(static c => ReportColumn.For(c.ColumnId, c.Totals)));
    }
}

await Report.Update(id, r => r.Lay([(1, [100, 101]), (2, [])]));   // matched by key: insert, update, delete
```

---

## RASK107

**An aggregate or a child entity is made with `new` outside the type that guards it** · Error

An aggregate keeps its invariants by being born in one place: a member of its own type — its factory — or, from
a form, the generated `X.Create(model)`. A child is made by its own type or by the entity that holds it. This is
what a `private` parameterless constructor used to say, so that constructor need not be written.

```csharp
public sealed class Order : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    public string Reference { get; private set; } = "";

    public static Order Place(string reference) => new() { Id = Guid.CreateVersion7(), Reference = reference };   // ✓
}

var order = new Order();                       // ✗ RASK107
var order = Order.Place("A-1");                // ✓ then: await order.Save();
```

Generated code is not analyzed — the generated writes start from `new Order()` on purpose — and EF Core builds a
row through reflection, so loading is never affected.

---

## RASK108

**A read face's collection of the rows that point at it would take a name already used** · Warning

Every navigation the read face infers from an id gives its target the rows that point there, named after the
referencing type and whatever the id says before the target's name: `Distribution.FromFuelingId` gives
`FuelingRead.DistributionsFrom` ([the rows that point here](data.md#the-rows-that-point-here)). When another id
would give it the same name, the first in source order keeps it and the other adds its own words
(`ReportColumnColumnId` and `TotalColumnId` to a `CatalogColumn` give `ReportColumnTotals` and
`ReportColumnTotalsTotal`). When the target's face already uses the name — a column, a navigation, a collection —
or the other's own words are taken too, or two ids say nothing apart (a `FuelingId` on two `Distribution` types),
the collection is left out rather than renamed by a guess, and this warning names the clash. The build stays green, and
the forward navigation (`DistributionRead.FromFueling`) is still there.

```csharp
public sealed class Fueling : Aggregate<string>
{
    public int DistributionsFrom { get; private set; }      // a column of that name
}

public sealed class Distribution : Aggregate<string>
{
    public string FromFuelingId { get; private set; } = "";   // ⚠ RASK108
}
```

```text
FuelingRead does not get 'DistributionsFrom': the name is already a column of Fueling. Rename the id (Distribution.FromFuelingId) or the column.
```

**Fix:** rename one side. Renaming the id (`SourceFuelingId` gives `DistributionsSource`) changes its column, so
keep the column with `[Column("FromFuelingId")]` on an existing table; renaming the target's member works the same
way. Two ids that cannot be told apart are reported on both, each naming the other.

---

## RASK109

**A call to an `[Authorize]` component method cannot be checked where it is written** · Warning

A component method with `[Authorize]` is checked wherever it is called ([handlers](authentication.md#authorize-on-an-event-handler)):
a method group handed to a callback at run time, and every direct call — the one inside `.OnClick(() => Remove(id))`
too — by an interceptor the build writes. An interceptor cannot be written for a generic method or a method of a
generic component, a call through `base`, a method whose signature names a private type, or a call inside an
expression tree; such a call would run the method unchecked, and this warning names it.

A type another source generator makes — a Rask.Data read face (`CompanyRead`), a generated model, a strongly-typed
id — is named exactly as the method's own file names it. One case cannot be: a nullable parameter of a non-Rask
generated type (`CompanyId?`), which generated code cannot tell from a nullable struct; that call is reported here.

```csharp
public sealed class TrailersPage : Component
{
    protected override Component? Render() => Button.OnClick(() => Remove<int>(3))["Remove"];   // ⚠ RASK109

    [Authorize(Roles = "admin")]
    private void Remove<T>(T id) { }
}
```

```text
The call to 'App.TrailersPage.Remove<T>(T)' is not checked against its [Authorize] because it is generic — hand the method to the callback as a method group, or check the person before calling it
```

**Fix:** make the method non-generic, or hand it over as a method group (`.OnClick(Remove)`), which is checked at run
time. A call through `base` is checked by calling the method directly. For a nullable generated type, take the
parameter non-nullable or pass the method group.

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
    Mail.Send(Mail.Message.To(user.Email).Subject("Welcome"));   // ❌ RASK093: does nothing
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
    await Mail.Send(Mail.Message.To(user.Email).Subject("Welcome"));
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

## Build errors from MSBuild

These come from Rask's other build targets rather than from an analyzer, so they have no severity to configure
and no quick-fix.

| Code | What happened | Fix |
| --- | --- | --- |
| `RASKSPA006` | A host references more than one WebAssembly client. | A host serves one client — give each its own host. |
| `RASKSPA008` | The WebAssembly client reported no target framework, so its bundle cannot be located. | Give the client project a `<TargetFramework>`. |
| `RASKSPA009` | The WebAssembly client targets several frameworks. | Give it exactly one, such as `net11.0-browser`. |
| `RASKDOM001` | Contributors only: the MDN snapshot could not be turned into element types. | The message names the member that collides. |
| `RASKDOM002` | Contributors only: an event name has a part the emitter cannot split into words. | Add the word to `DomEventEmitter.Words`. |

## RASK110

**More than one class implements `IRaskModel`** · Error

An app's model-wide rules — a string length, constraint names, what a delete does — live in
[one class implementing `IRaskModel`](data.md#app-wide-model-rules-one-iraskmodel), which the source generator finds
and names to the model registry. With two in one project there is no telling which rules the schema is built under,
so neither is applied and the build stops.

```csharp
public sealed class AppModel : IRaskModel { … }
public sealed class LegacyModel : IRaskModel { … }   // ✗ RASK110
```

```text
'global::Shop.AppModel' and 'global::Shop.LegacyModel' both implement IRaskModel, so Rask cannot tell which rules this app's model is built under; keep one and move the other's rules into it
```

**Fix:** move the rules of one into the other and delete it. Two assemblies that each bring one are refused at start,
naming both.

---

## RASK111

**A collection of value objects with no list behind it is a NULL column** · Warning

A [collection of value objects](domain.md#collections-of-values) is one JSON column, `NOT NULL` where the property is
non-nullable. EF Core reads and writes that column through an `IList<T>`, so the property needs one behind it. A get-only
`IReadOnlyList<T>` auto-property has none: its backing field is the same read-only view. Rask then maps it the old way,
as an owned collection, and EF Core makes that column NULL whatever the model says.

```csharp
public sealed class ReturnFiling : Aggregate<int>
{
    public IReadOnlyList<FiledFile> Files { get; private set; } = new List<FiledFile>();   // ⚠ RASK111
}
```

```text
'Files' on 'ReturnFiling' has no list behind it, so its JSON column is NULL whatever its nullability; hold it in a List<FiledFile> field behind the read-only view, or declare it List<FiledFile>
```

**Fix:** put a list behind the view, or make the property the list:

```csharp
private readonly List<FiledFile> _files = [];
public IReadOnlyList<FiledFile> Files => _files;                    // NOT NULL

public List<FiledFile> Files { get; private set; } = [];            // NOT NULL
```

A collection of plain values (`IReadOnlyList<string>`) is a primitive collection, which needs no list behind it and is
never reported.

---

## RASK112

**A relationship configured in `Configure` has no free name for its read navigation** · Error

A relationship the entity's own `Configure` declares — `b.HasOne<Provider>().WithMany().HasForeignKey(c => c.TransactionTypeId)`
— gives the read face a navigation to the configured target
([configured relationships](data.md#configured-relationships-win)). It is named after the id when the id ends in the
target's last word (`ForeignTaxProductCategoryId` → `ForeignTaxProductCategory`), after the target otherwise
(`TransactionTypeId` → `Provider`), and takes the other name when its own is held. When both are already a column, a
collection or another relationship's navigation — or two ids with no `Id` to name themselves by point at one target —
it gets no navigation, and the build stops rather than leave the join out without a word.

```csharp
public sealed class Note : Aggregate<int>
{
    public int Owner { get; private set; }
    public int Editor { get; private set; }

    public static void Configure(EntityTypeBuilder<Note> b)
    {
        b.HasOne<User>().WithMany().HasForeignKey(n => n.Owner);    // ✗ RASK112
        b.HasOne<User>().WithMany().HasForeignKey(n => n.Editor);   // ✗ RASK112
    }
}
```

```text
The relationship configured on 'Note.Owner' gets no navigation on 'NoteRead': 'User' is already taken by a member or another relationship. Rename the id, or what holds the name.
```

**Fix:** name the ids `OwnerId` and `EditorId` (keep the columns with `HasColumnName("Owner")`), which gives
`NoteRead.Owner` and `NoteRead.Editor`; or rename the member that already holds the name.

## RASK113

**More than one aggregate says `ITenant` or `IUser`** · Error

`Current.Tenant` and `Current.User` are generated from the one aggregate that says `ITenant` — or `IUser` — and typed as
its read face ([your tenant row](multi-tenancy.md#your-tenant-row-currenttenant)). With two, either answer would be a guess
at which row the work is for, so nothing is generated and the build stops. A user deriving from Rask.Auth's
`Authenticatable` says `IUser` already.

```csharp
public sealed class Tenant : Aggregate<int>, ITenant { }    // ✗ RASK113
public sealed class Partner : Aggregate<int>, ITenant { }   // ✗ RASK113
```

```text
'global::Shop.Partner' and 'global::Shop.Tenant' both say ITenant, so Rask cannot tell which one is this app's tenant, and Current.Tenant is not generated. Say ITenant on one of them.
```

**Fix:** keep the interface on the aggregate that IS the tenant.

## RASK114

**The tenant's or the user's key cannot be the current one** · Error

The framework carries the current tenant and user as a `Guid` and reads the row back by the key that `Guid` stands for:
a `Guid`, a `string` holding one, or the `int` or `long` it carries (`Tenant.Use(42)`). The marked type must be an
aggregate with one such key. The tenant's key must also be the one its `PerTenant` tables keep in `TenantId` — a `Guid`
(or `string`) tenant for `Guid` columns, the same number type for number columns — or a filter and the row would name
different tenants.

```csharp
public sealed class Tenant : Aggregate<Guid>, ITenant { }          // ✗ RASK114
public sealed class Invoice : Aggregate<int, PerTenant<int>> { }   //   keeps its tenant as int
```

```text
'Tenant' says ITenant, but its key is Guid and 'Invoice' keeps its tenant as int; give the tenant the key its tables keep, or the tables the tenant's
```

**Fix:** key the tenant like its tables (`Aggregate<int>` here), or the tables like the tenant.

## RASK115

**Tenant-scoped tables and no tenant aggregate** · Warning

A project with `PerTenant` tables and no aggregate that says `ITenant` has no `Current.Tenant`. It used to be the
tenant's `Guid`; it is now the app's own tenant row, generated once one aggregate says so.

```csharp
public sealed class Invoice : Aggregate<int, PerTenant<int>> { }   // ⚠ RASK115
```

```text
'Invoice' is PerTenant, but no aggregate in this project says ITenant, so there is no Current.Tenant. Say it on the tenant aggregate: public sealed class Tenant : Aggregate<int>, ITenant.
```

**Fix:** say `ITenant` on the tenant aggregate. A project whose tenant lives in another assembly (a feature library)
silences it with `<NoWarn>$(NoWarn);RASK115</NoWarn>`.

## RASK116

**Test is not named as a sentence** · Warning (off unless the app sets `<RaskStrict>`)

A test's name is what the runner prints when it fails, so it says what the test proves: at least three words joined
by underscores, most of them lower-case prose, and none of the scaffolding words `Should`, `Given`, `When`, `Then`,
`Test`, `Returns`, `Throws`. Names the test is about (`Cart`, `OnMount`) keep their casing and stay the minority. It
checks every xUnit `[Fact]`/`[Theory]`, NUnit `[Test]`/`[TestCase]`/`[TestCaseSource]` and MSTest `[TestMethod]`.

The rule ships switched off. [`<RaskStrict>`](code-analysis.md#strict-mode-for-your-app-raskstrict) turns it on in an
app's test projects; `dotnet_diagnostic.RASK116.severity = warning` in an `.editorconfig` turns it on anywhere.

```csharp
[Fact] public void AddItem() { }                        // ⚠ RASK116
[Fact] public void Should_add_the_item() { }            // ⚠ RASK116 — scaffolding word "Should"
[Fact] public void Add_puts_the_item_in_the_cart() { }  // ✓
```

```text
'AddItem' does not read as a sentence: fewer than three words — name it after what it proves, like Remember_loads_once_then_serves_from_the_cache
```

**Fix:** rename the test after what it proves.

## RASK117

**A cached fragment holds something interactive** · Error

[`Cached`](cached-fragments.md) stores the markup it drew and serves it to every later request as plain HTML. An
event handler (`OnClick`, `OnSubmit`, any `On…` step), a binding (`Bind`) or an element reference (`Ref`) written into
its children would reach the next visitor dead — nothing would be listening.

```csharp
Cached.Name("cart")[
    Button.OnClick(AddToCart)["Add"]          // ✗ RASK117
]
```

```text
'OnClick' inside Cached[ … ] would be served to later visitors as static HTML, where nothing answers it; move it outside the cached fragment
```

**Fix:** keep only static markup inside the fragment and put the interactive part next to it:

```csharp
Div[
    Cached.Name("cart-summary")[CartSummary],
    Button.OnClick(AddToCart)["Add"]
]
```

The analyzer sees what is written in the fragment's own children. A handler inside a component the fragment wraps is
refused at run time instead, by the first render that would store it, with the same advice.

## RASK118

**A chain step is called on a component type outside a markup host** · Warning

A component is built from its **chain entry** — the member named after it — and entries are members of a component
or a [markup host](#rask043) only. In any other class (a job, a mail sender, a domain-event handler) the name binds
to the component **type**, so the first step reads as an instance member asked of a type and the compiler says
**CS0120** ("An object reference is required for the non-static field, method, or property"), which reads like a
`static` problem and names neither Rask nor the fix:

```csharp
public sealed class SendReceipt(Order order) : IJob
{
    public Task Run() => Mail.Send(Email
        .To(order.Email)
        .Subject("Your receipt")
        .Body(OrderReceipt.OrderId(order.Id)));   // ✗ RASK118 — CS0120
}
```

```text
'OrderReceipt.OrderId' asks the component type 'OrderReceipt' for its step 'OrderId', so this does not compile (CS0120): 'OrderReceipt' is a chain entry only inside a component or a markup host, and 'Shop.SendReceipt' is neither — mark 'Shop.SendReceipt' '[Rask.Core.RaskMarkup]' and make it 'partial', or derive it from 'Rask.Core.RaskMarkup' when its base is free
```

**Fix:** make the class a markup host — `[RaskMarkup]` on a `partial` class puts every entry in scope (when its base
slot is free the generator gives it `: RaskMarkup`, so this costs nothing extra):

```csharp
using Rask.Core;

[RaskMarkup]
public sealed partial class SendReceipt(Order order) : IJob
{
    public Task Run() => Mail.Send(Email
        .To(order.Email)
        .Subject("Your receipt")
        .Body(OrderReceipt.OrderId(order.Id)));   // ✓
}
```

**Quick-fix available** — the IDE lightbulb adds `[Rask.Core.RaskMarkup]` and `partial` (to every enclosing type
too, which a nested host needs). A static member reached through the type (`OrderReceipt.Url()`, a constant) and
`nameof(OrderReceipt.OrderId)` are legal and never reported. Suppress with `#pragma warning disable RASK118` /
`.editorconfig` (`dotnet_diagnostic.RASK118.severity = none`).

## RASK119

**Redundant `StateHasChanged` in `OnMount`/`OnUpdated`** · Warning

`OnMount` and `OnUpdated` run in the render walk, *before* the component renders: what a hook with no `await` sets
is drawn by that render. A hook that awaits is rendered again after each `await` and once more when it completes.
So a `StateHasChanged()` that no `await` follows asks for a render that is already coming. Rask's counterpart of
Blazor's BL0012 ([RASK026](#rask026) is the same mistake inside an event callback).

```csharp
protected override async Task OnMount()
{
    _orders = await Order.Where(o => o.Open).ToList();
    StateHasChanged();                    // ⚠ RASK119
}
```

```text
StateHasChanged is redundant here — Rask renders this component after 'OnMount' returns, and no await follows the call; remove it
```

**Not reported:** a call with an `await` still to come — before one, or inside a loop that awaits — which can paint
an intermediate state; a call inside a lambda or a local function (a timer, an event subscription), which runs on its
own schedule; a call on another component; and `OnFirstRender`/`OnRendered`, which are not rendered again when they
finish without yielding, so a call there can be needed.

**Fix:** delete the call (**quick-fix available**).

## RASK120

**An event callback captures a `for` loop's variable** · Warning

A `for` loop has **one** variable for all its passes — unlike `foreach`, whose variable is fresh per pass — and a
lambda captures the variable, not its value. An event callback runs when the event fires, long after the loop has
finished, so every row's handler sees the value the loop ended on: here every button removes the item at
`_items.Count`, which throws. Rask's counterpart of Blazor's BL0014.

```csharp
for (var i = 0; i < _items.Count; i++)
{
    rows.Add(Button.OnClick(() => _items.RemoveAt(i))["Remove"]);   // ⚠ RASK120
}
```

```text
The 'OnClick' callback reads 'i', the for loop's variable: it runs after the loop has moved on, so it sees the last value, not this pass's; copy it into a local inside the loop ('var item = i;') or loop with foreach
```

**Not reported:** a lambda that is not handed to a callback step (`On…`, `AfterBind`) — a LINQ `Where` runs on the
spot and reads the right value; a variable the loop's increment does not write; and a `foreach` variable.

**Fix:** copy the value into a local declared inside the loop, or build the rows from the items themselves:

```csharp
for (var i = 0; i < _items.Count; i++)
{
    var row = i;
    rows.Add(Button.OnClick(() => _items.RemoveAt(row))["Remove"]);
}

// or, with nothing to copy:
Ul[_items.Select(item => Li.Key(item.Id)[Button.OnClick(() => _items.Remove(item))["Remove"]])]
```

## RASK121

**A `[JSInvokable]` method is not `public`** · Warning

Both hosts answer the browser's `DotNet.invokeMethodAsync` through `Microsoft.JSInterop`'s dispatcher, which looks
only at **public** methods. A private, protected or internal `[JSInvokable]` compiles, and the browser's call to it
then fails as "not found". Rask's counterpart of Blazor's BL0015, for the projects that one does not reach — a
WebAssembly app and a library on `Rask` alone. A project that sees the ASP.NET components (a Server app) already has
BL0015 from the framework, so RASK121 stays quiet there and a method is never reported twice.

```csharp
internal static class ScannerInterop
{
    [JSInvokable("RaskScanned")]
    private static Task Scanned(int id, string code) => …;   // ⚠ RASK121
}
```

```text
'Scanned' is [JSInvokable] but not public: the browser's call reaches only public methods, so it fails at run time; make it public
```

**Fix:** make the method `public` (**quick-fix available**). The class around it can stay `internal`: the dispatcher
looks at every type of the assembly, and only the method has to be public.

## RASK122

**The signed-in user is kept without following `IUserProvider.Changed`** · Warning

`IUserProvider.Current` is who is signed in **now**. A copy of it in a field or property — or of something read from
it, a name, a role check — is who was signed in when the copy was taken. Signing in, out or as someone else changes
the provider and raises `Changed`; a type that keeps the copy without listening goes on showing the old user. Rask's
counterpart of Blazor's BL0013.

```csharp
public sealed partial class Greeting(IUserProvider users) : Component
{
    private string? _name;

    protected override async Task OnMount() => _name = users.Current.Identity?.Name;   // ⚠ RASK122

    protected override Component? Render() => P["Hello, ", _name];
}
```

```text
'_name' keeps IUserProvider.Current, but 'Greeting' never subscribes to IUserProvider.Changed, so it goes on showing whoever was signed in when it was read; read 'Current' where it is used, or subscribe to 'Changed' and read it again
```

**Not reported:** reading `users.Current` where it is used (`Render`, a handler) and a copy in a local; a lambda kept
in a field, which reads it again each time it runs; a type that subscribes to `IUserProvider.Changed` anywhere; and a
type that is an `IUserProvider` itself.

**Fix:** read it where it is shown — or, for a gate, use [`Authorize`](authentication.md), which follows `Changed`
for you:

```csharp
protected override Component? Render() => P["Hello, ", users.Current.Identity?.Name];
```

A copy that has to be kept is refreshed from `Changed` (subscribe in `OnMount`, unsubscribe in `OnUnmount`):

```csharp
protected override async Task OnMount()
{
    _name = users.Current.Identity?.Name;
    users.Changed += OnUserChanged;
}

protected override async Task OnUnmount() => users.Changed -= OnUserChanged;

private void OnUserChanged(object? sender, EventArgs e)
{
    _name = users.Current.Identity?.Name;
    StateHasChanged();
}
```

## RASK123

**An `[OffThread]` message cannot run in a Web Worker** · Error

In a browser app an [`[OffThread]`](cqrs.md#off-the-pages-thread--offthread) query or command runs in a Web Worker
beside the page: a second .NET runtime with the app's Rask.Cqrs handlers and nothing of the page — no DOM, no
`IJSRuntime`, no `ElementRef`, no `Rask.Ui` service, nothing Program.cs registered. The message and its result cross in
the generated wire encoding. So two things that would only fail once the worker ran the handler are reported here:

```csharp
[OffThread]
public sealed record ReadSheet(byte[] File) : IQuery<ISheet>;          // ✗ RASK123 — an interface has no wire encoding

public sealed class ReadSheetHandler(IJSRuntime js)                     // ✗ RASK123 — IJSRuntime exists only on the page
    : IQueryHandler<ReadSheet, ISheet> { … }
```

```text
'Demo.ReadSheet' is [OffThread], so its handler runs in a Web Worker beside the page, but its handler 'ReadSheetHandler' asks for 'Microsoft.JSInterop.IJSRuntime', which exists only on the page — pass what the handler needs in the message, and do the page's part with its result
```

**Fix:** answer with a record the codec can encode (the shapes [RASK053](#rask053) lists), and give the handler what it
needs in the message instead of a page service — do the page's part (a toast, a focus, a download) with the result,
after the `await`. A handler that does not need to leave the page's thread does not need `[OffThread]`.

## RASK124

**An event's handlers disagree on who may publish it** · Warning

Who may publish an event over the wire (`IRemoteDispatch`, a browser's `Publish`) is read off its handlers, and a
publish runs every handler the event has. So the endpoint asks for what each of them asks — every policy, every roles
set — and lets an anonymous caller in only when **all** of them are `[AllowAnonymous]`. Handlers that ask different
things are reported, because one of them was usually written for a different caller than the others:

```csharp
public sealed record OrderPlaced(Guid OrderId) : IEvent;                // ⚠ RASK124

[AllowAnonymous]
public sealed class ThankYouMail : IEventHandler<OrderPlaced> { … }

[Authorize(Roles = "admin")]
public sealed class ReserveStock : IEventHandler<OrderPlaced> { … }     // a remote publish needs "admin"
```

**Fix:** give the handlers the same attributes, or split the event so each publisher raises the one meant for it.

## RASK125

**An action with several `[Route]`s gets a client method for the first** · Warning

A verb attribute with no template of its own (`[HttpGet]`) takes the action's `[Route]`, and ASP.NET serves the action
at every `[Route]` it carries. A generated client method calls one URL, so it calls the **first** one declared, and
says so; the others still answer over plain HTTP.

```csharp
[HttpGet]
[Route("{id}")]                 // the client's PostsClient.Get(id) calls this one
[Route("by-id/{id}")]           // ⚠ RASK125 — served, but not by the client
public ActionResult<Post> Get(int id) => …;
```

**Fix:** give the verb attribute the template the client should call (`[HttpGet("{id}")]`), or keep one `[Route]` on the
action.

## RASK126

**Lazy-loading proxies on a context that does not allow lazy loading** · Warning

Rask.Data switches lazy loading off (`ChangeTracker.LazyLoadingEnabled = false`) on every context its interceptors
reach: a navigation read in a loop, in a component's render or in a form's validation is a query nobody wrote — N+1,
and invisible in the code. `UseLazyLoadingProxies()` on such a context still builds the proxies, which then load
nothing: the navigation reads null or empty, which looks like missing data.

```csharp
services.AddDbContextFactory<AppDbContext>((sp, o) => o
    .UseRaskSqlite(sp)
    .UseLazyLoadingProxies()                                   // ✗ RASK126
    .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
```

**Fix:** say what a read needs — `Include`, a projection, a read face — and drop the proxies. If the app really wants
lazy loading (discouraged), opt in on the same options, and the warning goes:

```csharp
    .UseLazyLoadingProxies()
    .AllowLazyLoading()                                        // the explicit opt-in, Rask.Data
```

See [data access](data-access.md#lazy-loading-is-off).

## RASK127

**A value object declared from a base record is made past its rules** · Error · quick-fix

A value object declared from `Value`, `Text`, `Number`, `Whole`, `Day` or `Range` is made through its generated
`Create`, `TryCreate` or `CreateOptional`, which check the rules it declares and name the field a refusal belongs to.
`new KmRange(…)` throws without that field, and a `with` copies a value past its rules — its copy constructor asks
none. Inside the value object's own type both are allowed, and tests are analyzed like any other code; EF Core loads
stored values without asking the rules and is not affected.

```csharp
public sealed partial record KmRange(int Start, int End) : Range(Start, End, Strict: true);

var km = new KmRange(10, 20);              // ✗ RASK127
var longer = km with { End = 30 };         // ✗ RASK127
var made = KmRange.Create(10, 20, "Km");   // ✓ — a refusal is shown under Km.End
```

**Fix:** call `KmRange.Create(…)` (**quick-fix available** — the lightbulb rewrites `new KmRange(a, b)`, or a
target-typed `new(a, b)`, into `KmRange.Create(a, b)`, and `km with { End = 30 }` into `KmRange.Create(km.Start, 30)`.
A `with` gets the fix only when every member it sets is a positional one and the copied value is a name, so reading
it once per member changes nothing; a `new` with an object initializer gets none).

## RASK128

**A column is configured differently from its value object** · Warning

A value object declared from `Text` or `Number` gives its column the length, precision and scale it declares. An
attribute on the property — `[MaxLength]`, `[StringLength]`, `[Precision]` — wins over that, and the entity's own
`Configure` runs after both and still wins, so an attribute or a `HasMaxLength`, `HasPrecision` or `HasColumnType`
that says otherwise leaves the column and the rule disagreeing: a value the rule accepts does not fit, or the column
keeps decimals the rule refuses. Only literal arguments are compared, on both sides — the value object's base
arguments and the call's or the attribute's — so the warning is never a guess; a length computed at run time is not
checked.

```csharp
public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);

[MaxLength(200)] public DriverName Driver { get; private set; }  // ⚠ RASK128 — the rule allows 1000
builder.Property(d => d.Driver).HasMaxLength(200);             // ⚠ RASK128 — the same, from Configure
builder.Property(d => d.Fuel).HasColumnType("decimal(18,4)");  // ⚠ RASK128 on a Number(Value, Scale: 2, Precision: 9)
```

**Fix:** change the value object's declaration, which the rule and the column then both follow, or drop the
attribute or the call.

## RASK129

**A value object declared from a base record is not `sealed partial`** · Error · quick-fix

Rask writes the other half of a record deriving from `Value`, `Text`, `Number`, `Whole`, `Day` or `Range` — `Create`,
`TryCreate`, `CreateOptional`, and the column facets — so the record has to be `partial`; it is the value itself,
never a base for another, so it has to be `sealed`. A type it is nested in has to be `partial` too. Nothing is
generated until it is.

```csharp
public record DriverName(string Value) : Text(Value, MaxLength: 1000);                 // ✗ RASK129
public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000);  // ✓
```

**Fix:** declare it `sealed partial` (**quick-fix available** — the lightbulb adds whichever is missing).

## RASK130

**An aggregate stores a bare value where a value object would hold its rules** · Info (Warning under `<RaskStrict>`) · quick-fix

A public property of an aggregate or entity typed `string`, a number, `DateTime`, `DateTimeOffset`, `DateOnly`,
`TimeOnly` or `Guid` (nullable or not) holds whatever it is given, and every place that sets it has to remember the
rules. A value object declared from a [base record](domain.md#declared-from-a-base-record) checks them as it is made,
gives its column its length and a bound input its `maxlength`. The framework's own members (`Id`, `Version`,
`CreatedAt`, `UpdatedAt`, `DeletedAt`, `TenantId`) are not reported, nor a `bool`, an enum, a strongly-typed id, a
value object, a collection, or a property computed from others.

A suggestion by default, so it never fails a build; an app that sets `<RaskStrict>` gets it as a warning
(`rask-strict.globalconfig`) — under `<RaskStrict>errors</RaskStrict>` too, where it stays a warning and does not fail
the build: moving a model onto value objects is done one aggregate at a time.

```csharp
public sealed class Delivery : Aggregate<Guid>
{
    public string Driver { get; private set; } = "";               // ℹ RASK130
    public DriverName Mate { get; private set; } = null!;          // ✓ — public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100);
}
```

**Fix:** declare a value object for the property and store that (**quick-fix available** — "Declare a value object
from its rules"). The lightbulb names the record after the type and the property, declares it from the base for what
the property holds — `Text` for a `string`, `Number` for a `decimal`, `Whole` for an `int` or a `long`, `Day` for a
`DateOnly` — with the arguments the rules already written for it say, types the property as it, and takes those rules
off the property:

```csharp
// before
[StringLength(200)] public string Name { get; private set; } = "";
static void Rules(InlineValidator<Supplier> rules) => rules.RuleFor(s => s.Code).NotEmpty().Length(3).Matches("^[A-Z]+$");

// after
public SupplierName Name { get; private set; } = null!;
public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200, Required: false);
// …and for Code: Text(Value, MaxLength: 3, MinLength: 3), the rule left as RuleFor(s => s.Code).Matches("^[A-Z]+$")
```

`[StringLength]`, `[MaxLength]`, `[MinLength]` and FluentValidation's `MaximumLength`, `MinimumLength` and `Length`
become `MaxLength`/`MinLength`; `[Range]`, `InclusiveBetween`, `GreaterThanOrEqualTo` and `LessThanOrEqualTo` become
`Min`/`Max`; `PrecisionScale(p, s, …)` becomes `Precision`/`Scale`. A text no rule says must be filled (`[Required]`,
`NotEmpty`, a minimum length) gets `Required: false`, so it still accepts an empty one; an optional property becomes an
optional value object and keeps its `NotEmpty`. A FluentValidation check no base argument says (`Matches`,
`EmailAddress`, `Must`) stays on the rule, for you to move into the value object. The fix is not offered where the
value object would not say what was written: a type no base record holds, a name already taken, two rules that
disagree, a conditional (`When`/`Unless`) rule, a rule in another file, or a DataAnnotations attribute no argument
carries (`[EmailAddress]`, one with its own `ErrorMessage`), which on a value object would refuse every value. Nor is it offered while an `AbstractValidator<T>` class rules the same property: that rule would not compile once the property holds a value object.

To keep a bare value on purpose, set `dotnet_diagnostic.RASK130.severity = none` for the file or the project.

## RASK131

**A property is named by `[DisplayName]`, which Rask never reads** · Warning

Rask has ONE way to name a field for its readers — a control's label, a `Ui.Filters` chip, a column header, the `{0}`
of a validation message, a FluentValidation message and a Rask.Data model's generated server checks — and it is
`[Display(Name = …)]` from `System.ComponentModel.DataAnnotations`, through its `ResourceType` for a translated name.
That is the attribute .NET's own `Validator` reads too. `System.ComponentModel.DisplayNameAttribute` (or a subclass
of it) is read by none of them, so a property carrying only that one is shown by its member name. Reported in any
project that references Rask, on any property.

```csharp
public sealed class SignUp
{
    [Required, DisplayName("Given name")] public string? First { get; set; }         // ⚠ RASK131 — "The First field is required."
    [Required, Display(Name = "Given name")] public string? Given { get; set; }      // ✓ — "The Given name field is required."
}
```

**Fix:** write `[Display(Name = …)]` instead (**quick-fix available** — the lightbulb rewrites `[DisplayName("X")]`
as `[Display(Name = "X")]`, adds `using System.ComponentModel.DataAnnotations;` where the file lacks it, and drops
`using System.ComponentModel;` when nothing else in the file uses it; it is withheld for a subclass of
`DisplayNameAttribute`, whose name is built however that subclass builds it).

---

## RASK132

**A value object's `Validate` is never asked: declare it from a base record** · Warning

A value object states its rules in its constructor: one [declared from a base record](domain.md#declared-from-a-base-record)
— `Text`, `Number`, `Whole`, `Day`, `Range`, or `Value` with its own `Rules()` — is checked as it is made, and a
form bound to the aggregate's generated model asks the same rules
[with nothing written on the field](domain.md#what-a-form-over-the-model-asks-by-itself). A `public static Validate`
beside the value it holds was the earlier way to say it, and nothing reads it any more: no form asks it, and
neither do the generated `Create(model)` and `Update(id, model)`. A rule that is never asked looks exactly like one
that was asked and passed, so a one-value value object an aggregate holds, not declared from a base record, with a
static `Validate` of any shape, is reported on that method with the declaration that would hold its rule:

```csharp
public readonly record struct DriverName(string Value)
{
    public static IEnumerable<string> Validate(string value) => Rules.Text(value, 100);   // ⚠ RASK132
}
```

> 'DriverName.Validate(string)' on value object 'DriverName' is never asked: no form and no generated write runs a
> value object's Validate, only the rules of one declared from a Rask base record — declare it 'public sealed
> partial record DriverName(string Value) : Text(Value, …);' with its rules as the base's arguments, or ': Value'
> with them in 'protected override IEnumerable<Problem> Rules()', and make it with 'DriverName.Create(…)'

**Fix:** declare it from the base that takes what it holds — `Text` for a `string`, `Number` for a `decimal`,
`Whole` for a `long`, `Day` for a `DateOnly` — with the rules as arguments, and move a rule no argument says into
`Rules()`; anything else derives from `Value`. Then make it with `X.Create(…)` ([RASK127](#rask127) points at each
`new X(…)` left behind):

```csharp
// before
public readonly record struct DriverName(string Value)
{
    public static IEnumerable<string> Validate(string value) => Rules.Text(value, 100);
}
Driver = new DriverName(name);

// after
public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 100);
Driver = DriverName.Create(name, nameof(Driver));
```

A rule that awaits something — a lookup — is not a value's: it belongs on the field (`.Validate(async v => …)`), on
the form, or in the aggregate's unique rule (`IsUnique("…")`), which a form over the generated model asks by itself.

**Not reported:** a value object declared from a base record, whatever else it declares; an instance `Validate`, or
one that implements an interface (`IValidatableObject`); a value object of several values; and any type that is
not a value object held by an aggregate — so a static method a hand-written form names on a field
(`.Validate(Rules.Destination)`) is left alone; a value object's own rule is its generated `X.Check`.

RASK102 said a `Validate` was not the shape of a rule while that convention was read; it is retired with it.

## RASK133

**An aggregate is written on a DbContext around its own operations** · Info (Warning under `<RaskStrict>`) · quick-fix

App code hands an aggregate to a `DbContext` — `db.Products.Add(p)`, `db.Update(order)`, `db.Remove(supply)`, their
`Range` and `Async` forms — and the same method saves the context (`SaveChanges`, `SaveChangesAsync`). That write goes
around everything the aggregate was drawn for: its [factory and operations](domain.md#operations-what-the-aggregate-does),
the roles their generated entries ask, and [the rules every save asks](domain.md#what-every-save-asks) of a `Find` and
`Save()`. The aggregate is [the transaction boundary](domain.md#the-transaction-boundary), so the write goes through it:

```csharp
db.Products.Add(Product.Place(name, price));     // ℹ RASK133
await db.SaveChangesAsync();

await Product.Place(name, price);                 // ✓ the factory's generated entry: roles, rules, one transaction
await Order.Ship(id, version, tracking);          // ✓ an operation's entry, in place of db.Update(order)
await Supply.Delete(id, version);                 // ✓ in place of db.Remove(supply)
await Db.Transaction(async () =>                  // ✓ a check and the writes it guards, by hand
{
    var order = await Order.Find(id, version) ?? throw new KeyNotFoundException();
    order.Ship(tracking);
    await order.Save();
});
```

Not reported: a context that only stages a write for its caller to save (no `SaveChanges` in the same method — a
generated write's `db:` works that way); a write of an entity that is not an aggregate; a seed (a method or a type
whose name says `Seed`, the work of `UseSeeding`/`UseAsyncSeeding`; `HasData` writes no `Add`); a migration (a
`Migrations` folder, a `Migration` or a `ModelSnapshot`); a test (a `*.Tests` assembly, a method or a class with a
test attribute); and generated code, Rask's own generated entries among it.

**Fix:** call the generated entry the message names — the factory's `X.Register(…)`, the operation's
`X.M(id, version, …)`, `X.Delete(id, version)` — or `Find`, the operation and `Save()` inside `Db.Transaction`. The
**quick-fix** is offered where the rewrite is mechanical: `db.Add(X.F(…))` followed at once by
`await db.SaveChangesAsync(…)` on a context local the method uses for nothing else, where `F` is a factory with a
generated entry. It calls `await X.F(…, cancellationToken: …)` — the token passed by name, which is what binds the
call to the entry rather than to the factory — and drops the context. A suggestion by default; a warning under
`<RaskStrict>`.

## RASK140

**Domain code reads the machine's clock** · Info (Warning under `<RaskStrict>`) · quick-fix

`DateTime.Now`, `DateTime.Today`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` or `.ToLocalTime()`
inside an aggregate, an entity or a value object declared from a [base record](domain.md#declared-from-a-base-record)
(and any type nested in one). The machine's clock is not the app's: a test's `Clock.Fake(at: …)` does not freeze it,
and its zone is the server's — a container's is UTC — not the one the app named with `AddRask(timeZone: …)` or
`c.TimeZone`. Rask's [`Clock`](domain.md#time-in-the-domain) is both.

```csharp
public sealed class Delivery : Aggregate<Guid>
{
    public DateTime Stamped { get; private set; }

    public void Stamp() => Stamped = DateTime.Now;        // ℹ RASK140
    public void Stamp2() => Stamped = Clock.Wall;         // ✓ the wall clock in the app's zone, frozen by Clock.Fake
}
```

**Fix:** read `Clock` (**quick-fix available** — every replacement keeps the expression's type: `DateTime.Now` →
`Clock.Wall`, `DateTime.Today` → `Clock.Wall.Date` (and `DateOnly.FromDateTime(DateTime.Today)` → `Clock.Today`),
`DateTime.UtcNow` → `Clock.Now.UtcDateTime`, `DateTimeOffset.Now`/`UtcNow` → `Clock.Now`, `x.ToLocalTime()` →
`TimeZoneInfo.ConvertTimeFromUtc(x, Clock.Zone)` and, on a `DateTimeOffset`, `TimeZoneInfo.ConvertTime(x, Clock.Zone)`;
`Clock` is written `global::Rask.Clock` where `using Rask;` is missing; a conditional `x?.ToLocalTime()` gets no fix).
`Clock.Wall` is the wall-clock time with `DateTimeKind.Unspecified`, where `DateTime.Now` said `Local`. Code outside
the domain — a page, a handler, a job — is not reported. A suggestion by default; a warning under `<RaskStrict>`.

## RASK141

**Domain code looks up a service** · Info (Warning under `<RaskStrict>`)

`Current.Service<T>()` inside an aggregate, an entity or a value object declared from a base record. The domain
decides from the state it holds and what its caller hands it: a service it reaches for itself is a dependency no
caller can see, one a test has to stand a whole scope up for, and one that throws outside a scope.

```csharp
public sealed class Order : Aggregate<Guid>
{
    public void Price(Sku sku) => Total = Current.Service<IPrices>().Of(sku);   // ℹ RASK141
    public void Price(Sku sku, Money price) => Total = price;                   // ✓ the caller looked it up
}
```

**Fix:** hand what the operation needs in as a parameter — the caller (a page, a handler) has the service — or
raise a domain event and let its handler use the service. No quick-fix: which of the two is the app's call.

## RASK142

**Database work run side by side inside a transaction** · Info (Warning under `<RaskStrict>`)

`Task.WhenAll` inside the work of a [`Db.Transaction(…)`](domain.md#a-check-and-the-writes-it-guards-dbtransaction)
over database work: an entity's generated reads and writes (`Order.Find`, `Tank.Count`, `order.Save()` …), a
Rask.Data call or an EF Core one — in the `WhenAll`'s arguments, a method handed on (`ids.Select(Tank.Find)`), or the
initializer of a local it names. The transaction's work runs on its one connection, which takes one command at a
time, so EF Core refuses the second command at run time ("a second operation was started on this context").
A `WhenAll` over anything else — HTTP calls, say — is not reported, nor one outside a transaction.

```csharp
await Db.Transaction(async () =>
{
    await Task.WhenAll(Tank.Count(), Pistol.Count());      // ℹ RASK142
    var tanks = await Tank.Count();                         // ✓ one after the other
    var pistols = await Pistol.Count();
});
```

**Fix:** await the reads one after the other. No quick-fix: what the code does with the results decides the shape.

## RASK143

**`[Key]` on an aggregate or an entity** · Info (Warning under `<RaskStrict>`)

`System.ComponentModel.DataAnnotations.KeyAttribute` on a member of a type that derives from `Entity<TId>` (an
aggregate included). Its key is the `Id` it inherits, and every generated read, write and route finds a row by it;
EF Core honours the attribute and keys the table on the marked member too.

```csharp
public sealed class Order : Aggregate<Guid>
{
    [Key] public OrderNumber Number { get; private set; } = null!;   // ℹ RASK143
}
```

**Fix:** remove the attribute. A value that may not repeat is a unique index with a message in the aggregate's own
`Configure` (`builder.HasIndex(o => o.Number).IsUnique("…")`, see
[what a unique index's violation says](data.md#what-a-unique-indexs-violation-says)); a child keyed by the pair of
aggregates it joins is [a key of several columns](domain.md#a-key-of-several-columns).

## RASK144

**`[Compare]` names a property the model does not have** · Info (Warning under `<RaskStrict>`)

`[Compare("X")]` on a property, where neither the type it is written on nor its bases has a property named exactly
`X`. `CompareAttribute` looks the other property up by name when it validates, and a name it does not find refuses
every value ("Could not find a property named X") — a form nobody can send. It matches properties only, by exact
case, as the attribute does at run time. Reported on any type.

```csharp
public sealed class SignUp
{
    public string Password { get; set; } = "";
    [Compare("password")] public string Again { get; set; } = "";          // ℹ RASK144 — the case differs
    [Compare(nameof(Password))] public string Again2 { get; set; } = "";   // ✓
}
```

**Fix:** name the property with `nameof(…)`, which the compiler checks.

## RASK150

**A required value object starts from a placeholder** · Warning

A non-nullable value-object property of an aggregate or an entity — declared from one of Rask's base records — with
an initializer that makes a value. The initializer is what every new row holds before anything sets it, so it is
what a new form over the generated model starts from (the field already says `Unknown`), and what the save's rules
are asked about when a path forgets to set it: a made-up value passing as a real one. A nullable property, a
get-only one (a constant of the type), one initialized with `null!`, and any type that is not an entity are left
alone.

```csharp
public sealed class Delivery : Aggregate<Guid, Model>
{
    public DriverName Driver { get; private set; } = DriverName.Create("Unknown");   // ⚠ RASK150
    public DriverName? Mate { get; private set; }                                     // ✓ optional
    public Litres Fuel { get; private set; } = null!;                                 // ✓ no value until one is given
}
```

> 'Driver' on 'Delivery' starts as a made-up 'DriverName': a new form shows it and the rules are asked about it —
> initialize it with 'null!' and set it in the factory

**Fix:** initialize it with `null!` and set it where the aggregate is made (its factory, or the generated
`Delivery.Create(model)`); the form then starts the field empty, and required. The quick-fix writes the `null!`.
A placeholder that stands for "none" — `Opening Start = Opening.None` over `Opening(DateTime? Date, decimal? Quantity)`
— is an optional value: declare it `Opening? Start` with no initializer. A row with every one of its columns `NULL`
loads it as `null` ([domain.md](domain.md#declared-from-a-base-record)).

## RASK165

**A configuration call the generators read is written where they cannot see it** · Warning

The generated model, writes and read face exist before any EF model is built, so three calls are read from the
source of the entity's own static `Configure`, as written there: `KeepEmpty(c => c.Member)` (an empty text kept as
`''`), `Columns(c => c.Member, "{Member}Km")` (the columns of a value object of several members), `Json(c => c.Member)`
(a value object kept whole in one JSON column, which the read face then leaves out) and
`IsUnique("message")` on an aggregate's index (a form asks the store before it saves). Written anywhere else — a
helper method, another class, `OnModelCreating`, an `IEntityTypeConfiguration` — or called as a static method, with
the member in a variable rather than a lambda, or with a pattern that is not a constant, the call still configures
the column, but the generated code acts as if it were never made: an emptied text is stored as `NULL`, the read
face keeps the default column names, a taken name is told only by the save.

```csharp
public static void Configure(EntityTypeBuilder<Company> b)
{
    b.KeepEmpty(c => c.VatNumber);                       // ✓
    Texts.KeepAll(b);                                    // the helper's KeepEmpty: ⚠ RASK165
    var pattern = "{Member}Km";
    b.Columns(c => c.Km, pattern);                       // ⚠ RASK165 — not a constant
    b.HasIndex(c => c.Name).IsUnique("That name is taken.");   // ✓
}
```

**Fix:** write the call directly in the entity's own static `Configure`, the member as a lambda and the pattern as a
constant.

## RASK170

**An aggregate calls an operation's generated entry by its bare name** · Warning

Every public method of a `partial` aggregate gets a static entry of the same name —
`Supply.Change(id, version, details, parts)` — that loads the aggregate, runs the method and saves it. Inside the
aggregate a call by the bare name binds to that entry whenever the arguments fit, which is almost never what was
meant: it loads ANOTHER copy, changes and saves that one in a transaction of its own, and leaves the aggregate in hand
as it was.

```csharp
public sealed partial class Supply : Aggregate<Guid, Versioned>
{
    public void Change(string note) => Note = note;
    public Task Again(Guid other) => Change(other, 0, "again");   // ⚠ RASK170
    public void Twice(string note) { Change(note); this.Change(note); }   // ✓ the method
    public Task Other(Guid other) => Supply.Change(other, 0, "x");      // ✓ plainly the entry
}
```

**Fix:** call the method on the aggregate in hand (`this.Change(…)`), or name the type (`Supply.Change(…)`) when
another copy really is what is meant.

## RASK171

**An operation's parameter takes a name its generated entry needs** · Warning

The entry of an operation takes `id`, `version` and `cancellationToken` around the method's own parameters. An
operation whose parameter has one of those names would make the entry ambiguous, so it gets none.

```csharp
public void Bump(int version) => Level = version;   // ⚠ RASK171: no Tag.Bump(id, version, …)
```

**Fix:** rename the parameter.

## RASK172

**The roles of an operation are written where its generated entry cannot read them** · Error

`b.Operation(x => x.M, roles: …)` is read from the aggregate's own static `Configure` where the entry is generated,
as `[Authorize(Roles = …)]` is read by the compiler. Written in a helper, another class, `OnModelCreating` or an
`IEntityTypeConfiguration`, called as a static method, with the operation in a variable, or with roles that are no
constant, it would leave the entry asking nothing — open to everyone — so the build stops. Roles that are no constant
leave no entry at all.

```csharp
private static readonly string Admins = "Admin";
public static void Configure(EntityTypeBuilder<Supply> b)
{
    b.Operation(s => s.SetOpening, roles: Roles.Admin);   // ✓ a const string
    b.Operation(s => s.Close, roles: Admins);              // ⛔ RASK172 — not a constant
}
```

**Fix:** write the call directly in the aggregate's own static `Configure`, the operation as a lambda and the roles as
a `const string` or a literal.

## RASK173

**A factory is public, so its generated entry cannot be called** · Warning

A static method of a `partial` aggregate that returns it is its factory, and gets a public static entry of the same
name that builds it, asks its roles and rules, saves it in one transaction and returns its id. The entry takes the same
parameters, so a public factory wins every call — `await Supply.Register(…)` would not even compile — and no entry is
generated for it.

```csharp
public static Supply Register(CompanyId company, SupplyDetails details) => …;    // ⚠ RASK173
private static Supply Register(CompanyId company, SupplyDetails details) => …;   // ✓ SupplyId id = await Supply.Register(…)
```

**Fix:** make the factory `private` or `internal`. Code that only wants the aggregate built, not saved, calls it from
inside the aggregate (or the assembly, when it is `internal`).

## RASK139

**A `Rules` method is not the in-type FluentValidation convention** · Warning

FluentValidation rules written inside a type — an aggregate, an entity, a form model or a value object — are found by
their shape alone: `static void Rules(InlineValidator<T> rules)`, any accessibility, where `T` is the type the method is
declared in and neither the method nor the type is generic (the registration could not name it). A `Rules` method that
takes an `InlineValidator` in any other shape reads like rules and is never registered, so they never run. Reported
only when the app references FluentValidation.

```csharp
public sealed partial class Supplier : Aggregate<SupplierId>
{
    void Rules(InlineValidator<Supplier> rules) { … }                     // ⚠ RASK139 — not static
    static void Rules(InlineValidator<Order> rules) { … }                 // ⚠ RASK139 — validates another type
    static void Rules(InlineValidator<Supplier> rules) =>                 // ✓
        rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);
}
```

**Fix:** declare it `static void Rules(InlineValidator<T> rules)` on the type it validates, or, for a generic type, write
an `AbstractValidator<T>` for each closed type instead.

## RASK181

**A FluentValidation rule rules a value object** · Warning

A value object declared from a `Rask.Values` base record checks its own rules as it is made, wherever that is. A
FluentValidation rule on an aggregate's or entity's value-object member — in its in-type `Rules` or in an
`AbstractValidator<T>` — restates them on that one owner: the two drift apart, and a value made anywhere else is never
asked. `NotNull` and `NotEmpty`, which say only that the member is given, are the owner's to say and are not reported.

```csharp
public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200);

static void Rules(InlineValidator<Supplier> rules)
{
    rules.RuleFor(s => s.Name).Must(n => !n.Value.Contains('@'));   // ⚠ RASK181
    rules.RuleFor(s => s.Contact).NotNull();                         // ✓ — whether it is given is the aggregate's
}
```

**Fix:** move the rule into the value object — an argument of its base record, its `Rules()` override, or its own static
`Rules(InlineValidator<SupplierName> rules)`.

## RASK182

**A FluentValidation rule disagrees with its column** · Warning

The FluentValidation analogue of [RASK128](#rask128). A `[MaxLength]`, `[StringLength]` or `[Precision]` on a property —
and, inside a value object, the `MaxLength`, `Precision` and `Scale` it hands its `Text` or `Number` base — gives the
column its length and precision. A `MaximumLength`, `Length` or `PrecisionScale` that says otherwise leaves the rule and
the column disagreeing: a value the rule accepts does not fit, or one that fits is refused. Only constants are
compared, on both sides.

```csharp
[MaxLength(100)] public string Name { get; private set; } = "";
rules.RuleFor(s => s.Name).MaximumLength(200);                      // ⚠ RASK182 — the column holds 100

public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100)
{
    static void Rules(InlineValidator<SupplierName> rules) =>
        rules.RuleFor(n => n.Value).MaximumLength(200);             // ⚠ RASK182 — its base says 100
}
```

**Fix:** make the two agree — or drop one: the attribute (or the base argument) already gives the rule the column has.

## RASK183

**A value object declares its rules twice** · Error

A value object states the rules its base record cannot in one of two ways: a hand-written
`protected override IEnumerable<Problem> Rules()`, or FluentValidation's `static void Rules(InlineValidator<T> rules)`,
whose generated half overrides `Rules()` to run them. With both, the record would override `Rules()` twice.

```csharp
public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 200)
{
    protected override IEnumerable<Problem> Rules() { … }
    static void Rules(InlineValidator<SupplierName> rules) { … }    // ⛔ RASK183
}
```

**Fix:** keep one. A hand-written `Rules()` is first-class; FluentValidation needs `Rask.Validation.FluentValidation`.

## RASK174

**Two operations take the same parameters, so neither gets a `Form.For`** · Info

`Form.For(Driver.Rename, id)` picks the operation by the parameters of its generated entry, since a method group
carries no name a call can be told by. Two operations whose entries take the same types would make every such call
ambiguous, so neither gets a form.

```csharp
public void Rename(DriverName name) { … }        // ℹ RASK174
public void SetFullName(DriverName name) { … }   // ℹ RASK174
```

**Fix:** give one of them a value object of its own for what it takes (`FullName`), which is also the clearer domain.

## RASK175

**An aggregate stores a password** · Error

A value object declared from `Rask.Values.Password` holds a password as it was typed. An aggregate or an entity that
keeps one — as a property, inside a value object it holds, or in a list — would write it to a column and show it on the
generated read face. Found however deep it sits; a computed property is not state and is not reported.

```csharp
public sealed partial record SignInPassword(string Value) : Password(Value, MinLength: 8);

public sealed class User : Aggregate<Guid>
{
    public SignInPassword Password { get; private set; }   // ⛔ RASK175
    public string PasswordHash { get; private set; } = ""; // ✓
}
```

**Fix:** hash the password where it is checked and store the hash; keep the `Password` value object on the form or
the command that brings it in.

## RASK176

**A password is sent back** · Error

A `Password` value object is input only: a form or a command brings it to the server. A query's result
(`IQuery<T>`), a command's result (`ICommand<T>`), a subscription's item (`ISubscription<T>`) and an event (`IEvent`)
are written outward — to the browser, a log, another service — so none of them may hold one, at any depth.

```csharp
public sealed record SignIn(ContactEmail Email, SignInPassword Password) : ICommand<Token>;   // ✓ it comes in
public sealed record Login(ContactEmail Email, SignInPassword Password);
public sealed record FindLogin(Guid Id) : IQuery<Login>;                                      // ⛔ RASK176
```

**Fix:** leave the password out of what is sent back; send what the reader needs instead (a token, a flag).

## RASK177

**Code written for one database** · Info (Warning under `<RaskStrict>`)

App code that only one database understands: an API of the SQL Server, PostgreSQL or SQLite provider or of its
driver (`IncludeProperties`, `IsClustered`, `SqlException`, `PostgresException`, `SqliteConnection`, …),
`EF.Functions.*`, raw SQL (`FromSql`/`FromSqlRaw`/`FromSqlInterpolated`, `ExecuteSql*`, `SqlQuery`/`SqlQueryRaw`,
`MigrationBuilder.Sql` outside a migration) and SQL in the model (`HasColumnType`, `SetContainerColumnType`, `UseCollation`,
`HasDefaultValueSql`, `HasComputedColumnSql` and `HasFilter` given SQL, `ToSqlQuery`). An app's tests run on SQLite
in memory ([testing](testing.md)), and Rask proves what it translates behaves the same on SQLite, SQL Server and
PostgreSQL; code written for one of them is outside that proof — green on the database the tests run on, different
on the one production runs on.

```csharp
b.Property(s => s.Price).HasColumnType("decimal(18, 9)");                     // ℹ RASK177
public sealed partial record UnitPrice(decimal Value) : Number(Value, Scale: 9); // ✓ the scale on every database

db.Drivers.OrderBy(d => EF.Functions.Collate(d.Name, "Hungarian_100_CI_AS"));  // ℹ RASK177
db.Drivers.OrderBy(d => d.Name.InTextOrder());                                 // ✓ in the order OrderTextBy names

await db.Database.ExecuteSqlRawAsync("EXEC sp_getapplock @Resource = 'pump'"); // ℹ RASK177
await using var turn = await Db.Lock("pump").For(30.Seconds);                  // ✓ held on every database
```

**Fix:** the message names the neutral form. `EF.Functions.Like` → `Contains`/`StartsWith`/`EndsWith`;
`EF.Functions.Collate` and `UseCollation` → `OrderTextBy(…)` once and `InTextOrder()` where a query sorts, and `x.Loosely()` where it searches case and accents aside;
`EF.Functions.DateDiff*` → `end - start`; `HasColumnType` → the value object's `Scale`/`MaxLength`, or `HasPrecision`
/ `HasMaxLength`; `SetContainerColumnType` → nothing (Rask keeps a JSON column `nvarchar(max)` on SQL Server at every level); `HasDefaultValueSql` → [`b.Default(…)`](data.md#computed-columns-defaults-and-filtered-indexes-in-c) (`b.StoredDefault(…)` for a value the value object refuses, `() => Clock.Wall` for `CURRENT_TIMESTAMP`);
`HasComputedColumnSql` → `b.Computed(…)`; `HasFilter` → `HasIndex(…).Where(x => …)`; `IncludeProperties` →
`.Covering(…)`; raw SQL → a LINQ query, a change through the aggregate, `Db.Lock(name)` for `sp_getapplock` and
`X.Where(…).ForUpdate()` inside `Db.Transaction` for `UPDLOCK`/`HOLDLOCK`/`FOR UPDATE`; a driver's exception → `IsUnique("…")` on the index and the
message on `OnDelete(…)`, which every database turns into the same `RaskValidationException`.

**Not reported:** a migration — a type derived from `Migration` or `ModelSnapshot`, or any file under a `Migrations`
folder — and generated code, which are written for the database they migrate; and **the call that selects the
provider**, `UseSqlServer`, `UseAzureSql`, `UseAzureSynapse`, `UseNpgsql` or `UseSqlite` on a
`DbContextOptionsBuilder`, with everything in its arguments: that is the one place an app names its database
(`UseRaskDatabase(sp)` names none, and reads it from `Rask:Database:Provider`). Rask's own packages are compiled apart
from the app, so their provider code is not seen. A suggestion by default; a warning under `<RaskStrict>`, so an error
at `<RaskStrict>errors</RaskStrict>`. No quick-fix: each replacement is a different shape.

## RASK178

**A transaction writes two aggregates without saying why** · Info (Warning under `<RaskStrict>`)

A [`Db.Transaction(…)`](domain.md#a-check-and-the-writes-it-guards-dbtransaction) whose work — nested lambdas
included — writes two different aggregates and states no `manyAggregates:` reason. A write is a generated one: an
aggregate's `Create`, `Update` or `Delete`, an instance's `Save()` or `Delete()`, or an operation's entry (the static
`Supply.Change(id, version, …)` beside the method `Change`, or the factory entry `Supply.Register(…)`). Reads (`Find`,
`Where`, `Count`, `Any`, `Model`, the dry runs) are not writes. An aggregate is the transaction boundary: two
committed together is a decision, and the reason is written where it is made. At run time
`RaskDataOptions.OneAggregatePerTransaction` meets the same transaction (a warning in the log, or a
`ManyAggregatesException` under `AggregatePolicy.Throw`, which a `TestDatabase` runs at).

```csharp
await Db.Transaction(async () =>                                              // ℹ RASK178
{
    await filling.Save();
    await withdrawal.Save();
});

await Db.Transaction(async () =>                                              // ✓ the reason, written down
{
    await filling.Save();
    await withdrawal.Save();
}, manyAggregates: "The IBC filling and its withdrawals are only valid together");
```

**Fix:** write one aggregate per transaction — the other through a domain event (`static Task On(E e)`) — or state
why they are only valid together. **Quick-fix:** inserts `manyAggregates: "TODO: why"` right after the work, named in
its own position, so an isolation passed after it still binds (`Db.Transaction(work, manyAggregates: "TODO: why",
IsolationLevel.Serializable)`); the placeholder is yours to replace.

**Not reported:** one aggregate type written twice — the analyzer cannot tell two rows apart; the run-time check
counts them by key — and a write in a method the work calls, which the analyzer does not follow.

## RASK180

**Children are added or removed outside their aggregate** · Error

`Children<TEntity, TSpec>` is read by everyone and changed only by the aggregate that declares it: through `Sync`, or
through `Add` and `Remove` in its own methods, where its rules are. C# has no access level for "only my holder", so
`Add` and `Remove` are public and this error says it instead. A call is allowed when its receiver is the property (or
field) of the type the call is written in — or of a type it is nested in: `Parts.Add(part)`, `this.Parts.Remove(p)`,
`other.Parts.Add(p)` between two of the same aggregate, and `Parts.Add` handed over as a delegate. Children held in a
local or returned from a method cannot be traced to their aggregate, so they are refused even inside it.

```csharp
public sealed class Supply : Aggregate<SupplyId>
{
    public Children<SupplyPart, SupplyPartSpec> Parts { get; } = new();
    public void Take(SupplyPart part) => Parts.Add(part);   // ✓ the aggregate's own method
}

supply.Parts.Add(part);                                      // ✗ RASK180, from a page or a handler
supply.Take(part);                                           // ✓
```

**Fix:** call the aggregate's method that adds or removes the child, or write one.

## RASK184

**An aggregate refuses with an exception no form can show** · Info (Warning under `<RaskStrict>`) · quick-fix

`throw new InvalidOperationException(…)` or `throw new ArgumentException(…)` inside an aggregate or an entity (and any
type nested in one). A rule that says no is a sentence for the person at the screen, and a form shows a
[`Refuse`](domain.md#refusing-an-operation-refuse) under the field it names or above the form; those two exceptions are a
failed request instead — an error toast that says nothing useful, or an error page. Only those two exact types are
reported: `ArgumentNullException`, `ArgumentOutOfRangeException` and the other subclasses guard against a caller's
mistake, not a rule.

```csharp
public void Ship()
{
    if (Lines.Count == 0) throw new InvalidOperationException("Nothing to ship.");   // ℹ RASK184
    Refuse.When(Lines.Count == 0, "Nothing to ship.");                              // ✓ shown above the form
}
```

**Fix:** refuse (**quick-fix available**): `if (c) throw new InvalidOperationException(m);` — alone, or alone in a
block, with no `else` — becomes `Refuse.When(c, m);`, `if (!c) …` becomes `Refuse.Unless(c, m);`, and
`if (c) throw new ArgumentException(m, nameof(x));` becomes `Refuse.When(c, () => x, m);`, shown under the field `x`
fills. Any other throw of one — a throw expression, an `if` with an `else` — becomes
`throw new RaskValidationException(m)` (an inner exception is kept). `using Rask.Data;` / `using Rask.Wire;` is added
where it is missing. An exception built with no message has no sentence to show and gets no fix. A suggestion by
default; a warning under `<RaskStrict>`.

## RASK185

**An aggregate is loaded outside the transaction that changes it** · Info (Warning under `<RaskStrict>`) · quick-fix

The work of a [`Db.Transaction(…)`](domain.md#a-check-and-the-writes-it-guards-dbtransaction) uses an aggregate held
in a local declared before the block. That read was not part of the transaction: the isolation the block asks for
does not cover it, so a check the work makes on the copy is a check on what was there before the transaction began —
and on a retrying context the work runs again on the same copy, which on a `Versioned` aggregate is a conflict no
retry gets past. Loaded inside the block, every attempt reads what it changes. A local the work assigns itself is not
reported, nor an aggregate a parameter hands in, nor a nested block's (it joins the outer one).

```csharp
var supply = (await Supply.Find(id))!;                     // ℹ RASK185 where the block uses it
await Db.Transaction(async () =>
{
    supply.Distribute(rows);
    await supply.Save();
});

await Db.Transaction(async () =>
{
    var supply = (await Supply.Find(id))!;                 // ✓ read inside the transaction
    supply.Distribute(rows);
    await supply.Save();
});
```

**Fix:** load it inside the block (**quick-fix available** — the declaration moves to the top of the work, and an
expression-bodied work becomes a block, a `return` when it returns a value). The fix is offered when the declaration
declares only that local, sits before the transaction in the same block, and nothing outside the work reads the local;
a load that awaits needs an `async` work. A suggestion by default; a warning under `<RaskStrict>`.

## RASK186

**An update runs an operation that has a generated entry** · Info (Warning under `<RaskStrict>`) · quick-fix

`X.Update(id, x => x.M(…))` where `M` is an [operation](domain.md#an-operation-run-as-one-write) of a `partial`
aggregate, which has a generated entry `X.M(id, version, …)`. The entry loads, runs and saves in one transaction just
as the update does — and also asks the roles the aggregate's `Configure` names for the operation, which the update
does not. It is the call the operation's form makes, so code and form go through the same door.

```csharp
await Supply.Update(id, s => s.Change(details), version: read);   // ℹ RASK186
await Supply.Change(id, read, details);                           // ✓ its roles asked
```

Reported only when the rewrite means the same: the lambda does nothing but call the operation on the aggregate it is
handed, with arguments that do not read it; no `db:` is given; a `Versioned` aggregate is given an `int` version (the
entry takes no `null`); and the updated aggregate the update hands back is not used.

**Fix:** call the entry (**quick-fix available** — the id, the version, the operation's arguments and a
`cancellationToken:` in that order). A suggestion by default; a warning under `<RaskStrict>`.

## RASK187

**A raw ExecuteUpdate or ExecuteDelete writes an aggregate's rows** · Info (Warning under `<RaskStrict>`) · quick-fix

EF Core's `ExecuteUpdate`/`ExecuteDelete` over the rows of an aggregate. Every aggregate has its own
[set write](data-writes.md#writes-over-many-rows) — `X.UpdateWhere(…)`, `X.DeleteWhere(…)` — the same one statement, which
also stamps `UpdatedAt` and bumps `Version`, asks the aggregate's `LockedBy`, refuses a delete its `ValidateDelete()`
would have to ask row by row, counts toward [one aggregate per transaction](domain.md#one-aggregate-per-transaction-manyaggregates)
and tells the live pages showing the rows. The raw statement does none of it.

```csharp
await db.Pairings.Where(p => p.Pump == pumpId).ExecuteDeleteAsync();                    // ℹ RASK187
await Pairing.DeleteWhere(p => p.Pump == pumpId);                                       // ✓
await Period.UpdateWhere(p => p.Id == id, s => s.SetProperty(p => p.Closed, true));     // ✓ for ExecuteUpdateAsync
```

A table that is no aggregate (a plain entity, Rask's own outbox or cache) is not reported.

**Fix:** call the aggregate's own (**quick-fix available** for the awaitable form on a `DbSet`, straight —
`_ => true` — or behind one `Where`; the synchronous form, and a query composed further, are reported without one).
The rewrite runs on the app's context in a `Db.Transaction`, not on the one the raw call named. A suggestion by
default; a warning under `<RaskStrict>`.

## RASK188

**An operation gets no Form.For, as one of its parameters is no field a form holds** · Info

`Form.For(Supply.Change, id)` is a form whose fields are the [operation](domain.md#an-operation-run-as-one-write)'s
parameters: a value (text, number, day, bool, enum, id), a value object of one value — declared from `Text`, `Number`,
`Whole`, `Day`, `Moment`, or a one-member `record TankSize(int Value) : Value` — a value object of several values, or a
list of rows. An operation that takes anything else, or more than 13 parameters, gets no form, and this names the
parameter that keeps it from one rather than leaving the form silently missing.

```csharp
public void Weigh(Stream scale) { … }          // ℹ RASK188: 'scale' (System.IO.Stream) is no field a form holds
public void Resize(TankSize size) { … }        // ✓ a field held as its int, made through TankSize.Create
```

**Fix:** take a value object of what the parameter means, or a value; the operation's entry is generated either way.
