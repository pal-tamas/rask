# Islands — a `.tsx` file as a Rask component

An **island** is an ordinary Rask component whose markup is produced by another framework. Mark a
component `[Island]`, drop the front-end file beside it, and it goes anywhere the chain goes — a leaf
inside a card, a subtree, or the whole of a `[Route]` page's `Render()`.

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
island happens to be the page root, and it falls out of the same primitive:

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

An island is an ordinary component carrying an attribute — **not** a subclass of an island base type.
A base class would spend the single-inheritance slot, so a component already extending `BsBlock` or
your own base could never become island-backed. It also matches how the rest of the framework
declares behaviour: `[Route]`, `[ParentRoute]`, `[LocalOnly]`.

```csharp
// Features/Dashboard/Chart.cs
[Island]
public sealed partial class Chart : Component
{
    /// <summary>The points to plot.</summary>
    public required IReadOnlyList<Point> Series { get; set; }

    /// <summary>Heading shown above the plot.</summary>
    public string? Heading { get; set; }
}
```

`partial` is required ([RASK055](diagnostics.md#rask055)) — the host element, the props writer and the
hydration step are generated into the same class. Migrating an existing component is therefore an
attribute and a deletion: add `[Island]`, remove `Render()`, and the call sites do not change.

The front-end file is found the way scoped CSS and scoped JS already are — by filename, beside the
class. `Chart.cs` pairs with `Chart.tsx`. Name the module explicitly only when it lives somewhere
convention cannot reach:

```csharp
[Island("@acme/charts/Chart")]
[Island("./widgets/gauge.ts", Runtime = IslandRuntime.Lit)]
```

`.tsx` and `.jsx` infer React. **Lit has to say so**, because a Lit component is an ordinary `.ts`
file and nothing about the extension distinguishes it from any other TypeScript in the project.

### Preact needs nothing

`IslandRuntime.React` covers Preact unchanged. A Preact project aliases `react` and `react-dom` to
`preact/compat` in both tsconfig and the Vite plugin — the same aliasing the [TypeScript SPA
lane](spa.md) already relies on — so one adapter serves both and Rask never needs to know which it
got.

## C# owns the props

Props are C# properties, declared like any other component's, so the chain, the required-prop rule
([RASK001](diagnostics.md#rask001)) and every existing analyzer apply unchanged. The generator emits a
reflection-free `Utf8JsonWriter` writer for them, which is what lets an island survive trimming and
AOT.

The front-end file receives them as ordinary props:

```tsx
// Features/Dashboard/Chart.tsx
export default function Chart({ series, heading }: { series: Point[]; heading?: string }) {
  return <figure><figcaption>{heading}</figcaption><Plot data={series} /></figure>
}
```

Supported prop types are the wire vocabulary the CQRS codecs use: the primitives, `string`, `Guid`,
the date/time types, `Uri`, enums, `byte[]`, nullable versions of those, arrays and lists, string-keyed
dictionaries, and records composed of the same. Anything else is
[RASK056](diagnostics.md#rask056) at compile time rather than `null` in the browser. `[SkipFactory]`
keeps a property out of the props entirely.

> **A prop named after an HTML tag will not compile.** `Title`, `Label`, `Data`, `Form`, `Style` and
> friends collide with the chain entry of the same name (CS0108, fatal under `-warnaserror`). This is
> not island-specific, but islands make it much likelier because those are natural names for a UI
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
Chart.Series(_points).Hydration(IslandHydration.Visible)
```

| Mode | When the adapter mounts |
|---|---|
| `Load` (default) | As soon as the island's chunk has loaded. |
| `Idle` | On `requestIdleCallback`. |
| `Visible` | On `IntersectionObserver` — the chunk is not even **fetched** until the island is scrolled to. |
| `None` | Never. Server markup only, and no JavaScript is requested at all. |

## The diff boundary

Rask's live runtime diffs the server's render against the browser's DOM. An island's subtree is
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

## What the build does

`dotnet build` writes one entry module per island — pairing your component with its runtime's adapter,
so `Chart.tsx` stays a plain React component with no Rask import in it — and hands Vite a generated
config that builds them into one chunk each, plus the manifest the client runtime resolves names
through.

**An island project needs a `package.json`.** That single file is also the gate: a project without one
runs no npm, probes for no node, and never learns this package has a build step. A Rask app with no
islands is unaffected, and so is one whose islands are all `.razor`.

```bash
npm init -y
npm install -D vite @vitejs/plugin-react react react-dom
```

| Property | Default | |
|---|---|---|
| `RaskIslandsBuild` | `true` | `false` skips node entirely. The islands still render their host elements. |
| `RaskIslandsOutputDir` | `wwwroot/_rask/islands` | Under `wwwroot` so the SDK publishes it with no publish target of its own. |
| `RaskIslandsPublicBase` | `/_rask/islands/` | The URL prefix the manifest gives each chunk. |

A Lit island is declared to the build explicitly, since its `.ts` extension is not distinguishable
from any other TypeScript:

```xml
<ItemGroup>
  <RaskIsland Include="widgets/gauge.ts" Runtime="lit"/>
</ItemGroup>
```

A Lit island's module default-exports its registered tag name, which is how the generated entry knows
what to create — a custom element registers its own tag and nothing about the file reveals it.

## What is not here yet

- **Slots.** An island is a leaf: it cannot yet wrap Rask-rendered children. That needs a fourth
  adapter function to adopt existing DOM, because React, Vue, Solid and Svelte all expect to *create*
  what they render.
- **Vue, Svelte, Angular.** The adapter seam is three functions wide and the client runtime imports no
  framework, so these are additive. Angular is viable through standalone components plus
  `createApplication()`, which needs no root component and no NgModule; its build is the real cost,
  since Angular components need the Angular compiler rather than plain Vite.
- **Blazor.** `.razor` islands, with props staying C# and never becoming JSON — and static prerender
  needing no bundler at all.
- **Server-side rendering** for the bundler-backed runtimes, which is what would make `Hydration.None`
  broadly useful.
