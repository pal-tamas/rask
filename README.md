# Rask

> A C# component framework for building live web apps — server-rendered over WebSockets, or fully client-side in the
> browser via WebAssembly.

[![NuGet Rask.Server](https://img.shields.io/nuget/v/Rask.Server.svg?label=Rask.Server)](https://www.nuget.org/packages/Rask.Server)
[![NuGet Rask.Wasm](https://img.shields.io/nuget/v/Rask.Wasm.svg?label=Rask.Wasm)](https://www.nuget.org/packages/Rask.Wasm)
[![NuGet Rask.Wasm.Hosting](https://img.shields.io/nuget/v/Rask.Wasm.Hosting.svg?label=Rask.Wasm.Hosting)](https://www.nuget.org/packages/Rask.Wasm.Hosting)
[![NuGet Rask.Templates](https://img.shields.io/nuget/v/Rask.Templates.svg?label=Rask.Templates)](https://www.nuget.org/packages/Rask.Templates)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

## What is Rask?

*Rask* is the Norwegian/Danish/Swedish word for **fast** or **quick**.

Rask is a component framework for .NET. You write components as plain C# classes, return a tree of HTML from `Render()`,
and host the result one of three ways: server-rendered with live updates over a WebSocket, fully client-side in the
browser via WebAssembly, or an ASP.NET app that serves a published WASM bundle. The **same component code runs under
either host** — only the hosting glue changes.

What makes it different from other component frameworks:

- **Text-first DSL.** No `.razor`, no JSX. You call `Div(...)[Span(...), "hi"]`, `Button(...)["click"]`, `H1()["title"]`
  from C# — children attach through an indexer on every component, so the tree reads top-down like HTML and stays
  type-checked, refactor-safe, and IDE-friendly.
- **Source-generated factories.** Define `class Counter : Component` and a `Counter()` factory is generated for you.
  Required vs. optional parameters fall out of property nullability automatically.
- **Type-safe URLs.** Every `[Route]` becomes a generated URL builder — `NavLink(HomePage(), ...)` instead of `"/"`
  strings that rot.
- **Scoped CSS, colocated.** Override `protected override string? Css =>` on a component and selectors are auto-scoped
  to that type and hot-reloaded.
- **Constructor DI in components.** `class Weather(IWeatherForecastService svc) : Component` works directly — no
  `[Inject]` properties, no boilerplate.
- **Error boundaries.** `ErrorBoundary(...)` catches render-time, lifecycle, and event-handler faults in its subtree
  and renders a fallback with a one-shot `recover` callback — no app-wide crashes from a bad descendant.
- **Animated route transitions.** `Navigator.Navigate(...)` wraps the next morph in the browser's View Transitions
  API, so route changes crossfade by default (customisable per-element via the CSS `view-transition-name` property).
- **Forms with async validation.** `Form(Model: …)` auto-attaches a DataAnnotations validator and routes submit
  through `OnValidSubmit` / `OnInvalidSubmit`. Implement `IAsyncFieldValidator` for server-side rules — the submit
  bridge awaits async checks before routing, and rapid keystrokes cancel any prior in-flight validation (latest-wins).

## Install

### Scaffold a new project with `dotnet new` (recommended)

The fastest way to start. `Rask.Templates` ships three project templates — one per host model — already wired up to the
matching framework package:

```bash
dotnet new install Rask.Templates

dotnet new rask-server       -n MyApp    # ASP.NET live-server app
dotnet new rask-wasm         -n MyApp    # standalone browser-WASM SPA
dotnet new rask-wasm-hosted  -n MyApp    # browser-WASM client + ASP.NET host
```

Each template emits a runnable solution with `App` + `HomePage` + `Counter` + `Weather` (async DI demo). `rask-server`
and `rask-wasm` are single-project; `rask-wasm-hosted` is a two-project solution (`MyApp.Wasm/` + `MyApp.Host/`)
pre-wired with the cross-TFM ProjectReference and a sample `/api/weatherforecast` endpoint.

`cd MyApp && dotnet run` — that's it.

### Add packages to an existing project

Three NuGet packages, one per host model. Pick the one that matches the project you're authoring:

| Host model               | Package             | Project type                                                        | Entry-point API                                             |
|--------------------------|---------------------|---------------------------------------------------------------------|-------------------------------------------------------------|
| Server live (WebSockets) | `Rask.Server`       | `net10.0` ASP.NET                                                   | `services.AddRask()` + `app.UseRask<TApp>()`                |
| Browser WASM             | `Rask.Wasm`         | `net10.0-browser`                                                   | `WasmHostBuilder.CreateDefault()` + `host.RunAsync<TApp>()` |
| WASM bundle host         | `Rask.Wasm.Hosting` | `net10.0` ASP.NET (with a `<ProjectReference>` to the WASM project) | `app.UseRask()`                                             |

```bash
dotnet add package Rask.Server        # server live host
dotnet add package Rask.Wasm          # browser WASM client
dotnet add package Rask.Wasm.Hosting  # ASP.NET host serving a WASM bundle
```

`Rask.Server` and `Rask.Wasm` each bundle the core component types and source generators. `Rask.Wasm.Hosting` depends on
`Rask.Wasm` and pulls those in transitively.

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

**`App.cs`** — the page root. The `Rask.Server` and `Rask.Wasm` packages auto-import `Rask.Core` and the
generator-emitted factory namespaces (`Rask.Core.Components.Components`, `Rask.Core.Routing.Components`),
so `Component`, `Div(...)`, `H1(...)`, `Router()`, `Route<T>(...)` etc. are in scope project-wide with no
`using` lines.

```csharp
namespace MyApp;

public sealed class App : Component
{
    public override Component Render() =>
        Fragment()[
            Doctype(),
            Html("en")[
                Head()[
                    Meta("utf-8"),
                    Title()["My Rask App"],
                    RaskScopedStyles()
                ],
                Body()[
                    Router(),
                    RaskRuntimeScript()
                ]
            ]
        ];
}
```

**`HomePage.cs`** — your first route. `Rask.Core.Routing` (for `[Route]`, `[RouteParam]`, `Navigator`, …) is the one
namespace you still bring in explicitly.

```csharp
using Rask.Core.Routing;

namespace MyApp;

[Route("/")]
public sealed class HomePage : Component
{
    public override Component Render() =>
        Fragment()[
            H1()["Hello, world!"],
            P()["Welcome to your new Rask app."]
        ];
}
```

Run `dotnet run` and open the printed URL.

## Quick Start — WASM

Two projects: the WASM client itself, and an ASP.NET host that serves the published bundle. The `App.cs` from the server
quick start works here unchanged.

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

`app.UseRask()` mounts the published WASM AppBundle as static files with sensible MIME types, no-cache revalidation, and
a SPA fallback so client-side routes resolve. Add your `/api/...` endpoints alongside it.

## Core concepts

### Components

Every component is a `sealed class : Component`. Override `Render()` and return a tree. Children attach via the
`Component this[params IEnumerable<Child>]` indexer — strings and `Component`s convert implicitly to `Child`, so
`H1()["Hello"]` and `Div()[Span(...), "text"]` both work.

```csharp
public sealed class Greeting : Component
{
    public string? Name { get; set; }
    public override Component Render() => H1()[$"Hello, {Name ?? "world"}!"];
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

Local state on fields, event handlers as plain delegates. A click triggers a server round-trip (server host) or a local
re-render (WASM host) — same code.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    public override Component Render() =>
        Fragment()[
            H1()["Counter"],
            P()[$"Current count: {_count}"],
            Button(OnClick: () => _count++)["Click me"]
        ];
}
```

### Async data

Override `OnMountAsync` (runs once per instance) or `OnPropsChangedAsync` (runs every render). Each `await`
triggers an automatic re-render after the continuation, so a loading placeholder turns into real data with no manual
`StateHasChanged()`.

```csharp
[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync();

    public override Component Render() =>
        _forecasts is null
            ? P()[Em()["Loading..."]]
            : Table()[/* render rows */];
}
```

### Routing

`[Route]` registers a page. `[RouteParam]` and `[QueryParam]` bind URL pieces to properties. The generator emits a
strongly-typed URL builder for each route, so links don't carry stringly-typed paths.

```csharp
[Route("/users/{id}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }

    public override Component Render() => Span()[$"User #{Id} — {Tab ?? "overview"}"];
}

// elsewhere:
NavLink(UserPage(id: 42))["View user"];
```

Inside event handlers, navigate via the scoped `Navigator` service: `nav.Navigate(HomePage())`,
`nav.SetQuery("tab", "settings")`, etc. Inject it through the constructor like any other service. Navigations
animate by default — the morph is wrapped in `document.startViewTransition()` when the browser supports it.

Mark a component `[NotFound]` to register it as the catch-all 404 page; the framework falls back to a minimal
built-in page if no app-defined one exists.

### Error boundaries

Wrap any subtree in `ErrorBoundary(...)` to catch render-time, sync/async lifecycle, and event-handler exceptions
thrown by descendants. The fallback receives the exception plus a `recover` callback so the boundary can be reset.

```csharp
ErrorBoundary(
    Fallback: (ex, recover) => Div()[
        Strong()["Something went wrong: "], ex.Message,
        Button(OnClick: recover)["Try again"]
    ])[
    // any subtree — render, lifecycle, or handler faults all bubble here
    RiskyChild()
]
```

Pass `ResetKeys: [someId]` to auto-clear the error when the keys change (React `useEffect`-deps semantics). Without
a `Fallback`, the boundary renders a built-in default error page.

### Forms & validation

Bind inputs two-way with `Input(Bind: () => model.Field)` — the input type is inferred from the property's CLR type
(string → text, bool → checkbox, int → number, DateOnly → date, …) and new values flow back into the model on each
event. `Form(Model: …)` auto-registers a `DataAnnotationsValidator` and routes submit through `OnValidSubmit` /
`OnInvalidSubmit` once every `[Required]` / `[EmailAddress]` / `[Range]` check passes. Field errors render via
`ValidationMessage`; a top-of-form digest via `ValidationSummary`.

For async rules (uniqueness probes, remote checks), implement `IAsyncFieldValidator` and add it to a pre-built
`EditContext`. The submit bridge awaits async validation before routing, and rapid keystrokes cancel any prior
in-flight per-field check (latest-wins). `ValidatingIndicator` renders its children while a field is being checked.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
}

public sealed class UniqueUsernameValidator : IAsyncFieldValidator
{
    public async ValueTask ValidateFieldAsync(
        EditContext ctx, FieldIdentifier field, CancellationToken ct)
    {
        if (ctx.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
        {
            await Task.Delay(400, ct);                    // pretend it's an API call
            if (await IsTakenAsync(m.Username))
                ctx.AddValidationMessage(field, "Already taken.");
        }
    }
    public ValueTask ValidateAsync(EditContext c, CancellationToken ct) => default;
}

var ctx = new EditContext(model);
ctx.AddValidator(new DataAnnotationsValidator());
ctx.AddValidator(new UniqueUsernameValidator());

Form<SignupModel>(model, Context: ctx, OnValidSubmit: m => Console.WriteLine(m.Username))[
    Input(() => model.Username),
    ValidatingIndicator(() => model.Username)["Checking..."],
    ValidationMessage(() => model.Username),
    Button(Type: "submit")["Sign up"]
]
```

### Scoped CSS

Colocate styles on the component. Selectors are auto-scoped to the component type, served once from `/_rask/scoped.css`,
and hot-reloaded under `dotnet watch`.

```csharp
public sealed class Card : Component
{
    protected override string? Css => """
        .card { padding: 1rem; border-radius: 8px; border: 1px solid #ddd; }
        .card:hover { background: #f7f7f7; }
    """;

    public override Component Render() =>
        Div(Class: "card")["..."];
}
```

Place `RaskScopedStyles()` once inside `<head>` (see `App.cs` in the server quick start).

### Lifecycle reference

| Hook                                     | When                                               |
|------------------------------------------|----------------------------------------------------|
| `OnMount` / `OnMountAsync`               | Once, on first instance creation                   |
| `OnPropsChanged` / `OnPropsChangedAsync` | Every render after props are applied               |
| `OnRendered` / `OnRenderedAsync`         | After every render, with a `firstRender` flag      |
| `StateHasChanged()`                      | Call to force a re-render outside an event handler |

## Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts,
`net10.0-browser` for WASM projects). Production use at your own discretion — issues and PRs welcome.

## License

Rask is released under the [MIT License](LICENSE).
