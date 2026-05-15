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
- **Forms with async validation.** `Form<TModel>(model, OnValidSubmit: …)` routes submit through validators you opt
  into by dropping `DataAnnotationsValidator()` or `FluentValidationValidator(...)` inside the form as children.
  Implement `IAsyncFieldValidator` for ad-hoc server-side rules — the submit bridge awaits async checks before routing,
  and rapid keystrokes cancel any prior in-flight validation (latest-wins).

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

`Rask.Server` and `Rask.Wasm` each bundle the core component types and source generators. `Rask.Wasm.Hosting` depends on
`Rask.Wasm` and pulls those in transitively. The validation packages depend only on `Rask.Core` and add a global
`using static` for their factory namespace, so `DataAnnotationsValidator()` / `FluentValidationValidator(...)` are in
scope without any extra `using` lines.

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

    public override Component Render() =>
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

public override Component Render() =>
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

### Files: upload and download

`Input(Type: "file", OnFiles: …)` accepts files; `Navigator.Download(...)` sends them. The same component code runs
unchanged on Server and WASM — only the transport differs (multipart over the WebSocket on the server, JS-Map +
chunked reads on WASM; downloads go through `/_rask/download/{token}` on the server, base64 + Blob URL on WASM).

```csharp
Input(Type: "file", OnFiles: async files => {
    var file = files[0];                                         // RaskFile
    using var s = file.OpenReadStream(maxAllowedSize: 5_000_000, // valid only inside this handler
                                      cancellationToken: CancellationToken);
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

    public override Component Render() =>
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

| Hook                                     | When                                                                                |
|------------------------------------------|-------------------------------------------------------------------------------------|
| `OnMount` / `OnMountAsync`               | Once, on first instance creation                                                    |
| `OnPropsChanged` / `OnPropsChangedAsync` | Every render after props are applied                                                |
| `OnRendered` / `OnRenderedAsync`         | After every render, with a `firstRender` flag                                       |
| `OnUnmount` / `OnUnmountAsync`           | Once, on disposal (children before parents); the lifetime `CancellationToken` is still live |
| `StateHasChanged()`                      | Call to force a re-render outside an event handler                                  |

## Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts,
`net10.0-browser` for WASM projects). Production use at your own discretion — issues and PRs welcome.

## License

Rask is released under the [MIT License](LICENSE).
