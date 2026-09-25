# Rask

The **shared core** of [Rask](https://rask.sh/), a full-stack .NET web framework for teams of any size:
everything the server host and the browser host have in common.

- **Components in C#** — `Component`, the element chain (`Div.Class("panel")[Span["hi"]]`), every HTML and
  SVG element, `Text`/`Raw`/`Fragment`.
- **Routing** — `[Route]`, nested layouts, type-safe `Routes.X()` URLs.
- **Forms** — two-way binding, validation, submit state.
- **Typed browser APIs** — 50+ wrappers (`IGeolocation`, `IClipboard`, `IWebPush`, …) that work the same on
  both hosts.
- **Scoped CSS and TypeScript** — a `Counter.css` or `Counter.ts` beside `Counter.cs`, compiled by `dotnet build`.
- The **source generators** and analyzers that make the chain and the routes exist.

## Install

An application does not reference this package directly — it comes with the host:

```bash
dotnet add package Rask.Server   # an ASP.NET app: live pages over a WebSocket, every battery included
dotnet add package Rask.Wasm     # a browser app on .NET WebAssembly
```

A **library of components** used by either host references the core alone:

```bash
dotnet add package Rask
```

```csharp
public sealed partial class ProductCard : Component
{
    public required string Title { get; set; }   // a required step: ProductCard.Title("…")
    public string? Subtitle { get; set; }        // an optional setter

    protected override Component? Render() =>
        Div.Class("card")[H2[Title], Subtitle is null ? null : P[Subtitle]];
}
```

## Links

- [Documentation](https://rask.sh/docs) · [Building components](https://rask.sh/docs/guides/building-components)
- [Source and issues](https://github.com/pal-tamas/rask)
