# Islands — a `.tsx`, `.vue` or `.svelte` file as a Rask component

An **island** is an ordinary Rask component whose markup is produced by a front-end
framework. Derive from one of the seven base classes below, drop the front-end file beside it, and it
goes anywhere the chain goes — a leaf inside a card, a subtree, or the whole of a `[Route]` page's
`Render()`.

| Base class | Pairs with | Compiled by | Covers |
|---|---|---|---|
| `ReactComponent` | `Chart.tsx` | `@vitejs/plugin-react` | React, and Preact through a `preact/compat` alias |
| `PreactComponent` | `Chart.tsx` | `@preact/preset-vite` | Preact directly |
| `SolidComponent` | `Chart.tsx` | `vite-plugin-solid` | Solid |
| `VueComponent` | `Chart.vue` | `@vitejs/plugin-vue` | Vue 3 |
| `SvelteComponent` | `Chart.svelte` | `@sveltejs/vite-plugin-svelte` | Svelte 5 |
| `AngularComponent` | `Chart.ts` | `@analogjs/vite-plugin-angular` | Angular, as a standalone component |
| `LitComponent` | `Chart.ts` | nothing — it is ordinary TypeScript | Lit, and any custom element with property-shaped inputs |

The same seven the [SPA lane](spa.md) scaffolds a whole client for. Which one an island uses is
decided by its **base class**, never by its file extension — three of them write `.tsx` and two write
`.ts`, so the extension names a family and only the C# says which member.

```csharp
[Route("/dashboard")]
public sealed partial class DashboardPage : Component
{
    protected override Component? Render() =>
        Div.Class("grid")[
            H1["Revenue"],                 // Rask
            Chart.Series(_points),         // Chart.tsx, hydrated in the browser
            BsCard[ Table.Rows(_rows) ],   // Rask again
        ];
}
```

There is deliberately **no separate page concept**. "React owns this route" is the case where the
component happens to be the page root, and it falls out of the same primitive:

```csharp
[Route("/legacy/report")]
public sealed partial class ReportPage : Component
{
    protected override Component? Render() => Report.Id(Id);
}
```

That is what "every component is replaceable" means here: replaceability is a property of the
*component*, not of the route, so it composes at every level of the tree.

## Declaring one

The base class **is** the declaration. It names the runtime in the one place that cannot disagree
with what actually mounts, and there is nothing else to write:

```csharp
// Features/Dashboard/Chart.cs
public sealed partial class Chart : ReactComponent
{
    /// <summary>The points to plot.</summary>
    public required IReadOnlyList<Point> Series { get; set; }

    /// <summary>Heading shown above the plot.</summary>
    public string? Heading { get; set; }
}

// Features/Dashboard/Meter.cs
public sealed partial class Meter : SvelteComponent
{
    /// <summary>The reading, 0..1.</summary>
    public double Value { get; set; }
}

// Features/Dashboard/Gauge.cs
public sealed partial class Gauge : LitComponent
{
    /// <summary>The needle position, 0..1.</summary>
    public double Value { get; set; }
}
```

`partial` is required ([RASK056](diagnostics.md#rask056)) — the component's name, its module and its
props writer are generated into the same class.

The front-end file is found the way scoped CSS and scoped JS already are: by filename, beside the
class. `Chart.cs` pairs with `Chart.tsx`; `Meter.cs` with `Meter.svelte`; `Gauge.cs` with `Gauge.ts`.
**The base class is what makes that inference possible for Lit** — a Lit component is ordinary
TypeScript and nothing about a `.ts` extension distinguishes it, so before the runtime was stated by
the type, every Lit component
had to name its module by hand.

Override `Module` only when the file lives somewhere convention cannot reach:

```csharp
public sealed partial class Vendor : ReactComponent
{
    protected override string Module => "@acme/charts/Chart";
}
```

It has to be a **constant string** ([RASK059](diagnostics.md#rask059)). The bundler reads it at build
time to generate the entry module, long before any of this code runs, so anything computed would
leave the browser resolving a name the bundle never built.

### It costs the inheritance slot, and that is the trade

A component already extending `BsBlock` or your own base cannot also be a `ReactComponent` — C# gives
every class one base. That is deliberate rather than an oversight: chrome in Rask comes from the
chain, not from inheritance, so the answer is to compose.

```csharp
BsCard[ Chart.Series(points) ]        // ✓ Bootstrap chrome around a React component
class Themed : BsBlock, ReactComponent // ✗ does not compile, and should not
```

The alternative — a marker attribute usable on any class — was tried first. It works, but it means
writing the runtime twice (once as an attribute argument, once in the front-end file) with nothing
keeping the two in step.

### Two runtimes that share an extension need separate folders

React, Preact and Solid all compile `.tsx`. When two of them are in one project, each Vite plugin is
scoped to the directories its own islands live in — so they need folders of their own, and one must
not nest inside the other:

```
Features/Islands/React/Counter.tsx      ✓ each runtime in its own folder
Features/Islands/Solid/Spark.tsx

Features/Islands/Counter.tsx            ✗ refused: one tree, two JSX runtimes
Features/Islands/Solid/Spark.tsx
```

The build refuses the second arrangement by name rather than building it, because getting it wrong is
silent. Scoping has to be by **directory**, not by file: a file-level scope transforms the island
correctly and leaves every module it *imports* to the other plugin, so a `Row.tsx` beside a Solid
island gets compiled as Preact — which builds, ships, loads, and mounts a foreign vnode into Solid's
renderer. Astro documents the same rule for the same reason.

A runtime that owns its extension alone — Vue, Svelte — needs none of this and can live anywhere.
Angular is never scoped either: its plugin compiles the Angular island ahead of time and passes
ordinary TypeScript through untouched, so a Lit element sits happily beside one.

### React and Preact cannot share a project

Not a Rask rule. `@vitejs/plugin-react` resolves Babel 8 and `@preact/preset-vite` pins a
`@babel/core@"7.x"` peer, so **npm refuses to install both**. Pick one; the build says so by name
rather than leaving an ERESOLVE tree that names four Babel packages and neither island.

`ReactComponent` also still covers Preact the old way, for a project already built on it: alias
`react` and `react-dom` to `preact/compat` in both tsconfig and the Vite plugin — the same aliasing
the [TypeScript SPA lane](spa.md) relies on — and one adapter serves both. New code should reach for
`PreactComponent`, which imports Preact directly and needs no aliasing to be right.

### What Angular needs

Angular's plugin imports two packages it does not depend on, so both have to be installed beside it,
at the same major:

```
@analogjs/vite-plugin-angular  @angular/compiler-cli  @angular/build
@angular/core  @angular/common  @angular/compiler  @angular/platform-browser  rxjs
```

It also pins **TypeScript below 6.1**, which constrains the whole project. And its islands are far
the heaviest: a one-component Angular chunk is around 73 kB gzipped, against 12 kB for Preact and
10 kB for Solid.

Every prop must be an `@Input()` (or `input()`). The adapter drives updates through
`ComponentRef.setInput`, which is the only route that marks the view dirty — a plain public field is
not an input at all, and Angular reports that in a development build while ignoring it silently in a
production one.

Angular is also the only runtime whose bootstrap is asynchronous. Props that arrive before
`createApplication()` resolves are held and applied on arrival rather than dropped, and an island
removed while still booting destroys the application when it appears instead of leaking it.

### What Solid needs

Never destructure `props`. Solid tracks the *access*, not the value, so pulling a prop into a local
reads it once and freezes it — the component renders correctly and then never updates again:

```tsx
export default function Spark(props: SparkProps) {   // ✓
  return <div>{props.caption}</div>
}

export default function Spark({ caption }: SparkProps) {   // ✗ renders once, never again
  return <div>{caption}</div>
}
```

## C# owns the props

Props are C# properties, declared like any other component's, so the chain, the required-prop rule
([RASK001](diagnostics.md#rask001)) and every existing analyzer apply unchanged. The generator emits a
reflection-free `Utf8JsonWriter` writer for them, which is what lets a component survive trimming and
AOT.

**And the types cross back.** The build generates an interface per component, so the front-end file
imports the shape rather than restating it:

```tsx
// Features/Dashboard/Chart.tsx
import type { ChartProps } from '@rask/Chart.props'

export default function Chart({ series, heading, onPointClick }: ChartProps) {
  return <figure><figcaption>{heading}</figcaption><Plot data={series} /></figure>
}
```

Rename `Series` to `Points` in `Chart.cs` and `Chart.tsx` **stops compiling**. That is the difference
between this and embedding React by hand: the contract is checked in both directions rather than
maintained by discipline.

The generated file carries whatever the props are composed of, so one import is enough:

```ts
// obj/rask-external/types/Chart.props.d.ts — generated, do not edit
export interface Point { label: string; value: number }

export interface ChartProps {
  series: Point[];
  heading: string | null;
  onPointClick?: (value: number) => void;
}
```

Three things there are decisions rather than formatting. `heading` is **nullable but still required**,
because the writer emits the key with a JSON `null` — "never set" and "set to nothing" are different
facts, and `heading?: string` would describe the wrong one. A callback is **optional**, because an
unwired one omits its key entirely. And it returns **`void` even for a `Func<T, Task>`**: the callback
crosses as a handler reference and the client hands back a plain function that ships the payload, so
there is no promise on that side to await.

`@rask/*` comes from a tsconfig fragment the build writes into `obj/`. Extend it once:

```jsonc
// tsconfig.json
{ "extends": "./obj/rask-external/tsconfig.paths.json" }
```

A fragment rather than an edit to your own tsconfig: rewriting someone else's config means preserving
their comments, key order and formatting, and would still surprise anyone who opened it — for a path
mapping that is one line to add and obvious to remove. Nothing generated is committed, so a fresh
clone shows the import unresolved until the first build.

### The check runs as part of `dotnet build`

`dotnet build` type-checks each front-end file against the props its C# declares, so the guarantee
holds by default rather than only for people who run `tsc`:

```
error TS2339: Property 'level' does not exist on type 'DialProps'.
```

Turn it off with `<RaskExternalTypeCheck>false</RaskExternalTypeCheck>` — for a deliberately red front
end mid-refactor, say. tsgo costs roughly 0.2s; the single-file checkers below are seconds each,
and none of the three has an up-to-date check — they run on every build.

### A single-file component needs its framework's own checker

Rask type-checks `.ts`, `.tsx` and `.jsx` with **tsgo**, the same native TypeScript it fetches for
everything else. tsgo parses TypeScript and JSX and nothing else, and there is no plugin seam to teach
it a `.vue` or a `.svelte` — so those are checked by **`vue-tsc`** and **`svelte-check`**, run from
your own `node_modules` at the version your `package.json` pinned.

That is a second and third toolchain, which is exactly the tax [Why Vite, and only
Vite](#why-vite-and-only-vite) declines for *bundlers*. It is accepted here because the alternative is
not a weaker check but no check: no single tool reads all five file types, and a compile-time contract
that silently skips two of them is the failure this whole feature exists to avoid. The reward is that
the check reaches into the **template**, not only the script block:

```
error TS2339: Property 'seriesTypo' does not exist on type 'DefineProps<LooseRequired<ChartProps>>'.
```

Rask does **not** fetch these the way it fetches tsgo — they are the same packages your front end
already builds with, and a second copy on a different version would disagree with the bundler about
the same file. If one is missing, the build says so in as many words and carries on; it never passes
silently.

**A component that imports nothing from npm is checked with nothing installed.** A Lit element or a
plain custom element resolves its generated props and no more, so the no-npm path is checked too —
which is the case it would be easiest to leave uncovered.

A project that *has* a `package.json` but has not installed it is skipped, with a message saying so.
That is not a preference: a `.tsx` importing `react` cannot be checked before the install, and there
is no weaker mode that works. TypeScript's no-resolve mode does not merely tolerate the missing
package — it stops resolving the generated props as well, so the contract error never fires and
correct and incorrect code fail identically.

Supported prop types are the wire vocabulary the CQRS codecs use: the primitives, `string`, `Guid`,
the date/time types, `Uri`, enums, `byte[]`, nullable versions of those, arrays and lists, string-keyed
dictionaries, and records composed of the same. Anything else is
[RASK057](diagnostics.md#rask057) at compile time rather than `null` in the browser. `[SkipFactory]`
keeps a property out of the props entirely.

> **A prop named after an HTML tag will not compile.** `Title`, `Label`, `Data`, `Form`, `Style` and
> friends collide with the chain entry of the same name (CS0108, fatal under `-warnaserror`). This is
> not specific to these, but they make it much likelier because those are natural names for a UI
> component's props. Rename the property, or qualify the tag at its use site.

## Callbacks

A delegate prop becomes a function on the front end, and calling it re-enters C#:

```csharp
public Action<int>? OnPointClick { get; set; }
public Func<Range, Task>? OnZoomAsync { get; set; }
```

```tsx
export default function Chart({ series, onPointClick }: ChartProps) {
  return <Plot data={series} onPointClick={onPointClick} />
}
```

`Action`, `Action<T>`, `Func<Task>` and `Func<T, Task>` — the four shapes Rask already auto-wraps.

They travel as a handler reference rather than a value, and reach C# through the **same channel every
DOM handler uses**: the open WebSocket on the Server host, a direct `[JSExport]` call into this tab's
runtime on WASM. An island never opens a channel of its own, so a callback inherits sequence
stamping, the queue-while-reconnecting, and the auth suppression window for free — and the `.tsx` is
byte-identical on both hosts.

A callback that is not wired omits its key entirely, so the front end sees `undefined` rather than a
key that still looks callable. A **data** prop set to null stays a JSON `null`, because "never set"
and "set to nothing" are different facts.

## Hydration

```csharp
Chart.Series(_points).Hydration(ExternalHydration.Visible)
```

| Mode | When the adapter mounts |
|---|---|
| `Load` (default) | As soon as the component's chunk has loaded. |
| `Idle` | On `requestIdleCallback`. |
| `Visible` | On `IntersectionObserver` — the chunk is not even **fetched** until the component is scrolled to. |
| `None` | Never. Server markup only, and no JavaScript is requested at all. |

## An island takes no children

An island is a leaf. Writing children into one is a **compile error** —
[RASK062](diagnostics.md#rask062) — rather than something that binds, compiles and then quietly stops
tracking what you gave it:

```csharp
Panel.Heading("Sales")[ Table.Rows(_rows) ]   // RASK062
```

Children would have to be handed across the diff boundary below, and once a front-end framework has
moved those nodes into its own tree every path Rask holds into them is wrong — the diff addresses DOM
nodes by `childNodes` index from the document. Content placed that way is placed once and then goes
dead, which looks like composition and is not.

Compose the other way round instead. It costs nothing, and everything on the Rask side stays live:

```csharp
BsCard[
    Panel.Heading("Sales"),
    Table.Rows(_rows),
    BsButton.OnClick(Save)["Save"],
]
```

## Tailwind

A utility written inside an island works like any other. Tailwind v4 detects sources from the project
it runs in, and a `.vue`, `.tsx` or `.svelte` is an ordinary source to it, so an island in the same
project as your stylesheet needs nothing:

```vue
<div class="flex h-32 items-end gap-2">
```

**An island in a different project needs two statements, not one.** Tailwind scans one project, so a
front-end file in a sibling one is invisible to it — every utility used there would be dropped from the
sheet, with the island rendering unstyled and nothing reporting why. Name the directory in the
stylesheet:

```css
@import "tailwindcss";
@source "../../Shop.Web/Features/Islands";
```

And name it again to the build, which cannot read that line:

```xml
<ItemGroup>
  <RaskTailwindSource Include="../Shop.Web/Features/Islands/**/*.vue"/>
</ItemGroup>
```

`@source` decides what Tailwind **scans**; `RaskTailwindSource` decides what the build **watches**. With
only the first, the sheet is built from those files once and then never rebuilt when one changes — the
same staleness as before, one project further out.

> **A Lit island is the exception, and it is Lit's rule rather than Rask's.** A `LitElement` renders
> into a shadow root, and page-level CSS does not cross that boundary — so the app's Tailwind sheet
> cannot reach inside one. Style it with Lit's own `static styles`, or render to light DOM by
> overriding `createRenderRoot()`.

## The diff boundary

Rask's live runtime diffs the server's render against the browser's DOM. Its subtree is
**not Rask's to diff**: React owns those nodes and reconciles them on its own schedule, and two
writers on one subtree does not throw — it corrupts on the next parent re-render.

So the host element is a boundary. Nothing inside it is ever patched, removed or re-keyed by Rask.
What crosses is **props, and only props**: a changed prop travels as a single attribute op, and the
client routes it to the adapter's `update` rather than letting it land as an attribute nobody reads.
An update is therefore a reconcile, never a remount — the component keeps its scroll position, its
focus, its open dropdown and its half-typed field.

Callbacks keep their identity across updates for the same reason. React compares props by identity, so
a fresh closure per render would invalidate every `useCallback` and `memo` keyed on it and re-fire
every `useEffect` that lists it.

## Hot reload

`rask dev` runs a Vite dev server for the islands beside the app, so editing a `.tsx` or a `.svelte`
hot-replaces in place instead of rebuilding:

```
$ rask dev
Serving islands from http://localhost:5174 (hot reload)…
```

Nothing to configure. The dev server starts when the project has islands, dies with the host, and
listens on **5174** — not Vite's 5173, which belongs to the [SPA lane's](spa.md) client, so a solution
with both does not have two dev servers fighting for one port. Override it with
`<RaskExternalDevServerPort>` if something else is already there.

What changes during a dev session is one step: the production `vite build` is skipped, because it
would rebuild every island in the project on every save and nothing would read the result. Everything
else still runs — the entry modules, the prop types, the type-check — and the manifest is still
written, pointing at the dev server rather than at chunks.

**How much you get back depends on the runtime, and that is upstream's call, not Rask's.** Once the
modules are served by the dev server, each framework's own refresh integration owns them:

| Runtime | On save |
|---|---|
| React, Preact | Fast Refresh — component state survives |
| Solid | `solid-refresh` — signals survive |
| Vue, Svelte | plugin HMR — component state survives |
| Lit | full page reload (a custom element cannot be re-registered) |
| Angular | full page reload |

Even the reload cases skip the C# rebuild, which is the slow half.

Rask deliberately does not replace a mounted island itself. Doing so would fight the framework
integrations and lose: they preserve component state, and a remount is precisely what the [diff
boundary](#the-diff-boundary) exists to avoid.

## What the build does

`dotnet build` writes one entry module per component — pairing your component with its runtime's adapter,
so `Chart.tsx` stays a plain React component with no Rask import in it — and hands Vite a generated
config that builds them into one chunk each, plus the manifest the client runtime resolves names
through.

**A project using them needs Node and a `package.json`.** That single file is also the gate: a project
without one runs no npm, probes for no node, and never learns this package has a build step. A Rask app
with no islands is unaffected, which is most of them.

```bash
npm init -y
npm install -D vite @vitejs/plugin-react react react-dom          # React
npm install -D vite @preact/preset-vite preact                    # Preact (not beside React)
npm install -D vite vite-plugin-solid solid-js                    # Solid
npm install -D vite @vitejs/plugin-vue vue vue-tsc                # Vue
npm install -D vite @sveltejs/vite-plugin-svelte svelte svelte-check   # Svelte
npm install -D vite lit                                           # Lit
npm install -D vite @analogjs/vite-plugin-angular @angular/compiler-cli @angular/build \
    @angular/core @angular/common @angular/compiler @angular/platform-browser rxjs   # Angular
```

Install only what you use. A plugin is written into the generated Vite config **only** when an island
of that runtime exists, so a Lit-only app is never asked for `@vitejs/plugin-react`, and a Vue-only
app is not either.

### Why Vite, and only Vite

Rask itself stays npm-free — [#871](https://github.com/pal-tamas/rask/pull/871) fetches esbuild and
tsgo as verified native binaries, and the framework's own browser code compiles with no package
manager anywhere. That does not change here. What changes is that **you** asked for a React component,
and React is an npm package: needing it is inherent to the choice rather than a gap in the framework.

A second, esbuild-based path with no npm was designed and rejected. esbuild can do the whole job —
`--splitting` gives one hashed chunk per component plus a shared chunk, `--metafile` maps each entry to
what was actually emitted, `--jsx=automatic` handles `.tsx`, and two components bundle in about a
millisecond. The problem is not capability, it is having two of everything: two bundlers, two failure
modes, and "why does it work in project A but not B" for the life of the feature. That tax outlasts a
one-time install.

The no-npm audience was also narrower than it first appeared. A real Lit component starts
`import { LitElement, html } from 'lit'` — which is npm. Only a dependency-free custom element
qualified, which is a genuine case but a small one, and not worth a permanent second toolchain.

Vite is also what made Vue and Svelte cheap when they landed: a single-file component is compiled by a
*Vite plugin*, so each was an adapter rather than a compiler integration. Angular is the one still
outstanding, and the one where that is not true.

**Plugin order is not cosmetic.** A Vue or Svelte plugin claims one extension it alone understands;
the React plugin installs a *general* JSX transform. Rask registers the single-file compilers first,
because the other order sends a `.vue` to the JSX parser and fails as `Unexpected JSX expression` at
line 1 — naming neither Vue nor the plugin that should have handled it.

| Property | Default | |
|---|---|---|
| `RaskExternalBuild` | `true` | `false` skips node entirely. They still render their host elements. |
| `RaskExternalLitAutoPair` | `true` | `false` stops a `.ts` beside a `.cs` being assumed a Lit island. Set it in any project that also has scoped TypeScript. |
| `RaskExternalOutputDir` | `wwwroot/_rask/external` | Under `wwwroot` so the SDK publishes it with no publish target of its own. |

The bundle is written after `wwwroot` has already been globbed, so the build registers it as a
**static web asset** rather than relying on that glob. Without it `app.MapStaticAssets()` — which
serves only what reached the endpoints manifest — would answer every chunk request with the page's
own HTML, and the client would report `Unexpected token '<'` with nothing mounted. Nothing extra is
needed in `Program.cs`; in particular `app.UseStaticFiles()` is **not** required. A publish that
somehow produced no endpoint for the bundle fails the build with `RASKISLAND003` rather than
shipping an app whose components silently never mount.

| `RaskExternalPublicBase` | `/_rask/external/` | The URL prefix the manifest gives each chunk. |

A `.ts` is picked up when a `.cs` of the same name sits beside it. Declare one explicitly only when it
lives somewhere that pairing cannot reach — the build-side counterpart of overriding `Module`:

```xml
<ItemGroup>
  <RaskExternal Include="widgets/gauge.ts" Runtime="lit"/>
</ItemGroup>
```

A Lit module default-exports its registered tag name, which is how the generated entry knows
what to create — a custom element registers its own tag and nothing about the file reveals it.

> **Write a Lit element without decorators.** `@customElement` and `accessor` are standard-decorator
> syntax, and the *bundler* is what has to lower them — Vite's oxc-based transform does not. The
> failure is silent in the worst way: the chunk builds, ships, and loads, and the element simply never
> upgrades, so the island renders empty with nothing in the console and nothing in the build log. Use
> `static properties` plus `customElements.define('my-tag', MyElement)`, which is the same API with no
> transform to depend on.

> **A Lit island collides with scoped TypeScript.** Both features are spelled `Name.ts` beside
> `Name.cs`, and nothing in MSBuild can tell them apart: the only difference is whether the class
> derives from `LitComponent`, which Roslyn knows and a glob does not. It bites in both directions —
> the scoped pipeline compiles an island's file as a component asset, and island discovery offers
> every scoped file to the bundler as a Lit module that never default-exported a tag name.
>
> Say which the project has:
>
> - **Islands only** (no scoped TypeScript): `<RaskScopedTsAutoInclude>false</RaskScopedTsAutoInclude>`.
> - **Scoped TypeScript only**, or scoped TypeScript plus Lit islands you name yourself:
>   `<RaskExternalLitAutoPair>false</RaskExternalLitAutoPair>`, then declare each Lit island with
>   `<RaskExternal Include="widgets/gauge.ts" Runtime="lit"/>`.
>
> The other three runtimes have extensions of their own and are never ambiguous.

### Both hosts, verified

The same component, byte-identical `.tsx`, was built and driven in a browser on both hosts: it mounts,
receives its C# props, and round-trips a typed callback back into C# — server state and React's own
local state advancing together, which is what shows the adapter reconciles rather than remounts.

On WASM the callback reaches C# through a `[JSExport]` call into this tab's runtime rather than over a
socket; nothing in the front-end file knows which.

## What is not here yet

- **Children inside an island.** An island is a leaf ([RASK062](diagnostics.md#rask062)). Handing
  Rask-owned nodes to a framework that then owns them needs updates addressed by MARKER rather than by
  DOM path, since `EditOp` paths are positional `childNodes` indices — see
  [children](#an-island-takes-no-children).
- **Angular.** The adapter seam is three functions wide and the client runtime imports no framework, so
  it is additive the way Vue and Svelte were. Angular is viable through standalone components plus
  `createApplication()`, which needs no root component and no NgModule; its build is the real cost,
  since Angular components need the Angular compiler rather than plain Vite.
- **Blazor.** `.razor` components, with props staying C# and never becoming JSON — and static prerender
  needing no bundler at all.
- **Islands in a shared library.** The client runtime resolves the manifest at the app-rooted
  `/_rask/external/manifest.json`, so the app that serves the page is the app that has to bundle them.
  A class library can hold the C#, but its bundle would be served under `_content/<PackageId>/` where
  nothing looks for it.
- **Server-side rendering** for the bundler-backed runtimes, which is what would make `Hydration.None`
  broadly useful.
