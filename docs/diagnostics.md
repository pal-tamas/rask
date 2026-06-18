# Rask diagnostics (RASK001–RASK023)

Every Rask diagnostic, what triggers it, and how to fix it. Errors block the build; warnings don't
but flag a real problem; one is hidden (informational, surfaced only as an IDE suggestion).

These come from the Rask source generator and analyzers (`Rask.Generators`). The generated
factories don't exist until a build runs, so if an ID below isn't recognised by your IDE yet,
build once.

| ID | Severity | Summary |
|----|----------|---------|
| [RASK001](#rask001) | Hidden | Property is treated as a required factory parameter |
| [RASK002](#rask002) | Warning | `required` property is incompatible with a DI constructor |
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

**Fix (optional):** add `required` for language-level enforcement, or make the property nullable
(`string? Label`) if it should be optional. HTML-attribute props are intentionally declared nullable
to stay ergonomic. See [factory generation rules](getting-started.md).

## RASK002
**`required` property is incompatible with a DI constructor** · Warning

A property is marked `required`, but the component's only constructor takes dependency-injected
parameters. With no parameterless constructor available, the factory builds the component with
`ActivatorUtilities.CreateInstance`, which can't satisfy a `required` member, so the requirement
can't be honoured. (Adding a parameterless constructor lets the factory use the object-initializer
path, which does honour `required` — so this warning does not fire in that case.)

**Fix:** remove `required`, **or** move the value to a constructor parameter, **or** add a
parameterless constructor, **or** drop the DI constructor. Framework services (`RouteState`,
`Navigator`, `HttpClient`, `IJSRuntime`) should come through the constructor, never as settable
properties.

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

**Fix:** override `protected override RenderResult Head => ...` on any component and return your
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

**Fix:** make the root render the complete shell — typically `Fragment()[ Doctype(), Html(...)[...] ]`.
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
assistive technology skips it. See [accessibility](accessibility.md).
