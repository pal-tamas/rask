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

> **Prerequisites:** the **.NET 10 SDK** (`dotnet --version` ≥ `10.0`); the `wasm-tools` workload
> (`dotnet workload install wasm-tools`) for the WASM templates only.

```bash
dotnet new install Rask.Templates        # scaffolding
dotnet new rask-server -o MyApp          # or: rask-wasm, rask-wasm-hosted
```

Or add to an existing project:

```bash
dotnet add package Rask.Server            # server-rendered over WebSockets
dotnet add package Rask.Wasm              # client-side WebAssembly
dotnet add package Rask.Wasm.Hosting      # host a published WASM bundle on ASP.NET
dotnet add package Rask.Bootstrap          # optional: typed Bootstrap 5.3 components
```

## Why Rask

After 15+ years building full-stack .NET apps — WebForms, MVC, Angular and React over a C# API — I wanted the front end
back in C# without `.razor` mixing markup and code. So Rask makes a component a plain C# class that returns a tree, runs
the *same* code on Server or WASM, and treats the network as the bottleneck (a state change ships a minimal diff, not
the page). It's a craft project built in the open, deep on Roslyn source generators and tree diffing.

- **One component model, two runtimes** — the same C# component runs Server (live diff over WS) or WASM.
- **Compile-time factories** — a Roslyn generator emits `Div(...)`, `Counter(...)`, type-safe routes.
- **Scoped CSS & JS** — sibling `Component.css`/`Component.js`, content-addressed and cached.
- **Routing, lifecycle, forms, validation, auth** — batteries included, no JavaScript required.
- **Tiny live updates** — a minimal edit-op diff ships instead of the whole page.
- **Slow-link aware** — WASM boot shows download progress; a slow Server round-trip surfaces a pending bar.
- **Optional typed Bootstrap** — `Rask.Bootstrap` adds typed Bootstrap 5.3 factories (`BsButton`/`BsCard`/`BsModal`/…), `IFormControl<T>`-bound inputs, a typed `BsIcon`, and typed utility classes, with interactive components driven by the live runtime — no JavaScript. See [docs/bootstrap.md](https://github.com/pal-tamas/rask/blob/main/docs/bootstrap.md).

## Links

- 📖 **[Documentation](https://github.com/pal-tamas/rask/tree/main/docs)** ·
  [Getting started](https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md) ·
  [Configuration](https://github.com/pal-tamas/rask/blob/main/docs/configuration.md) ·
  [Observability](https://github.com/pal-tamas/rask/blob/main/docs/observability.md) ·
  [Accessibility](https://github.com/pal-tamas/rask/blob/main/docs/accessibility.md)
- 🚀 **[Live demo](https://pal-tamas.github.io/rask/)**
- 💻 **[Source & README](https://github.com/pal-tamas/rask)**
- 🤖 **[AI assistant guide](https://github.com/pal-tamas/rask/blob/main/llms.txt)**

Licensed under MIT.
