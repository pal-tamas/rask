# Rask

> A C# component framework for building live web apps — server-rendered over WebSockets, or fully client-side in the browser via WebAssembly.

[![NuGet Rask.Server](https://img.shields.io/nuget/v/Rask.Server.svg?label=Rask.Server)](https://www.nuget.org/packages/Rask.Server)
[![NuGet Rask.Wasm](https://img.shields.io/nuget/v/Rask.Wasm.svg?label=Rask.Wasm)](https://www.nuget.org/packages/Rask.Wasm)
[![NuGet Rask.Wasm.Hosting](https://img.shields.io/nuget/v/Rask.Wasm.Hosting.svg?label=Rask.Wasm.Hosting)](https://www.nuget.org/packages/Rask.Wasm.Hosting)
[![NuGet Rask.Templates](https://img.shields.io/nuget/v/Rask.Templates.svg?label=Rask.Templates)](https://www.nuget.org/packages/Rask.Templates)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

## What is Rask?

*Rask* is the Norwegian/Danish/Swedish word for **fast** or **quick**.

Rask is a component framework for .NET. You write components as plain C# classes, return a tree of HTML from `Render()`, and host the result one of three ways: server-rendered with live updates over a WebSocket, fully client-side in the browser via WebAssembly, or an ASP.NET app that serves a published WASM bundle. The **same component code runs under either host** — only the hosting glue changes.

What makes it different from other component frameworks:

- **Text-first DSL.** No `.razor`, no JSX. You call `Div(...)`, `Button(...)`, `H1(...)` from C# — type-checked, refactor-safe, and IDE-friendly.
- **Source-generated factories.** Define `class Counter : Component` and a `Counter()` factory is generated for you. Required vs. optional parameters fall out of property nullability automatically.
- **Type-safe URLs.** Every `[Route]` becomes a generated URL builder — `NavLink(HomePage(), ...)` instead of `"/"` strings that rot.
- **Scoped CSS, colocated.** Override `protected override string? Css =>` on a component and selectors are auto-scoped to that type and hot-reloaded.
- **Constructor DI in components.** `class Weather(IWeatherForecastService svc) : Component` works directly — no `[Inject]` properties, no boilerplate.

## Install

### Scaffold a new project with `dotnet new` (recommended)

The fastest way to start. `Rask.Templates` ships three project templates — one per host model — already wired up to the matching framework package:

```bash
dotnet new install Rask.Templates

dotnet new rask-server       -n MyApp    # ASP.NET live-server app
dotnet new rask-wasm         -n MyApp    # standalone browser-WASM SPA
dotnet new rask-wasm-hosted  -n MyApp    # browser-WASM client + ASP.NET host
```

Each template emits a runnable solution with `App` + `HomePage` + `Counter` + `Weather` (async DI demo). `rask-server` and `rask-wasm` are single-project; `rask-wasm-hosted` is a two-project solution (`MyApp.Wasm/` + `MyApp.Host/`) pre-wired with the cross-TFM ProjectReference and a sample `/api/weatherforecast` endpoint.

`cd MyApp && dotnet run` — that's it.

### Add packages to an existing project

Three NuGet packages, one per host model. Pick the one that matches the project you're authoring:

| Host model | Package | Project type | Entry-point API |
|---|---|---|---|
| Server live (WebSockets) | `Rask.Server` | `net10.0` ASP.NET | `services.AddRask()` + `app.UseRask<TApp>()` |
| Browser WASM | `Rask.Wasm` | `net10.0-browser` | `WasmHostBuilder.CreateDefault()` + `host.RunAsync<TApp>()` |
| WASM bundle host | `Rask.Wasm.Hosting` | `net10.0` ASP.NET (with a `<ProjectReference>` to the WASM project) | `app.UseRask()` |

```bash
dotnet add package Rask.Server        # server live host
dotnet add package Rask.Wasm          # browser WASM client
dotnet add package Rask.Wasm.Hosting  # ASP.NET host serving a WASM bundle
```

`Rask.Server` and `Rask.Wasm` each bundle the core component types and source generators. `Rask.Wasm.Hosting` depends on `Rask.Wasm` and pulls those in transitively.

## Quick Start — Server

Three files. Live, server-rendered, no JavaScript to write.

**`Program.cs`**

```csharp
using Rask.Server;
using MyApp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRask();

var app = builder.Build();
app.UseRask<App>();
app.Run();
```

**`App.cs`** — the page root.

```csharp
using Rask.Core;
using static Rask.Core.Tags;

namespace MyApp;

public sealed class App : Component
{
    public override Component Render() =>
        Fragment(
            Doctype(),
            Html("en", Children:
            [
                Head(Children:
                [
                    Meta("utf-8"),
                    Title(Children: ["My Rask App"]),
                    RaskScopedStyles()
                ]),
                Body(Children:
                [
                    Router(),
                    RaskRuntimeScript()
                ])
            ])
        );
}
```

**`HomePage.cs`** — your first route.

```csharp
using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace MyApp;

[Route("/")]
public sealed class HomePage : Component
{
    public override Component Render() =>
        Fragment(
            H1(Children: ["Hello, world!"]),
            P(Children: ["Welcome to your new Rask app."])
        );
}
```

Run `dotnet run` and open the printed URL.

## Quick Start — WASM

Two projects: the WASM client itself, and an ASP.NET host that serves the published bundle. The `App.cs` from the server quick start works here unchanged.

**WASM client `Program.cs`** (`net10.0-browser`):

```csharp
using Rask.Wasm;
using MyApp;

var host = WasmHostBuilder.CreateDefault();
host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });

await host.RunAsync<App>();
```

**Host `Program.cs`** (`net10.0`, with a `<ProjectReference>` to the WASM project):

```csharp
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseRask();
app.Run();
```

`app.UseRask()` mounts the published WASM AppBundle as static files with sensible MIME types, no-cache revalidation, and a SPA fallback so client-side routes resolve. Add your `/api/...` endpoints alongside it.

## Core concepts

### Components

Every component is a `sealed class : Component` (or `: Component<TProps>` for tag-shaped wrappers). Override `Render()` and return a tree.

```csharp
public sealed class Greeting : Component
{
    public string? Name { get; set; }
    public override Component Render() => H1(Children: [$"Hello, {Name ?? "world"}!"]);
}
```

The source generator emits a `Greeting(...)` factory automatically:

- Non-nullable property with no initialiser → **required** factory parameter.
- Nullable property with no initialiser → optional, defaults to `null`.
- Property with an initialiser → kept out of the factory; your default wins.
- `[SkipFactory]` on a property excludes it explicitly.

Inject framework services through the **constructor**, not as properties:

```csharp
public sealed class Weather(IWeatherForecastService service) : Component { ... }
```

### Interactivity

Local state on fields, event handlers as plain delegates. A click triggers a server round-trip (server host) or a local re-render (WASM host) — same code.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    public override Component Render() =>
        Fragment(
            H1(Children: ["Counter"]),
            P(Children: [$"Current count: {_count}"]),
            Button(
                OnClick: () => _count++,
                Children: ["Click me"])
        );
}
```

### Async data

Override `OnInitializedAsync` (runs once per instance) or `OnParametersSetAsync` (runs every render). Each `await` triggers an automatic re-render after the continuation, so a loading placeholder turns into real data with no manual `StateHasChanged()`.

```csharp
[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnInitializedAsync() =>
        _forecasts = await service.GetForecastsAsync();

    public override Component Render() =>
        _forecasts is null
            ? P(Children: [Em(Children: ["Loading..."])])
            : Table(Children: [/* render rows */]);
}
```

### Routing

`[Route]` registers a page. `[RouteParam]` and `[QueryParam]` bind URL pieces to properties. The generator emits a strongly-typed URL builder for each route, so links don't carry stringly-typed paths.

```csharp
[Route("/users/{id}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }

    public override Component Render() => Span(Children: [$"User #{Id} — {Tab ?? "overview"}"]);
}

// elsewhere:
NavLink(UserPage(id: 42), Children: ["View user"]);
```

Inside event handlers, navigate via the scoped `Navigator` service: `nav.Navigate(HomePage())`, `nav.SetQuery("tab", "settings")`, etc. Inject it through the constructor like any other service.

### Scoped CSS

Colocate styles on the component. Selectors are auto-scoped to the component type, served once from `/_rask/scoped.css`, and hot-reloaded under `dotnet watch`.

```csharp
public sealed class Card : Component
{
    protected override string? Css => """
        .card { padding: 1rem; border-radius: 8px; border: 1px solid #ddd; }
        .card:hover { background: #f7f7f7; }
    """;

    public override Component Render() =>
        Div(Class: "card", Children: ["..."]);
}
```

Place `RaskScopedStyles()` once inside `<head>` (see `App.cs` in the server quick start).

### Lifecycle reference

| Hook | When |
|---|---|
| `OnInitialized` / `OnInitializedAsync` | Once, on first instance creation |
| `OnParametersSet` / `OnParametersSetAsync` | Every render after props are applied |
| `StateHasChanged()` | Call to force a re-render outside an event handler |

## Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts, `net10.0-browser` for WASM projects). Production use at your own discretion — issues and PRs welcome.

## License

Rask is released under the [MIT License](LICENSE).
