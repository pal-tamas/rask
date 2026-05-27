# Rask

> A C# component framework for building live web apps — server-rendered over WebSockets, or fully client-side in the
> browser via WebAssembly.

[![NuGet Rask.Server](https://img.shields.io/nuget/v/Rask.Server.svg?label=Rask.Server)](https://www.nuget.org/packages/Rask.Server)
[![NuGet Rask.Wasm](https://img.shields.io/nuget/v/Rask.Wasm.svg?label=Rask.Wasm)](https://www.nuget.org/packages/Rask.Wasm)
[![NuGet Rask.Wasm.Hosting](https://img.shields.io/nuget/v/Rask.Wasm.Hosting.svg?label=Rask.Wasm.Hosting)](https://www.nuget.org/packages/Rask.Wasm.Hosting)
[![NuGet Rask.Templates](https://img.shields.io/nuget/v/Rask.Templates.svg?label=Rask.Templates)](https://www.nuget.org/packages/Rask.Templates)
[![NuGet Rask.Validation.DataAnnotations](https://img.shields.io/nuget/v/Rask.Validation.DataAnnotations.svg?label=Rask.Validation.DataAnnotations)](https://www.nuget.org/packages/Rask.Validation.DataAnnotations)
[![NuGet Rask.Validation.FluentValidation](https://img.shields.io/nuget/v/Rask.Validation.FluentValidation.svg?label=Rask.Validation.FluentValidation)](https://www.nuget.org/packages/Rask.Validation.FluentValidation)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

## Contents

- [What is Rask?](#what-is-rask)
- [Compared to Blazor](#compared-to-blazor)
- [Install](#install)
- [Quick Start — Server](#quick-start--server)
- [Quick Start — WASM](#quick-start--wasm)
- [Core concepts](#core-concepts)
    - [Components](#components)
    - [Interactivity](#interactivity)
    - [Async data](#async-data)
    - [Routing](#routing)
    - [Page head contributions](#page-head-contributions)
    - [Error boundaries](#error-boundaries)
    - [Forms & validation](#forms--validation)
    - [Files: upload and download](#files-upload-and-download)
    - [Virtualization](#virtualization)
    - [Scoped CSS](#scoped-css)
    - [Scoped JS](#scoped-js)
    - [Lifecycle reference](#lifecycle-reference)
- [Performance](#performance)
- [Status](#status)
- [License](#license)

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
- **Scoped CSS, colocated.** Drop a sibling `{Component}.css` next to `{Component}.cs` and selectors are auto-scoped
  to that type and hot-reloaded — no class-name discipline, no BEM, no leaks.
- **Constructor DI in components.** `class Weather(IWeatherForecastService svc) : Component` works directly — no
  `[Inject]` properties, no boilerplate.
- **Error boundaries.** `ErrorBoundary(...)` catches render-time, lifecycle, and event-handler faults in its subtree
  and renders a fallback with a one-shot `recover` callback — no app-wide crashes from a bad descendant.
- **Forms with async validation.** `Form<TModel>(model, OnValidSubmit: …)` routes submit through validators you opt
  into by dropping `DataAnnotationsValidator()` or `FluentValidationValidator(...)` inside the form as children.
  Implement `IAsyncFieldValidator` for ad-hoc server-side rules — the submit bridge awaits async checks before routing,
  and rapid keystrokes cancel any prior in-flight validation (latest-wins).

## Compared to Blazor

If you've worked in Blazor, here's how the day-to-day differs in Rask:

- **No `.razor`.** Components are plain C# classes with an indexer for children — `Div(...)[Span(...), "hi"]` instead
  of mixed markup + code. The whole tree is C# expressions, so refactors, find-references, and IDE navigation just
  work.
- **No `[Inject]`.** Services come in through the constructor (`Counter(IClock clock) : Component`), exactly like
  anywhere else in .NET. Framework services (`Navigator`, `RouteState`, `HttpClient`) inject the same way.
- **No `@page "/path"`.** `[Route("/path")]` goes on the class, and every route gets a generated **type-safe URL
  builder** — `NavLink(UserPage(id: 42))` instead of `"/users/42"` strings that rot when the route changes.
- **No `RenderFragment` / `EventCallback`.** Children are `IEnumerable<Child>` and event handlers are plain delegates
  (`OnClick: () => _count++`). No specialised types, no `@bind-Value:event`.
- **Scoped CSS via sibling `.css`** (Blazor-parity descendant combinators) — auto-globbed at build time, hot-reloaded
  under `dotnet watch`, with no `.razor.css` association ceremony. Same idea for JS: a sibling `{Component}.js` is
  bundled and dispatched by the framework.
- **Same component code on Server or WASM.** Pick the host package per project; you don't rewrite components when
  switching render mode. Server-only behaviours (multipart upload) and WASM-only behaviours (chunked file reads,
  inline downloads) live in the hosts, not in your tree.

Rask isn't a Blazor replacement so much as a different take on the same problem space. If those trade-offs appeal, the
rest of this README walks through what they look like in practice.

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

Pick one host package per project, then add validation packages as needed:

| Package                            | Project type                                                        | Entry-point API                                             |
|------------------------------------|---------------------------------------------------------------------|-------------------------------------------------------------|
| `Rask.Server`                      | `net10.0` ASP.NET                                                   | `services.AddRask()` + `app.UseRask<TApp>()`                |
| `Rask.Wasm`                        | `net10.0-browser`                                                   | `WasmHostBuilder.CreateDefault()` + `host.RunAsync<TApp>()` |
| `Rask.Wasm.Hosting`                | `net10.0` ASP.NET (with a `<ProjectReference>` to the WASM project) | `app.UseRask()`                                             |
| `Rask.Validation.DataAnnotations`  | any host (referenced from the project that hosts your forms)        | drop `DataAnnotationsValidator()` inside a `Form<T>`        |
| `Rask.Validation.FluentValidation` | any host (referenced from the project that hosts your forms)        | drop `FluentValidationValidator(new MyValidator())` inside  |

```bash
dotnet add package Rask.Server                       # server live host
dotnet add package Rask.Wasm                         # browser WASM client
dotnet add package Rask.Wasm.Hosting                 # ASP.NET host serving a WASM bundle
dotnet add package Rask.Validation.DataAnnotations   # opt-in: System.ComponentModel.DataAnnotations
dotnet add package Rask.Validation.FluentValidation  # opt-in: FluentValidation 12.x
```

`Rask.Server` and `Rask.Wasm` each pull in `Rask.Core` and the source generators transitively; `Rask.Wasm.Hosting`
pulls in `Rask.Wasm`. The validation packages add a global `using static` for their factory namespace, so
`DataAnnotationsValidator()` / `FluentValidationValidator(...)` are in scope without extra `using` lines.

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
generator-emitted factory namespaces (`Rask.Core.Components.Generated`, `Rask.Core.Routing.Generated`),
so `Component`, `Div(...)`, `H1(...)`, `Router()`, `Route<T>(...)` etc. are in scope project-wide with no
`using` lines.

```csharp
namespace MyApp;

public sealed class App : Component
{
    // App-level head content goes through the RenderResult Head override.
    // Pages override their own Head to set per-page Title — singleton dedup
    // means the page's contribution supersedes this fallback for the tab.
    protected override RenderResult Head => [
        Title()["My Rask App"],
        Meta("utf-8")
    ];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),                       // framework-managed slot
                Body()[
                    Router(),
                    RaskRuntimeScript()
                ]
            ]
        ];
}
```

`<head>` is framework-managed — passing children to `Head()` is a `RASK019` compile error.
The framework collects every component's `Head` override during render, dedupes
contributions, resolves singleton tags (`<title>`, `<base>` — last contributor wins),
and splices the result plus the scoped-CSS link and scoped-JS bundle script inside
`<head>` automatically.

**`HomePage.cs`** — your first route. `Rask.Core.Routing` (for `[Route]`, `[RouteParam]`, `Navigator`, …) is the one
namespace you still bring in explicitly.

```csharp
using Rask.Core.Routing;

namespace MyApp;

[Route("/")]
public sealed class HomePage : Component
{
    protected override RenderResult Render() =>
        [
            H1()["Hello, world!"],
            P()["Welcome to your new Rask app."]
        ];
}
```

Run `dotnet run` and open the printed URL.

## Quick Start — WASM

Two flavours: a **standalone** SPA that ships as a static bundle, or a **hosted** variant where an ASP.NET project
serves the published WASM bundle alongside your own `/api/...` endpoints. The `App.cs` and `HomePage.cs` from the
server quick start work under both, unchanged.

### Standalone (`rask-wasm`)

One `net10.0-browser` project. `Program.cs`:

```csharp
using Rask.Wasm;
using MyApp;

var host = WasmHostBuilder.CreateDefault();
host.Services.AddSingleton(_ =>
    new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });

await host.RunAsync<App>();
```

`dotnet publish -c Release` emits a static `wwwroot/` directory you can serve from any static host (GitHub Pages, S3,
nginx). No runtime server process is required for the framework itself — bring your own host for whatever public APIs
the client calls.

### Hosted (`rask-wasm-hosted`)

Two projects: the WASM client itself, and an ASP.NET host that serves the published bundle. The host project takes a
cross-TFM `<ProjectReference>` to the WASM project; the auto-imported targets discover the bundle and bake the path
into an assembly attribute at publish.

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
builder.Services.AddRask();              // opt-in: brotli/gzip response compression of the AppBundle

var app = builder.Build();
app.UseRask();
app.Run();
```

`AddRask()` is optional but recommended — it wires `UseResponseCompression` ahead of `UseStaticFiles` so the
precompressed `.br` / `.gz` siblings emitted by the WASM publish step go out with the right `Content-Encoding` and
no runtime compression cost on the framework payload. Skip it and the host still works; you just lose the compression
layer.

`app.UseRask()` mounts the published WASM AppBundle as static files with sensible MIME types, no-cache revalidation,
and a SPA fallback so client-side routes resolve. Add your `/api/...` endpoints alongside it.

## Core concepts

### Components

Every component is a `sealed class : Component`. Override `Render()` and return a tree. Children attach via the
`Component this[params IEnumerable<Child>]` indexer — strings and `Component`s convert implicitly to `Child`, so
`H1()["Hello"]` and `Div()[Span(...), "text"]` both work.

`Render()` (and the `Head` override) return `RenderResult`, which accepts three shapes:

- **A single component** — `Render() => Div()[...]` (converts implicitly).
- **A collection expression** — `Render() => [Doctype(), Html(...)]` for multiple top-level nodes, with no wrapper element. (This is sugar for `Fragment()[...]`; the items are grouped into a `Fragment` internally.)
- **`default`** — render nothing / no contribution. Conditionals target-type each branch, so `Render() => ready ? [Doctype(), Html(...)] : default;` works.

```csharp
public sealed class Greeting : Component
{
    public string? Name { get; set; }
    protected override RenderResult Render() => H1()[$"Hello, {Name ?? "world"}!"];
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

    protected override RenderResult Render() =>
        [
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

The runtime coalesces these into **one payload per handler dispatch**, and the terminal auto re-render is a
*publish-only* walk that won't re-fire `OnRendered` on components that already rendered. That keeps an
`OnRenderedAsync` hook which awaits a next-frame JS call (e.g. drawing a chart) from looping on itself —
newly-mounted children still get their first `OnRendered(firstRender: true)`.

```csharp
[Route("/weather")]
public sealed class Weather(IWeatherForecastService service) : Component
{
    private WeatherForecast[]? _forecasts;

    protected override async Task OnMountAsync() =>
        _forecasts = await service.GetForecastsAsync();

    protected override RenderResult Render() =>
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

    protected override RenderResult Render() => Span()[$"User #{Id} — {Tab ?? "overview"}"];
}

// elsewhere:
NavLink(UserPage(id: 42))["View user"];
```

Inside event handlers, navigate via the scoped `Navigator` service: `nav.Navigate(HomePage())`,
`nav.SetQuery("tab", "settings")`, etc. Inject it through the constructor like any other service.

Mark a component `[NotFound]` to register it as the catch-all 404 page; the framework falls back to a minimal
built-in page if no app-defined one exists.

### Page head contributions

Any component can override `protected virtual RenderResult Head` to declare what belongs in `<head>` while that component
is in the tree. The default is `default` — no contribution; a single tag, a collection expression of several tags, or
`default` for "nothing" are all valid (e.g. `Head => loggedIn ? [Meta(...)] : default`):

```csharp
public sealed class UserDetailPage : Component
{
    [RouteParam] public int Id { get; set; }

    // The framework dedupes by rendered HTML; <title> and <base> are singleton
    // tags — last contributor wins. So this page's Title overrides App's
    // fallback when the user lands on /users/42.
    protected override RenderResult Head => [
        Title()[$"User #{Id} — My Rask App"],
        Meta(Name: "description", Content: $"Profile for user {Id}"),
        Link(Rel: "stylesheet", Href: "https://cdn.example.com/profile.css")
    ];

    protected override RenderResult Render() => /* … */;
}
```

When the user navigates away, the page leaves the tree, its contributions drop from the registry, and the next
render's `<head>` reflects whatever components remain. Multiple instances of the same `Link(Href: "...")` dedupe to
a single emission. The `Head()` HTML element itself is **framework-managed** — passing it children is a `RASK019`
compile error; everything goes through the override.

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

Without a `Fallback`, the boundary renders a built-in default error page. The `recover` callback passed to the
fallback is the only reset path.

### Forms & validation

Bind inputs two-way with `Input(Bind: () => model.Field)` — the input type is inferred from the property's CLR type
(string → text, bool → checkbox, int → number, DateOnly → date, …) and new values flow back into the model on each
event. `Form<TModel>(model, OnValidSubmit: …, OnInvalidSubmit: …)` routes submit through whichever validators are
attached to its `EditContext`. Field errors render via `ValidationMessage` and a top-of-form digest via
`ValidationSummary` — both are headless and take a required `Template:` lambda so you control the markup
(e.g. `Template: errs => Div(Class: "err")[errs[0]]`).

`Input` / `Select` / `Textarea` also accept `AfterBind` / `AfterBindAsync` callbacks that fire **after** the new value
is written to the model (and after validators see the change) — handy for dependent fields that need to rebind in the
same render. Skipped on parse failure or no-op writes.

Validation is opt-in — Rask.Core ships no validator by default. Add the package you want and drop the validator
component inside the form:

- **`Rask.Validation.DataAnnotations`** — `DataAnnotationsValidator()` wires `[Required]` / `[EmailAddress]` / `[Range]`
  / `IValidatableObject` into the form's `EditContext`.
- **`Rask.Validation.FluentValidation`** — `FluentValidationValidator(new MyValidator())` delegates to a
  `FluentValidation.IValidator`, including async rules via `MustAsync`.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, EmailAddress]                        public string Email    { get; set; } = "";
}

[Route("/signup")]
public sealed class SignupPage : Component
{
    private readonly SignupModel _model = new();

    protected override RenderResult Render() =>
        Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
            DataAnnotationsValidator(),                         // opt-in: DA attributes
            Input(Bind: () => _model.Username),
            ValidationMessage(For: () => _model.Username,
                Template: errs => Div(Class: "field-error")[errs[0]]),
            Input(Bind: () => _model.Email),
            ValidationMessage(For: () => _model.Email,
                Template: errs => Div(Class: "field-error")[errs[0]]),
            Button(Type: "submit")["Sign up"]
        ];
}
```

#### Async validation

For ad-hoc async rules (uniqueness probes, remote checks), implement `IAsyncFieldValidator` and add it to a manually
built `EditContext`. The submit bridge awaits async validation before routing, and rapid keystrokes cancel any prior
in-flight per-field check (latest-wins). `ValidatingIndicator` is headless too — pass a `Template:` lambda for
whatever should show while the field is being checked (e.g. `Template: () => Span()["Checking..."]`).

Two lighter-weight alternatives, when a full `IAsyncFieldValidator` is overkill:

- **Per-field inline rule** — pass a `Validate:` lambda directly to an `Input`. Three overloads cover the common
  shapes: omit it, return `IEnumerable<string>` for sync rules, or return `ValueTask<IEnumerable<string>>` for async
  (the `CancellationToken` cancels the in-flight check on the next keystroke). An empty sequence means valid; any
  returned strings become the field's errors.
- **Cross-field rule on the form** — pass `Validate:` to `Form<TModel>` to run a model-level check on submit (great
  for "passwords must match" or "either email or phone is required"). `[FactoryGeneric]` narrows the lambda's
  parameter to `TModel` so it's strongly typed.

Reach for `IAsyncFieldValidator` when the rule needs DI (an `HttpClient`, a repository) or when you want to reuse it
across forms.

```csharp
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

private readonly SignupModel _model = new();
private EditContext? _ctx;

protected override void OnMount()
{
    _ctx = new EditContext(_model);
    _ctx.AddValidator(new UniqueUsernameValidator());
}

protected override RenderResult Render() =>
    Form<SignupModel>(_model, Context: _ctx, OnValidSubmit: m => Console.WriteLine(m.Username))[
        DataAnnotationsValidator(),
        Input(Bind: () => _model.Username),
        ValidatingIndicator(For: () => _model.Username,
            Template: () => Span(Class: "spinner")["Checking..."]),
        ValidationMessage(For: () => _model.Username,
            Template: errs => Div(Class: "field-error")[errs[0]]),
        Button(Type: "submit")["Sign up"]
    ];
```

#### Complex models — sub-objects and lists

`Bind` and validation extend transparently through nested sub-objects and collections. A single
`DataAnnotationsValidator()` or `FluentValidationValidator(...)` at the top of the form covers the whole reachable
graph — there's no per-level opt-in. Validation messages key off the **owner sub-instance**, not a dotted path from
the root, so removing or replacing a row drops its error state with it.

```csharp
public sealed class CheckoutModel
{
    [Required] public string Name { get; set; } = "";
    public AddressModel Address { get; set; } = new();
    public List<LineItem> Items { get; set; } = new();
}

public sealed class AddressModel
{
    [Required] public string Street { get; set; } = "";
    [Required, RegularExpression("^[A-Z]{2}$")] public string Country { get; set; } = "";
}

public sealed class LineItem
{
    [Required] public string Description { get; set; } = "";
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1;
}
```

**Sub-object binding** uses the same `Bind: () => ...` shape as flat models:

```csharp
Input(Bind: () => _model.Address.Street),
ValidationMessage(For: () => _model.Address.Street,
    Template: errs => Div(Class: "field-error")[errs[0]]),
```

**Collection binding — foreach + per-item capture** is the canonical pattern. Each iteration captures a different
`item` reference into its own closure, so each row's lambda points at a distinct instance:

```csharp
foreach (var item in _model.Items)
{
    rows.Add(Tr()[
        Td()[Input(Bind: () => item.Description)],
        Td()[Input(Bind: () => item.Quantity)],
        Td()[Button(Type: "button", OnClick: () => _model.Items.Remove(item))["×"]]
    ]);
}
```

**Collection binding — indexer style** is the alternative when you need the row number for UI (reorder buttons,
"Row #3" labels) or when items are records that get replaced rather than mutated — `() => model.Items[i].Name`
re-resolves the indexer every render, so the binding follows the new slot value through replacement. Watch out for
the classic `for (int i = …)` closure trap: copy the index into a per-iteration local before the lambda captures it.

```csharp
for (var idx = 0; idx < _model.Items.Count; idx++)
{
    var i = idx;                                      // <-- per-iteration capture, NOT idx
    rows.Add(Tr()[
        Td()[$"#{i + 1}"],
        Td()[Input(Bind: () => _model.Items[i].Description)],
        Td()[Input(Bind: () => _model.Items[i].Quantity)]
    ]);
}
```

`foreach` doesn't have the closure trap. Records with init-only properties can't be auto-bound via the `Bind` setter
— either declare the record properties as mutable (`{ get; set; }`), or use the indexer pattern with a manual
`OnChange` that replaces the slot with `_model.Items[i] = _model.Items[i] with { Field = newValue }`.

**FluentValidation nesting** uses `SetValidator(...)` and `RuleForEach(...).SetValidator(...)` in the user validator
— Rask routes the dotted `error.PropertyName` (`Address.Street`, `Lines[0].Quantity`) back to the runtime sub-
instance so `ValidationMessage(For: () => _model.Address.Street, ...)` reads it off the right slot.

**Trimming caveat.** Validating a nested graph reflects over every reachable model type. The trimming contract that
already applies to the root model (preserve its public properties via `[DynamicallyAccessedMembers]` or a
`<TrimmerRootDescriptor>`) extends to every nested type. The full Forms/Complex-models showcase under `/nested-forms`
demonstrates all four patterns side-by-side.

### Files: upload and download

`Input(Type: "file", OnFiles: …)` accepts files; `Navigator.Download(...)` sends them. The same component code runs
unchanged on Server and WASM — only the transport differs (multipart over the WebSocket on the server, JS-Map +
chunked reads on WASM; downloads go through `/_rask/download/{token}` on the server, base64 + Blob URL on WASM).

```csharp
Input(Type: "file", OnFiles: async files => {
    var file = files[0];                                         // RaskFile
    using var s = file.OpenReadStream(maxAllowedSize: 5_000_000); // valid only inside this handler
    await s.CopyToAsync(destination);
})
```

```csharp
public sealed class ReportPage(Navigator nav) : Component
{
    private void Download() =>
        nav.Download("report.txt",
                     Encoding.UTF8.GetBytes("hello"),
                     "text/plain");

    protected override RenderResult Render() =>
        Button(OnClick: Download)["Download report"];
}
```

`RaskFile` exposes `Name`, `Size`, `ContentType`, `LastModified`, plus `OpenReadStream(maxAllowedSize, ct)`. The
stream is only valid while the handler is on the stack — read whatever you need before returning. Inside a `Form`,
files also surface through `FormData.Files(name)` and participate in submit. `Navigator.Download` must be called from
an event handler. See `Rask.Example.Shared/Pages/UploadPage.cs` and `DownloadPage.cs` for the canonical demos.

### Virtualization

`Virtualize<T>` is a headless windowed-list primitive — it emits no DOM of its own and instead invokes the `Render`
delegate with the visible window of items plus the spacer offsets you wire into your own scroll container.

```csharp
Virtualize<Row>(
    Items: _rows,                                  // or ItemsProvider for async paging
    ItemSize: 32,                                  // pixel height of one row
    OverscanCount: 4,
    InitialClientHeight: 400,
    Render: ctx => Div(
        Style: "height:400px; overflow:auto;",
        OnScroll: ctx.OnScroll)[
        Div(Style: $"height:{ctx.OffsetBefore}px"),  // spacer for off-screen rows above
        Table()[
            Tbody()[
                ctx.VisibleItems.Select(item => (Child)Tr()[
                    Td()[$"#{item.Index}"],
                    Td()[item.Value?.Name ?? ""]    // null while a placeholder is loading
                ]).ToArray()
            ]
        ],
        Div(Style: $"height:{ctx.OffsetAfter}px")    // spacer for off-screen rows below
    ])
```

Provide exactly one of `Items` (in-memory) or `ItemsProvider` (async paging:
`Func<ItemsProviderRequest, ValueTask<ItemsProviderResult<T>>>`). With a provider, `Virtualize` caches loaded items by
global index, requests missing windows in the background, and emits placeholder rows with `IsPlaceholder = true` until
a fetch completes. See `Rask.Example.Shared/Pages/VirtualizePage.cs` for a 10K-row table demo.

### Scoped CSS

Drop a sibling `{Component}.css` file next to `{Component}.cs` and the source generator pairs them at compile time —
selectors are auto-scoped to that component type, served from `/_rask/scoped.css`, and hot-reloaded under
`dotnet watch`.

```csharp
// Card.cs
public sealed class Card : Component
{
    protected override RenderResult Render() =>
        Div(Class: "card")["..."];
}
```

```css
/* Card.css — sibling file, no extra wiring */
.card { padding: 1rem; border-radius: 8px; border: 1px solid #ddd; }
.card:hover { background: #f7f7f7; }
```

The showcase uses this throughout: `App.css`, `HomePage.css`, `ScopedRed.css` / `ScopedBlue.css`,
and the `Layout/ShowcaseLayout.css` for the sidebar. Two components can use the same `.box` selector — the framework
rewrites each to `.box[data-{scopeId}]` so they never collide. An orphan `.css` file with no matching component raises
`RASK015`; two `.css` files claiming the same component raise `RASK016`; opt the whole project out with
`<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>` in the `.csproj`.

The bundle is delivered automatically — on the server as a `<link rel="stylesheet" href="/_rask/scoped.css?v=…">`
inside `<head>`, on WASM through the page shell's `<style id="rask-scoped">` slot. No call site or placement is
required; the framework-managed `<head>` (see *Page head contributions* above) splices it in for you.

**Targeting shell tags** — selectors like `body`, `html`, `button` don't carry `data-{scopeId}` (those tags are
intentionally excluded from stamping), so a sibling rule like `body { ... }` would never match. Wrap the selector in
`:global(...)` to opt out of scoping:

```css
:global(body) {
    overscroll-behavior-y: none;
    padding-left: env(safe-area-inset-left);
}
:global(button), :global(a) { touch-action: manipulation; }
```

The wrapper is stripped at compile time and the rule emits exactly the inner selector. `:global()` also works inside
`@media` / `@supports` / `@container` / `@layer` blocks.

### Scoped JS

Drop a sibling `{Component}.js` file next to `{Component}.cs` to colocate behavior with markup. The file exports any
number of named functions; the framework bundles them, ships the bundle (server: `/_rask/scoped.js?v={hash}`; WASM:
inline), and wraps each file as `window.Rask["{TypeName}"] = (function () { /* exports */ return { … }; })();` — so
each `export function rendered(...) { ... }` becomes `window.Rask.{TypeName}.rendered`.

Dispatch goes through the standard `Microsoft.JSInterop.IJSRuntime`. Inject it via constructor and call
`InvokeVoidAsync("Rask.{TypeName}.{method}", args...)` from a lifecycle hook (typically `OnRenderedAsync`). The
framework does not pass an `el` argument — user JS queries the DOM itself, via a marker class or attribute the
component renders.

```js
// CodeSample.js — sibling of CodeSample.cs
export function rendered(firstRender) {
    const codes = document.querySelectorAll('.sample-card code[class*="language-"]');
    codes.forEach(code => {
        delete code.dataset.highlighted;
        window.hljs.highlightElement(code);
    });
}
```

```csharp
public sealed class CodeSample(IJSRuntime js) : Component
{
    // The framework does NOT auto-fire scoped-JS hooks. OnRenderedAsync is the
    // typical hook — it gets a firstRender bool that flows straight through to
    // your `rendered(firstRender)` function.
    protected override async Task OnRenderedAsync(bool firstRender) =>
        await js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender);

    protected override RenderResult Render() =>
        Div(Class: "sample-card")[ /* the marker class user JS will query */ ];
}
```

You can call from any lifecycle hook (`OnMount*`, `OnRendered*`, event handlers, …) and from anywhere else where
`IJSRuntime` is in scope. Orphan `.js` files (no matching `.cs` in the same folder) raise `RASK017`; ambiguous matches
raise `RASK018`. Opt out per-project with `<RaskScopedJsAutoInclude>false</RaskScopedJsAutoInclude>`.

#### Async JS interop

For round-trips where C# needs a value back from JS, use `IJSRuntime.InvokeAsync<T>`:

```csharp
public sealed class Measure(IJSRuntime js) : Component
{
    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender) return;
        int height = await js.InvokeAsync<int>("Rask.Measure.getScrollHeight");
        // … do something with height
    }
}
```

```js
// Measure.js
export function getScrollHeight() {
    return document.querySelector('.measure-target').scrollHeight;   // sync return is fine
}
// async functions also work — the dispatcher awaits the returned Promise
export async function loadFromCdn(url) {
    const r = await fetch(url);
    return await r.text();
}
```

On **WASM**, the standard Blazor-WASM trimming constraint applies: `T` in `InvokeAsync<T>` must be kept rooted on the
call site (via `[DynamicallyAccessedMembers]` or a `JsonSerializerContext`). JSON primitives (`bool`, `int`, `long`,
`double`, `string`) are always safe. Server has no such constraint.

### Live rendering & the diff codec

A Rask app stays live after the first paint: the server pushes re-renders over a WebSocket, and WASM re-renders in the
browser — the **same component code drives both**, only the transport differs. When state changes (an event handler
mutates a field, an `await` resolves), the runtime renders the tree again and reconciles the DOM in place.

What it sends on the wire is the interesting part. The first render ships the full HTML. After that, a small state
change ships a **minimal edit-op payload** — a handful of text / attribute / subtree operations the client walks
against the live DOM — instead of re-serializing the whole body. A counter tick on a large page goes from the entire
rendered document to a few dozen bytes: an order-of-magnitude saving on every interaction, with no change to how you
write components.

This is on by default. `LiveDiffMode.Auto` (the framework default) uses a choose-smaller heuristic: ship the diff when
it beats the full HTML on bytes, otherwise fall back to full HTML transparently. You don't opt in — but you can tune it:

```csharp
// Server
builder.Services.AddRask(o => o.DiffMode = LiveDiffMode.Auto);   // default; choose-smaller

// WASM
var host = WasmHostBuilder.CreateDefault(o => o.DiffMode = LiveDiffMode.Auto);
```

The other modes are `LiveDiffMode.DisabledFull` (always full HTML — bit-for-bit pre-codec behaviour) and
`LiveDiffMode.Forced` (always diff when one is computable; for tests/benchmarks). The codec is transparent to your
components and is exercised end-to-end on both hosts by the Playwright E2E suite.

### Lifecycle reference

| Hook                                     | When                                                                                        |
|------------------------------------------|---------------------------------------------------------------------------------------------|
| `OnMount` / `OnMountAsync`               | Once, on first instance creation                                                            |
| `OnPropsChanged` / `OnPropsChangedAsync` | Every render after props are applied                                                        |
| `OnRendered` / `OnRenderedAsync`         | After every render, with a `firstRender` flag; skipped on publish-only walks (loop-safe)    |
| `OnUnmount` / `OnUnmountAsync`           | Once, on disposal (children before parents); the lifetime `CancellationToken` is still live |
| `StateHasChanged()`                      | Call to force a re-render outside an event handler                                          |

The post-`await` auto re-render is a *publish-only* walk that does not re-fire `OnRendered`/`OnRenderedAsync` on
already-rendered components, so an async hook that awaits a next-frame side effect won't loop (see **Async data**).

## Performance

Headline numbers from `Rask.Benchmarks.VsBlazor` — Rask vs Blazor's
`HtmlRenderer` (Scope 1, render hot path), `RenderTreeBuilder` parameter
churn (Scope 2, live-diff payload). Measured 2026-05-27 on **Apple M4 Pro
(14 logical cores, .NET 10.0.5)** with BenchmarkDotNet ShortRun (3 warmup
+ 3 iteration + 1 launch, `[MemoryDiagnoser]`). Lower-is-better for both
columns; bold marks the winner.

| Scenario                                          | Rask time      | Blazor time    | Rask alloc    | Blazor alloc  |
|---------------------------------------------------|----------------|----------------|---------------|---------------|
| Counter render (1 button, 1 span)                 | **246 ns**     | 1 030 ns       | **1.59 KB**   | 3.37 KB       |
| AttributeHeavy 100 elements × 20 attrs            | **88 µs**      | 147 µs         | **273 KB**    | 436 KB        |
| AttributeHeavy 100 elements × 50 attrs            | **256 µs**     | 314 µs         | **842 KB**    | 1 028 KB      |
| LiveDiff: counter on 200-row page                 | **49 µs**      | 136 µs         | **87 KB**     | 229 KB        |
| LiveDiff: attribute update on 100 × 20 page       | **131 µs**     | 242 µs         | **343 KB**    | 589 KB        |
| LiveDiff: input-typing burst (3-field form)       | **1.2 µs**     | 2.9 µs         | **5.81 KB**   | 5.84 KB       |
| LiveDiff: multi-attribute (5 attrs on root)       | **1.1 µs**     | 3.1 µs         | **4.47 KB**   | 6.63 KB       |
| Realistic: dashboard counter tick                 | **8.9 µs**     | 23.7 µs        | **27 KB**     | 49 KB         |
| Realistic: navigation tab switch                  | **15.9 µs**    | 34.1 µs        | **25.7 KB**   | 56.7 KB       |
| Virtualize 1000 items vs render-all (Rask wins)   | **1.9 µs**     | 163 µs         | **11.2 KB**   | 609 KB        |
| Scale: keyed-list reorder (1 000 rows)            | **269 µs** (0.93×) | 290 µs     | 658 KB        | **464 KB**    |
| Scale: keyed-list reorder (5 000 rows)            | 1 625 µs (1.08×) | **1 505 µs** | 3 391 KB      | **3 266 KB**  |
| Scale: random permutation 1 000 keyed rows        | 477 µs (1.39×) | **343 µs**     | 632 KB        | **457 KB**    |

**Where Rask wins:** small-tree renders, attribute-heavy markup, live diffs
that touch only a few nodes on a large page, virtualised lists, and the
"realistic" patterns (dashboard tick, nav switch). The
[diff codec](#live-rendering--the-diff-codec) is the main lever — a counter
tick on a 200-row page ships ~57 bytes over the wire vs ~50 KB pre-codec
(see [`Rask.Benchmarks.VsBlazor/Reports/Justifications.md`](Rask.Benchmarks.VsBlazor/Reports/Justifications.md)
for the full residual-loss breakdown).

**Where Rask trails:** sustained 10 000-iteration churn workloads, and
keyed-list reorders on the allocation axis (1.04–1.42× — time is now at or
below Blazor parity). Both come from the same root cause — Rask elements are
heap `Component` instances vs Blazor's struct render-tree frames — and are
documented as accepted trade-offs in the Justifications doc above. (The
keyed-move path was also corrected in the process: the prior implementation
emitted wrong DOM order for permutations needing 3+ moves — see
[`Justifications.md`](Rask.Benchmarks.VsBlazor/Reports/Justifications.md) §2.)

**Reproduce:**

```bash
# Scope 1 — render hot path (24 benchmarks, ~12 min):
dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- --filter '*RenderHotPath_*' --job short

# Scope 2 — live-diff payload (32 benchmarks, ~15 min):
dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- --filter '*LiveDiffPayload_*' --job short

# Scope 3 — scale sweeps:
dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- --filter '*Scale_*' --job short

# Scope 6/7 — realistic patterns + sustained-load:
dotnet run -c Release --project Rask.Benchmarks.VsBlazor -- --filter '*Realistic_*' '*MemoryGc_*' --job short
```

Results are written to `BenchmarkDotNet.Artifacts/results/`.

## Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts,
`net10.0-browser` for WASM projects). Production use at your own discretion — issues and PRs welcome.

- **Test coverage:** unit suites across `Rask.Core.Tests`, `Rask.Generators.Tests`, host-specific test projects, and
  the validation packages, plus a Playwright E2E smoke suite (`Rask.Examples.E2E.Tests`) that runs against both
  example hosts.
- **Benchmarks:** baselines for the render hot path live in `Rask.Benchmarks` (BenchmarkDotNet); committed reports
  under `BenchmarkDotNet.Artifacts/results/`. `Rask.Benchmarks.VsBlazor` adds a head-to-head suite measuring Rask
  against Blazor on shared scenarios — render throughput and the wire-efficiency the [diff codec](#live-rendering--the-diff-codec)
  buys on live updates.
- **Trimming:** `Rask.Example.Wasm` publishes with zero IL warnings. See `CLAUDE.md` for the contract that keeps it
  that way.

## License

Rask is released under the [MIT License](LICENSE).
