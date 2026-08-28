# External components — a `.tsx` file as a Rask component

An **external component** is an ordinary Rask component whose markup is produced by a front-end
framework. Derive from `ReactComponent` or `LitComponent`, drop the front-end file beside it, and it
goes anywhere the chain goes — a leaf inside a card, a subtree, or the whole of a `[Route]` page's
`Render()`.

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

// Features/Dashboard/Gauge.cs
public sealed partial class Gauge : LitComponent
{
    /// <summary>The needle position, 0..1.</summary>
    public double Value { get; set; }
}
```

`partial` is required ([RASK055](diagnostics.md#rask055)) — the component's name, its module and its
props writer are generated into the same class.

The front-end file is found the way scoped CSS and scoped JS already are: by filename, beside the
class. `Chart.cs` pairs with `Chart.tsx`; `Gauge.cs` pairs with `Gauge.ts`. **The base class is what
makes that inference possible for Lit** — a Lit component is ordinary TypeScript and nothing about a
`.ts` extension distinguishes it, so before the runtime was stated by the type, every Lit component
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

### Preact needs nothing

`ReactComponent` covers Preact unchanged. A Preact project aliases `react` and `react-dom` to
`preact/compat` in both tsconfig and the Vite plugin — the same aliasing the [TypeScript SPA
lane](spa.md) already relies on — so one adapter serves both and Rask never needs to know which it
got.

## C# owns the props

Props are C# properties, declared like any other component's, so the chain, the required-prop rule
([RASK001](diagnostics.md#rask001)) and every existing analyzer apply unchanged. The generator emits a
reflection-free `Utf8JsonWriter` writer for them, which is what lets a component survive trimming and
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
runtime on WASM. An external component never opens a channel of its own, so a callback inherits sequence
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

## Slots

An external component can wrap Rask-rendered content, so replacing a component in the middle of a tree does not
strand its descendants:

```csharp
Panel.Heading("Sales")[
    ExternalSlot.Named("footer")[ BsButton["Save"] ],
    Table.Rows(_rows),                              // the default slot
]
```

Anything not assigned to a named slot goes to `default`, which the front end receives as `children`:

```tsx
export default function Panel({ heading, children, footer }: PanelProps) {
  return <section><h2>{heading}</h2>{children}<footer>{footer}</footer></section>
}
```

It is called `ExternalSlot`, not `Slot`, because `Slot` is already the HTML `<slot>` element in
`Rask.Html.Components` and a colliding name does not compile.

**How the content travels.** The server renders each slot into an inert
`<template data-rask-slot="…">`, so Rask-owned nodes cannot flash on screen between first paint and
the component mounting. On mount the client lifts each template into a fragment, removes it, and hands
it to the adapter — which decides where its framework wants the nodes. React renders an empty
container and adopts into it via a ref, so React has no children to reconcile there. Lit needs no
trick at all: a custom element projects its light-DOM children through `<slot>` natively.

> **Slot content is placed once, at mount.** If the C# that produced it re-renders, the component keeps
> showing what it was given. That is the genuinely hard half: the diff addresses DOM nodes by
> `childNodes` index from the document, so once an adapter has moved slot nodes into its own tree,
> every path Rask holds into them is wrong. Making it live needs slot updates addressed by marker
> rather than by path — a subtree morph scoped to `[data-rask-slot]` — which is not built yet. Until
> then, prefer props for anything that changes, and slots for structure that does not.

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

## What the build does

`dotnet build` writes one entry module per component — pairing your component with its runtime's adapter,
so `Chart.tsx` stays a plain React component with no Rask import in it — and hands Vite a generated
config that builds them into one chunk each, plus the manifest the client runtime resolves names
through.

**A project using them needs a `package.json`.** That single file is also the gate: a project without one
runs no npm, probes for no node, and never learns this package has a build step. A Rask app with none of them
is unaffected.

```bash
npm init -y
npm install -D vite @vitejs/plugin-react react react-dom
```

| Property | Default | |
|---|---|---|
| `RaskExternalBuild` | `true` | `false` skips node entirely. They still render their host elements. |
| `RaskExternalOutputDir` | `wwwroot/_rask/external` | Under `wwwroot` so the SDK publishes it with no publish target of its own. |
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

## What is not here yet

- **Live slot updates.** Slot content is placed once, at mount. When the C# that produced it
  re-renders, the component does not yet see the new content — see [Slots](#slots) for why that is the
  hard half.
- **Vue, Svelte, Angular.** The adapter seam is three functions wide and the client runtime imports no
  framework, so these are additive. Angular is viable through standalone components plus
  `createApplication()`, which needs no root component and no NgModule; its build is the real cost,
  since Angular components need the Angular compiler rather than plain Vite.
- **Blazor.** `.razor` components, with props staying C# and never becoming JSON — and static prerender
  needing no bundler at all.
- **Server-side rendering** for the bundler-backed runtimes, which is what would make `Hydration.None`
  broadly useful.
