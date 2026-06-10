# Getting started with Rask

This is a zero-to-running tutorial. By the end you'll have a Rask app on screen, a
component of your own, an event handler that updates the UI, and a route.

Rask requires the **.NET 10 SDK**. Check with `dotnet --version` (≥ `10.0`). WASM
projects also need the WebAssembly tooling — install it once with
`dotnet workload install wasm-tools`.

## 1. Install the templates and scaffold

`Rask.Templates` ships three `dotnet new` templates, one per host model:

```bash
dotnet new install Rask.Templates

dotnet new rask-server       -o MyApp    # ASP.NET live-server app (SSR over WebSockets)
dotnet new rask-wasm         -o MyApp    # standalone browser-WASM SPA (static bundle)
dotnet new rask-wasm-hosted  -o MyApp    # browser-WASM client + ASP.NET host project
```

Which one to pick:

- **`rask-server`** — a single ASP.NET project. Components render on the server and
  live updates ship over a WebSocket. Good default for apps that already have a
  backend or want server-side secrets.
- **`rask-wasm`** — a single `net10.0-browser` project that publishes to a static
  `wwwroot/` you can host anywhere (GitHub Pages, S3, nginx). No server process for
  the framework itself; bring your own API for whatever the client calls.
- **`rask-wasm-hosted`** — two projects: the WASM client plus an ASP.NET host that
  serves the published bundle and your own `/api/...` endpoints, pre-wired with the
  cross-TFM `ProjectReference`.

All three accept a `--auth` switch that scaffolds a working login flow
(`rask-server` / `rask-wasm-hosted` scaffold a **cookie** login; `rask-wasm`, having
no host, scaffolds a **JWT** bearer login against an external API). See
[authentication](authentication.md) for the full picture.

Each template emits a runnable solution with `App`, `HomePage`, `Counter`, and a
`Weather` page that demonstrates async data through constructor DI.

## 2. Run it

```bash
cd MyApp
dotnet run            # rask-server / rask-wasm
# rask-wasm-hosted: run the host project
dotnet run --project MyApp.Host
```

Open the URL printed in the console.

> The first build is also when the source generators run. The IDE may flag
> `HomePage()`, `Counter()`, or `NavLink(...)` as undefined until then — they're
> **generated** factories. Build once, then reload the solution so IntelliSense
> picks them up.

## 3. Your first component

Every component is a `sealed class : Component`. Override `Render()` and return a
tree of HTML built from generated factories. Children attach through an **indexer**
on every component — `Div()[ ... ]` — and strings, `Component`s, and value types
all convert implicitly to a child node:

```csharp
public sealed class Greeting : Component
{
    protected override RenderResult Render() =>
        Div(Class: "greeting")[
            H1()["Hello, world!"],
            P()["Welcome to your new Rask app — ", Strong()["it's all C#"], "."],
            Span()[42]                          // value types convert too — no .ToString()
        ];
}
```

`Render()` returns `RenderResult`, which accepts three shapes:

- a single component — `Render() => Div()[...]`;
- a **collection expression** for several top-level nodes with no wrapper —
  `Render() => [H1()["Title"], P()["Body"]]` (sugar for `Fragment()[...]`);
- `default` — render nothing.

### Text vs. Raw

A plain string becomes a `Text` node, which **HTML-encodes** its content (safe by
default). When you genuinely need to emit verbatim markup, use `Raw`:

```csharp
P()["<b>encoded</b> — shows the angle brackets as text"],   // string → Text, encoded
Div()[Raw("<b>bold</b>")]                                    // emitted verbatim
```

## 4. Add interactivity

Keep local state in fields and wire event handlers as plain delegates. A click does
a server round-trip (server host) or a local re-render (WASM host) — the same code.
After the handler runs, **the component that owns the handler re-renders
automatically**; you never call `StateHasChanged()` by hand for a local update.

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

Child → parent communication uses the same idea: a child declares a plain delegate
property (`Action<int>?`, `Func<Task>?`, …), and the generated factory wraps it so
invoking it re-renders the **parent** that owns the lambda. There is no
`EventCallback` type. The child stays oblivious to the parent:

```csharp
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }            // a plain delegate prop

    protected override RenderResult Render() =>
        Div()[
            Enumerable.Range(1, 5).Select(i => (Child)Button(
                OnClick: () => OnRate?.Invoke(i),        // child invokes; parent re-renders
                Key: i)[i <= Value ? "★" : "☆"])
        ];
}

public sealed class RatingDemo : Component
{
    private int _rating;

    protected override RenderResult Render() =>
        [
            RatingStars(Value: _rating, OnRate: n => _rating = n),   // lambda captures this
            P()[_rating == 0 ? "Click a star." : $"You rated: {_rating}/5"]
        ];
}
```

## 5. Factory generation rules

You never write a factory by hand. For each concrete `Component`, the generator
emits a `{Type}(...)` factory and derives its parameters from your public settable
properties:

| Property shape | In the factory |
|----------------|----------------|
| Non-nullable, **no initializer** | **required** parameter |
| Nullable (`T?` / `Nullable<T>`), no initializer | optional, defaults to `null` |
| Has an initializer (`= ...`) | **excluded** — your default wins |
| `[SkipFactory]` (property or class) | **excluded** |
| `Children` | always excluded (children attach via the indexer) |

```csharp
public sealed class Card : Component
{
    public required string Title { get; set; }   // required factory param
    public string? Subtitle { get; set; }         // optional, default null
    public int Elevation { get; set; } = 1;        // excluded — your default wins
    [SkipFactory] public int Internal { get; set; }// excluded explicitly
    // → generated: Card(string Title, string? Subtitle = null, ...)
}
```

**Inject framework services (`HttpClient`, `Navigator`, `RouteState`, `IJSRuntime`)
through the constructor, not as properties** — a non-nullable settable property
would become a required factory parameter:

```csharp
public sealed class Weather(IWeatherForecastService service) : Component { ... }
```

## 6. The page-root shell and the `Head` override

Your root component (the `TApp` you pass to the host) must render the **full HTML
shell** — `Doctype`, `Html`, `Head`, `Body`. Both `<head>` and `<body>` are
framework-managed: the runtime `<script>` is auto-appended to `<body>`, and `<head>`
is filled from every mounted component's `Head` override.

```csharp
public sealed class App : Component
{
    // App-level head; pages can override their own Head to set a per-page Title.
    protected override RenderResult Head => [
        Title()["My Rask App"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1")
    ];

    protected override RenderResult Render() =>
        [
            Doctype(),
            Html("en")[
                Head(),               // framework-managed slot — do NOT pass children
                Body()[
                    Router()
                ]
            ]
        ];
}
```

Any component can contribute to `<head>` while it's in the tree by overriding
`protected virtual RenderResult Head`. `<title>` and `<base>` are singleton tags —
the last contributor wins, so a page's `Title` overrides the App fallback:

```csharp
protected override RenderResult Head => Title()["Welcome — My Rask App"];
```

Two compile-time guardrails to know about (full list in [diagnostics](diagnostics.md)):

- **RASK021** — the root doesn't render a complete shell (`Doctype`/`Html`/`Head`/`Body`).
- **RASK019** — you passed children to `Head()`. Use the `Head` override instead.

## 7. Add a route

Put `[Route("/path")]` on a component to register it as a page (`Rask.Core.Routing`
is the one namespace you bring in explicitly). `[RouteParam]` and `[QueryParam]` bind
URL pieces to properties, and every route gets a generated, type-safe URL builder:

```csharp
using Rask.Core.Routing;

[Route("/users/{id}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }

    protected override RenderResult Render() =>
        Span()[$"User #{Id} — {Tab ?? "overview"}"];
}

// elsewhere — type-safe, refactor-proof:
NavLink(UserPage(id: 42))["View user"];
```

The `Router()` in your shell matches the current path and renders the page. To
navigate from an event handler, inject the `Navigator` service through the
constructor and call `nav.Navigate(HomePage())`, `nav.SetQuery("tab", "settings")`,
and so on. For nested layouts (`[ParentRoute]` + `Outlet()`), 404 pages
(`[NotFound]`), and the full routing model, see [routing](routing.md).

## Next steps

- [routing](routing.md) — nested layouts, route/query params, `Navigator`.
- [forms](forms.md) — `Form<T>`, `Input(Bind: ...)`, validation.
- [lifecycle](lifecycle.md) — `OnMount*` / `OnPropsChanged*` / `OnRendered*`, async hooks.
- [testing](testing.md) — unit-testing components and rendered HTML.
- [diagnostics](diagnostics.md) — every RASK0xx analyzer ID and how to fix it.
- [authentication](authentication.md) — cookie/JWT/OIDC on Server and WASM.
