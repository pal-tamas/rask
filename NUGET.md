<div align="center">

<img alt="Rask" src="https://raw.githubusercontent.com/pal-tamas/rask/main/assets/rask-logo.svg" width="280">

### Live web apps in C#. One codebase — server-rendered over WebSockets, or client-side in the browser via WebAssembly.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/pal-tamas/rask/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

</div>

Write components as plain C# classes. Return a tree of HTML from `Render()`. **No `.razor`, no JSX,
no JavaScript to write** — and the *same* component code runs server-rendered with live WebSocket
updates or fully client-side on WebAssembly.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override RenderResult Render() =>
    [
        H1()["Counter"],
        P()[$"Current count: {_count}"],
        Button(OnClick: () => _count++)["Click me"]
    ];
}
```

## Install

```bash
dotnet new install Rask.Templates        # scaffolding
dotnet new rask-server -o MyApp          # or: rask-wasm, rask-wasm-hosted
```

Or add to an existing project:

```bash
dotnet add package Rask.Server            # server-rendered over WebSockets
dotnet add package Rask.Wasm              # client-side WebAssembly
dotnet add package Rask.Wasm.Hosting      # host a published WASM bundle on ASP.NET
```

## Why Rask

- **One component model, two runtimes** — the same C# component runs Server (live diff over WS) or WASM.
- **Compile-time factories** — a Roslyn generator emits `Div(...)`, `Counter(...)`, type-safe routes.
- **Scoped CSS & JS** — sibling `Component.css`/`Component.js`, content-addressed and cached.
- **Routing, lifecycle, forms, validation, auth** — batteries included, no JavaScript required.
- **Tiny live updates** — a minimal edit-op diff ships instead of the whole page.

## Links

- 📖 **[Documentation](https://github.com/pal-tamas/rask/tree/main/docs)** ·
  [Getting started](https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md)
- 🚀 **[Live demo](https://pal-tamas.github.io/rask/)**
- 💻 **[Source & README](https://github.com/pal-tamas/rask)**
- 🤖 **[AI assistant guide](https://github.com/pal-tamas/rask/blob/main/llms.txt)**

Licensed under MIT.
