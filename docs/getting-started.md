# Getting started with Rask

Rask lets you build web UIs as plain C# classes — no `.razor`, no JSX, no JavaScript to write. A
component is a class that returns a tree of HTML from `Render()`, and the *same* component runs either
server-rendered (live updates over a WebSocket) or fully client-side in the browser on WebAssembly.

This is a zero-to-running tutorial for someone new to Rask. By the end you'll have an app on screen,
you'll understand the files the template gave you, and you'll have written your own component, an event
handler that updates the UI, and a route. It assumes you're comfortable with C# — we explain the
Rask-specific ideas, not the language.

> **Coming from Blazor?** Skim [migrating from Blazor](migration-from-blazor.md) for the concept
> mapping (`@page` → `[Route]`, `[Parameter]` → a property, `EventCallback` → a plain delegate). **Just
> want to look first?** Click through the [live demo](https://pal-tamas.github.io/rask/) — a full
> multi-page Rask app, no install needed.

## Before you start

Rask requires the **.NET 10 SDK**. Confirm you have it:

```bash
dotnet --version      # must be ≥ 10.0
```

If that prints an older version (or errors), install the .NET 10 SDK from
[dotnet.microsoft.com](https://dotnet.microsoft.com/download) first.

> **WASM only:** the two WebAssembly templates (`rask-wasm`, `rask-wasm-hosted`) also need the browser
> WebAssembly tooling — install it once with `dotnet workload install wasm-tools`. If you're starting
> with the server template (recommended below), you can skip this.

## 1. Scaffold a project

**Not sure which host to pick? Choose `rask-server`** — it's a single ASP.NET project that runs with no
extra setup, and the components you write are identical across hosts, so nothing you learn here is
wasted if you switch later.

```bash
dotnet new install Rask.Templates    # one-time: install the templates

dotnet new rask-server -o MyApp      # create a server app in ./MyApp
```

`Rask.Templates` ships three templates, one per host model:

| Template            | What you get                                                                                  |
|---------------------|-----------------------------------------------------------------------------------------------|
| `rask-server`       | One ASP.NET project. Components render on the server; live updates ship over a WebSocket. **Best default.** |
| `rask-wasm`         | One `net10.0-browser` project that publishes to a static `wwwroot/` you can host anywhere (GitHub Pages, S3, nginx). Bring your own API. |
| `rask-wasm-hosted`  | Two projects: the WASM client plus an ASP.NET host that serves the bundle and your own `/api/...` endpoints. |

All three emit the same starter pages, so the rest of this guide applies whichever you chose. Each also
accepts a `--auth` switch that scaffolds a working login flow — see [authentication](authentication.md)
when you need it.

## 2. Run it

```bash
cd MyApp
dotnet run            # rask-server / rask-wasm
# rask-wasm-hosted: run the host project — dotnet run --project MyApp.Host
```

Open the URL printed in the console. **You should see** a small nav bar (`Home | Counter | Weather`)
above three working pages: a welcome card, a counter you can click, and a weather table that shows a
*"Loading…"* placeholder for a moment before async data fills in.

> **Edit-and-refresh with `dotnet watch`.** Run `dotnet watch` instead of `dotnet run` for a live
> inner loop: edit a component's `Render()` (or its scoped `.css`/`.js`) and save, and **C# Hot Reload**
> applies the change to the running app and Rask re-renders the open session in place — no manual rebuild
> or browser refresh. It's the closest a compiled framework gets to Rails' no-build loop.

> **First build is slower, and the IDE may look broken — that's expected.** The first build is when
> Rask's source generators run. Until then your IDE may flag `HomePage()`, `Counter()`, or
> `NavLink(...)` as undefined — they're *generated* methods that don't exist until you build. Build
> once, then reload the solution so IntelliSense picks them up. (More on this in
> [Troubleshooting](#troubleshooting) below.)

## 3. Tour of what the template generated

Before writing code, here's what's in the project and why. This is the `rask-server` layout (the WASM
templates differ only in `Program.cs`):

- **`Program.cs`** — the host setup. `builder.Services.AddRask()` registers the framework, and
  `app.UseRask<App>()` mounts your root component (`App`) as the whole site. Your own services go in
  here too — the template registers `IWeatherForecastService` so the Weather page can inject it.

- **`App.cs`** — the **root component**, and the one place that renders the full HTML page shell
  (`Doctype` / `Html` / `Head` / `Body`). It holds the nav bar and drops a `Router()` where the current
  page should appear. `<head>` is framework-managed — app-wide tags (title, charset, viewport) go
  through its `Head` override, not by passing children to `Head()`. More on this in
  [section 7](#7-the-page-shell-and-the-head-override).

- **`HomePage.cs`** + **`HomePage.css`** — the `/` route. The sibling `.css` file is **auto-scoped** to
  this component: drop a `{Component}.css` next to a `{Component}.cs` and its selectors only apply to
  that component — no class-name discipline, no leaks. (Same idea for a sibling `{Component}.js`.)

- **`Counter.cs`** — the `/counter` route: local state in a field, a click handler, automatic re-render.
  You'll write something like it in [section 5](#5-add-interactivity).

- **`Weather.cs`** (+ `WeatherForecast.cs`, `LocalWeatherForecastService.cs`) — the `/weather` route.
  Shows the canonical async-data pattern: a service injected through the **constructor**, loaded in an
  `OnMountAsync` lifecycle hook, with a loading placeholder until the data arrives.

Open these side by side as you read the next sections — each one is a live example of a concept below.

## 4. Your first component

Every component is a `sealed class : Component`. Override `Render()` and return a tree of HTML built
from generated factory methods (`Div()`, `H1()`, `P()`, …). Children attach through an **indexer** on
every component — `Div()[ ... ]` — and strings, other components, and value types all convert to a
child node automatically:

```csharp
public sealed class Greeting : Component
{
    protected override Component? Render() =>
        Div(Class: "greeting")[
            H1()["Hello, world!"],
            P()["Welcome to your new Rask app — ", Strong()["it's all C#"], "."],
            Span()[42]                          // value types convert too — no .ToString()
        ];
}
```

`Render()` returns `Component?`, which accepts three shapes — you'll mostly use the first two:

- a single node — `Render() => Div()[...]`;
- a **collection expression** for several top-level nodes with no wrapper — `Render() => [H1()["Title"],
  P()["Body"]]`;
- `null` — render nothing.

> **Safe by default — good to know, not needed yet.** Two security defaults are worth knowing about but
> won't get in your way:
> - **Strings are HTML-encoded.** A plain string becomes a `Text` node, so `P()["<b>hi</b>"]` shows the
>   angle brackets as text. When you genuinely need verbatim markup, use `Raw("<b>hi</b>")`.
> - **URL attributes are scheme-sanitized.** `href`/`src`/etc. neutralize dangerous schemes
>   (`javascript:` → `about:blank`) so a user-supplied URL can't run script on click. For a URL you
>   fully control, opt out per-call with `RaskUrl.Trusted(...)`.
>
> See [best practices](best-practices.md) for the full security picture.

## 5. Add interactivity

Keep local state in fields and wire event handlers as plain delegates. After the handler runs, **the
component that owns it re-renders automatically** — you never call `StateHasChanged()` by hand for a
local update. A click does a server round-trip (server host) or a local re-render (WASM host); the same
code works for both.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
        [
            H1()["Counter"],
            P()[$"Current count: {_count}"],
            Button(OnClick: () => _count++)["Click me"]
        ];
}
```

### Going further: child → parent communication

A child declares a plain delegate property (`Action<int>?`, `Func<Task>?`, …), and the generated factory
wraps it so invoking it re-renders the **parent** that owns the lambda. There is no `EventCallback`
type, and the child stays oblivious to the parent:

```csharp
public sealed class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }            // a plain delegate prop

    protected override Component? Render() =>
        Div()[
            Enumerable.Range(1, 5).Select(i => (Component)Button(
                OnClick: () => OnRate?.Invoke(i),        // child invokes; parent re-renders
                Key: i)[i <= Value ? "★" : "☆"])
        ];
}

public sealed class RatingDemo : Component
{
    private int _rating;

    protected override Component? Render() =>
        [
            RatingStars(Value: _rating, OnRate: n => _rating = n),   // lambda captures this
            P()[_rating == 0 ? "Click a star." : $"You rated: {_rating}/5"]
        ];
}
```

## 6. Why `HomePage()` already exists (factory generation)

You never write a factory method by hand. For each concrete `Component`, the generator emits a
`{Type}(...)` factory — that's why `HomePage()`, `Counter()`, and your own `Greeting()` are callable.
Its parameters are derived from your public settable properties:

| Property shape                                    | In the factory                                  |
|---------------------------------------------------|-------------------------------------------------|
| Non-nullable, **no initializer**                  | **required** parameter                          |
| Nullable (`T?` / `Nullable<T>`), no initializer   | optional, defaults to `null`                    |
| Has an initializer (`= ...`)                      | **excluded** — your default wins                |
| `[SkipFactory]` (property or class)               | **excluded**                                    |
| `Children`                                        | always excluded (children attach via the indexer) |

```csharp
public sealed class Card : Component
{
    public required string Title { get; set; }     // required factory param
    public string? Subtitle { get; set; }          // optional, default null
    public int Elevation { get; set; } = 1;        // excluded — your default wins
    [SkipFactory] public int Internal { get; set; }// excluded explicitly
    // → generated: Card(string Title, string? Subtitle = null, ...)
}
```

Live — a `Greeting` with a required `Name` and an optional `Title`, called through its generated
`Greeting(Name: "Ada", …)` factory:

<!-- demo:components-greeting -->

**Inject framework services (`HttpClient`, `Navigator`, `RouteState`, `IJSRuntime`) through the
constructor, not as properties** — a non-nullable settable property would become a *required* factory
parameter (and `required` on a property with a DI-only constructor is the **RASK002** warning). The
template's `Weather` page is the model to copy:

```csharp
public sealed class Weather(IWeatherForecastService service) : Component { ... }
```

<!-- demo:components-di -->

`[SkipFactory]` keeps a property settable in code but out of the factory signature — useful for seeding
cached internal state the caller shouldn't pass. The counter below starts at 7 (its `Initial` is
`[SkipFactory]`, seeded in `OnMount`) and keeps its state across re-renders like any private field:

<!-- demo:components-skipfactory -->

## 7. The page shell and the `Head` override

Your root component (the `TApp` you pass to the host — `App` in the template) must render the **full
HTML shell**: `Doctype`, `Html`, `Head`, `Body`. Both `<head>` and `<body>` are framework-managed — the
runtime `<script>` is auto-appended to `<body>`, and `<head>` is filled from every mounted component's
`Head` override.

```csharp
public sealed class App : Component
{
    // App-level head; pages can override their own Head to set a per-page Title.
    protected override Component? Head => [
        Title()["My Rask App"],
        Meta("utf-8"),
        Meta(Name: "viewport", Content: "width=device-width, initial-scale=1")
    ];

    protected override Component? Render() =>
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

Any component can contribute to `<head>` while it's in the tree by overriding `Head`. `<title>` and
`<base>` are singleton tags — the last contributor wins, so a page's `Title` overrides the app fallback:

```csharp
protected override Component? Head => Title()["Welcome — My Rask App"];
```

> **Guardrails:** two compile-time checks catch the common mistakes (full list in
> [diagnostics](diagnostics.md)) — **RASK021** if the root doesn't render a complete shell, and
> **RASK019** if you pass children to `Head()` instead of using the override.

## 8. Add a route

Put `[Route("/path")]` on a component to register it as a page (`Rask.Core.Routing` is the one namespace
you bring in explicitly). `[RouteParam]` and `[QueryParam]` bind URL pieces to properties, and every
route gets a generated, type-safe URL builder:

```csharp
using Rask.Core.Routing;

[Route("/users/{id}")]
public sealed class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }

    protected override Component? Render() =>
        Span()[$"User #{Id} — {Tab ?? "overview"}"];
}

// elsewhere — type-safe, refactor-proof:
NavLink(UserPage(id: 42))["View user"];
```

The `Router()` in your shell matches the current path and renders the page. To navigate from an event
handler, inject the `Navigator` service through the constructor and call `nav.NavigateTo(HomePage())`,
`nav.SetQuery("tab", "settings")`, and so on. For nested layouts (`[ParentRoute]` + `Outlet()`), 404
pages (`[NotFound]`), and the full routing model, see [routing](routing.md).

## Troubleshooting

The snags you're most likely to hit on a fresh project (the README has a
[fuller list](../README.md#-troubleshooting)):

- **The IDE flags `HomePage()`, `Counter()`, or `NavLink(...)` as undefined.** These are
  *source-generated* — the factory for every component, the URL builder for every `[Route]`. They don't
  exist until the generator runs, which happens on build. Run `dotnet build` once, then reload the
  solution / restart the language server.

- **`net10.0` / `net10.0-browser` won't restore, or a WASM publish fails.** You're missing the .NET 10
  SDK (`dotnet --version` must be ≥ `10.0`) or, for WASM, the workload — install it with
  `dotnet workload install wasm-tools`.

- **A scoped `.css` / `.js` file isn't taking effect.** The sibling file must sit in the **same folder**
  as its component and share the **base name** (`Card.cs` ↔ `Card.css`). A mismatch is a build error
  (`RASK015`–`RASK018`) — check the build output.

- **Blank page or 404s on `/_rask/...` assets behind a reverse proxy or sub-path.** The app is running
  under a URL prefix the framework doesn't know about — set `PathBase` (see the README's
  *Sub-path hosting* section).

## Next steps

You now have a running, routed, interactive app. Where to go for the next thing you need:

- **Build a form** → [forms](forms.md) — `Form<T>`, `Input(Bind: ...)`, validation.
- **Add more routes / layouts** → [routing](routing.md) — nested layouts, route/query params, `Navigator`.
- **Load or save data** → [data access](data-access.md) — EF Core + SQLite in a Server app.
- **Run code on mount / after render** → [lifecycle](lifecycle.md) — `OnMount*` / `OnRendered*`, async hooks.
- **Share state without prop-drilling** → [composition](composition.md) — context, callbacks, `VirtualizeModel`.
- **Add a login** → [authentication](authentication.md) — cookie/JWT/OIDC on Server and WASM.
- **Test your components** → [testing](testing.md) — unit-testing components and rendered HTML.
- **Write idiomatic Rask** → [best practices](best-practices.md) — patterns and pitfalls that keep an app correct, secure, and fast.
- **Decode a build error** → [diagnostics](diagnostics.md) — every RASK0xx analyzer ID and its fix.
