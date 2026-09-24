# Rask.External

**Islands**: a React, Preact, Solid, Vue, Svelte, Angular or Lit component as an ordinary component in
[Rask](https://rask.sh/), a full-stack .NET web framework for teams of any size. It goes anywhere the chain
goes — a leaf inside a card, a subtree, or a whole `[Route]` page.

- **The base class is the declaration.** Derive from `ReactComponent`, `PreactComponent`, `SolidComponent`,
  `VueComponent`, `SvelteComponent`, `AngularComponent` or `LitComponent` and drop the front-end file beside
  it — `Chart.cs` pairs with `Chart.tsx`.
- **Props are declared in C#** and serialized reflection-free, so an island survives trimming and AOT.
- **The types cross back.** The build generates a TypeScript interface per component, so renaming a C#
  property stops the front-end file compiling.
- **Callbacks re-enter C#** over the page's existing channel.
- **npm components directly.** A package island (`Mui.Button`) needs no front-end file at all.
- The island's subtree is a diff boundary: the live diff leaves it to its own renderer.

## Install

```bash
dotnet add package Rask.External
```

Node is needed at build time; the build bundles the islands with Vite.

## Use

```csharp
// Features/Dashboard/Chart.cs
public sealed partial class Chart : ReactComponent
{
    public required IReadOnlyList<Point> Series { get; set; }
    public string? Heading { get; set; }
}
```

```tsx
// Features/Dashboard/Chart.tsx
import type { ChartProps } from '@rask/Chart.props'

export default function Chart({ series, heading }: ChartProps) {
  return <figure><figcaption>{heading}</figcaption><Plot data={series} /></figure>
}
```

```csharp
Div.Class("grid")[
    H1["Revenue"],                             // Rask
    Chart.Series(_points).Heading("This year")  // Chart.tsx, rendered by React
]
```

Guide: [Islands](https://rask.sh/docs/guides/islands)
