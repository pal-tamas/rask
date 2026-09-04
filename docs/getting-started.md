# Getting started with Rask

Rask is **the .NET One Person Framework** — one developer builds, runs, and ships a whole product solo, in
C#, on one server ([read the doctrine](one-person-framework.md)). It starts with the UI: you build it as
plain C# classes — no `.razor`, no JSX, no JavaScript to write. A component is a class that returns a tree
of HTML from `Render()`, and the *same* component runs either server-rendered (live updates over a
WebSocket) or fully client-side in the browser on WebAssembly.

This is a zero-to-running guide for someone new to Rask. By the end you'll have an app on screen,
you'll understand the files the template gave you, and you'll have written your own component, an event
handler that updates the UI, and a route. It assumes you're comfortable with C# — we explain the
Rask-specific ideas, not the language.

> **Coming from Blazor?** Skim [migrating from Blazor](migration-from-blazor.md) for the concept
> mapping (`@page` → `[Route]`, `[Parameter]` → a property, `EventCallback` → a plain delegate). **Just
> want to look first?** Click through the [live demo](https://rask.sh/docs/) — a full
> multi-page Rask app, no install needed.

## Before you start

Rask requires the **.NET 10 SDK**. Confirm you have it:

```bash
dotnet --version      # must be ≥ 10.0
```

If that prints an older version (or errors), install the .NET 10 SDK from
[dotnet.microsoft.com](https://dotnet.microsoft.com/download) first.

> **WASM only:** the `wasm` template — and `--wasm` on a server app — also need the browser
> WebAssembly tooling — install it once with `dotnet workload install wasm-tools`. If you're starting
> with the server template (recommended below), you can skip this.

## 1. Scaffold a project

Scaffolding is done by the [`rask` CLI](cli.md) (`Rask.Cli`, a global .NET tool). **Not sure which host
to pick? Choose the default `server` template** — it's a single ASP.NET project that runs with no extra
setup, and the components you write are identical across hosts, so nothing you learn here is wasted if
you switch later.

```bash
curl -sSL https://rask.sh/rask.sh | sh   # one-time: the rask CLI + what it needs

rask new MyApp                       # create a server app in ./MyApp (server is the default)
```

The installer adds the .NET 10 SDK if you don't have one, plus `dotnet-ef`, the `wasm-tools`
workload and Node — all under `$HOME`, no `sudo`. With the SDK already in place,
`dotnet tool install -g Rask.Cli` is enough. See [Installing Rask](installation.md).

`rask new` ships three templates:

| `--template`        | What you get                                                                                  |
|---------------------|-----------------------------------------------------------------------------------------------|
| `server` (default)  | One ASP.NET project. Components render on the server; live updates ship over a WebSocket. **Best default.** |
| `wasm`              | One `net10.0-browser` project that publishes to a static `wwwroot/` you can host anywhere (GitHub Pages, S3, nginx). Bring your own API. |

They emit the same starter pages, so the rest of this guide applies whichever you chose.

**Each arrives with every battery it can carry.** On `server` that is a SQLite database, the
[Rask.Cqrs](cqrs.md) mediator, background jobs, transactional email, a cache, a transactional outbox,
scheduled backups, a durable log store, the [operator dashboard](dashboard.md), an installable
[PWA](pwa.md) with Web Push, a Dockerfile for [`rask deploy`](cli.md), and the localization machinery —
wiring, not sample pages. `wasm` takes the PWA and the Dockerfile; the rest need a host to put a
database in. Languages are configured in `Program.cs` rather than on the command line — a server app
starts with English registered there, and adding another is a line in the same block. A browser-WASM
app registers none, because a language there means shipping ICU: roughly a megabyte of extra download
that an app formatting nothing culture-sensitive should not pay by default. See
[localization](localization.md).

Almost nothing is left to you. Sign-in comes with the app — register, sign in and sign out work out of
the box, and the first account you create is the administrator (see [authentication](authentication.md)).
Styling is not a flag either: every project is Tailwind. To leave a battery out, name it:
`rask new MyApp --no-push --no-ops`. The full flag list is in [the CLI reference](cli.md).

## 2. Run it

```bash
cd MyApp
dotnet run            # server / wasm
```

Open the URL printed in the console. **You should see** a single **"Hello, Rask! 👋"** welcome card that
lists the `rask` commands you'll use next. The starter app is deliberately minimal — a clean shell with one
page — so there's nothing to delete before you start building.

> **Edit-and-refresh with hot reload.** Run `rask dev` instead of `dotnet run` for a live inner loop: edit
> a component's `Render()` (or its scoped `.css`/`.ts`), a `[Route]` template, or a CQRS handler and save —
> **C# Hot Reload** applies the change to the running app and Rask re-renders the open session in place, no
> manual rebuild or browser refresh. A small "Hot reload applied" pill confirms it landed. Edits the
> runtime can't apply (adding a type, changing a signature) restart the app instead, and the page reloads
> itself. The full list is in [what hot-reloads](cli.md#what-hot-reloads).

> **First build is slower, and the IDE may look broken — that's expected.** The first build is when
> Rask's source generators run. Until then your IDE may flag `HomePage()`, `Counter()`, or
> `NavLink(...)` as undefined — they're *generated* methods that don't exist until you build. Build
> once, then reload the solution so IntelliSense picks them up. (More on this in
> [Troubleshooting](#troubleshooting) below.)

## 3. Tour of what the template generated

Before writing code, here's what's in the project and why. The `server` template is small on purpose (the
WASM templates differ mainly in `Program.cs`):

- **`Program.cs`** — the host setup, and mostly notable for what is *not* in it:

  ```csharp
  var app = RaskApp.Create(args);
  app.Run<App>();
  ```

  `RaskApp.Create` builds the host and turns on **every battery** — the database, mediator, background
  jobs, transactional email, cache, outbox, operator dashboard, durable logs, Web Push, snapshots and
  continuous backup. `app.Run<App>()` mounts your root component as the whole site and applies the
  middleware order: forwarded headers, the health endpoint, HSTS and HTTPS redirection, static assets,
  authentication, your own endpoints, then Rask's catch-all.

  Your own services go here too, on `app.Services`. What an app writes in this file is the *exceptions* —
  the batteries it does without, and anything configured differently:

  ```csharp
  app.Configure(c =>
  {
      c.Jobs.Off();                                            // this app has no background work
      c.Mail.Configure(o => o.From = "no-reply@example.com");
  });
  ```

  Nothing has to be turned off in order to configure it: you can also call a battery's own `AddRaskX`
  directly and yours wins. And to map your own endpoints, use `app.MapEndpoints(e => …)` — a named place
  for them rather than an ordering rule, since routing matches on precedence and any route you write is
  more specific than Rask's catch-all.

- **`App.cs`** — two things live here. First, the **root component** `App`: it renders straight into
  `<body>` — Rask builds the document around it — and drops a `Router()` where the current page appears.
  `<head>` is framework-managed — app-wide tags (title, charset, viewport) go through its `Head`
  override, not by passing children to `Head()` (more in [section 7](#7-the-document-and-the-head-override)).
  Second, the **`HomePage`** component — the `/` route, a small welcome card. Edit or replace it; it's your
  starting point.

- **`{Project}.csproj`** and **`Properties/launchSettings.json`** — the project file (framework package
  references, source generators) and the local run profile (URLs, environment).

That's the whole starter app — no example `Counter` or `Weather` pages to clean up. You'll add your own
screens next; a **scoped `.css`** or **`.ts`** file is as easy as dropping `{Component}.css` next to a
`{Component}.cs` (same folder, same base name) — its selectors apply only to that component, no leaks.

## 4. Your first component

Every component is a `sealed partial class : Component`. Override `Render()` and return a tree of HTML
written as a **chain**: name a component and dot onto it — `Div.Class("greeting")`. The name *is* the
component, so pressing `.` lists everything it has, each step carrying its own doc comment. A tag you set
nothing on needs no parentheses at all (`H1["Hi"]`). Children attach through an **indexer** on every
component — `Div[ ... ]` — and strings, other components, and value types all convert to a child node
automatically:

```csharp
public sealed partial class Greeting : Component
{
    protected override Component? Render() =>
        Div.Class("greeting")[
            H1["Hello, world!"],
            P["Welcome to your new Rask app — ", Strong["it's all C#"], "."],
            Span[42]                          // value types convert too — no .ToString()
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
public sealed partial class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
        [
            H1["Counter"],
            P[$"Current count: {_count}"],
            Button.OnClick(() => _count++)["Click me"]
        ];
}
```

### Going further: child → parent communication

A child declares a plain delegate property (`Action<int>?`, `Func<Task>?`, …), and the chain step that
sets it wraps it so invoking it re-renders the **parent** that owns the lambda. There is no
`EventCallback` type, and the child stays oblivious to the parent:

```csharp
public sealed partial class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }            // a plain delegate prop

    protected override Component? Render() =>
        Div[
            Enumerable.Range(1, 5).Select(i => (Component)Button.OnClick(() => OnRate?.Invoke(i))// child invokes; parent re-renders
.Key(i)[i <= Value ? "★" : "☆"])
        ];
}

public sealed partial class RatingDemo : Component
{
    private int _rating;

    protected override Component? Render() =>
        [
            RatingStars.Value(_rating).OnRate(n => _rating = n),   // lambda captures this
            P[_rating == 0 ? "Click a star." : $"You rated: {_rating}/5"]
        ];
}
```

## 6. Why `HomePage` already chains (the generated surface)

You never write a builder by hand. For each concrete `Component`, the generator emits a **chain entry**
and a step per public settable property — that's why `HomePage`, `Counter`, and your own `Greeting` can
be named and dotted onto. Which shape a property takes is derived from its declaration:

| Property shape                                    | In the chain                                    |
|---------------------------------------------------|-------------------------------------------------|
| Non-nullable, **no initializer**                  | a **step** — required before the component exists |
| Nullable (`T?` / `Nullable<T>`), no initializer   | an optional setter                              |
| Has an initializer (`= ...`)                      | an optional setter — your default wins          |
| `[SkipFactory]` (property or class)               | **excluded** — no step, no setter               |
| `Children`                                        | always excluded (children attach via the indexer) |

```csharp
public sealed partial class Card : Component
{
    public required string Title { get; set; }     // a step: Card.Title("…") opens the chain
    public string? Subtitle { get; set; }          // an optional setter
    public int Elevation { get; set; } = 1;        // an optional setter — your default wins
    [SkipFactory] public int Internal { get; set; }// excluded explicitly
    // → Card.Title("Pricing").Subtitle("per seat").Elevation(2)
}
```

The steps come first and in any order; miss one and there is no component to render, so the mistake is a
compile error where you made it rather than a null at runtime. The class must be `partial` — that is
where the generator puts the surface.

Live — a `Greeting` with a required `Name` and an optional `Title`, built with
`Greeting.Name("Ada")…`:

<!-- demo:components-greeting -->

**Inject framework services (`HttpClient`, `Navigator`, `RouteState`, `IJSRuntime`) through the
constructor, not as properties** — a non-nullable settable property would become a *required step*
(and `required` on a property with a DI-only constructor is the **RASK002** warning). Inject
through the primary constructor instead:

```csharp
public sealed partial class Weather(IWeatherForecastService service) : Component { ... }
```

<!-- demo:components-di -->

`[SkipFactory]` keeps a property settable in code but off the chain — useful for seeding
cached internal state the caller shouldn't pass. The counter below starts at 7 (its `Initial` is
`[SkipFactory]`, seeded in `OnMount`) and keeps its state across re-renders like any private field:

<!-- demo:components-skipfactory -->

## 7. The document and the `Head` override

Your root component (the `TApp` you pass to the host — `App` in the template) renders straight into
`<body>`. Rask composes the document around it: the doctype, `<html>`, a `<head>` filled from every
mounted component's `Head` override (plus the scoped CSS and JS the page needs), and a `<body>` holding
what the root rendered and the auto-appended runtime `<script>`. So a root is just its head
contributions and a `Router()`:

```csharp
public sealed partial class App : Component
{
    // App-level head; pages can override their own Head to set a per-page Title.
    protected override Component? Head => [
        Title["My Rask App"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1")
    ];

    protected override Component? Render() => Router;
}
```

The two attributes an app usually wants on the shell are overrides of their own, read off the root:
`HtmlLang` — the `lang` on `<html>`, `"en"` by default, `null` to omit it — and `BodyClass`, the
`class` on `<body>`, `null` by default:

```csharp
protected override string? HtmlLang => "fr";
protected override string? BodyClass => "bg-body-tertiary";
```

Anything those two can't express — another attribute on `<html>`, an element wrapped around the app —
is a `Shell` override. It receives the framework's `<head>` and the app's rendered body as
**parameters**, so place both: drop `head` and the page loses every head asset.

```csharp
protected override Component Shell(Component head, Component body) =>
    Html("en", Dir: "rtl")[head, Body.Class("dark")[body]];
```

The doctype is still emitted ahead of whatever `Shell` returns, and the runtime `<script>` still lands
in `<body>` — neither is yours to add. `Shell` is evaluated once per render, *before* your `Render()`
runs, so it can't observe state that render produces; keep anything reactive in the body or in `Head`.

Any component can contribute to `<head>` while it's in the tree by overriding `Head`. `<title>` and
`<base>` are singleton tags — the last contributor wins, so a page's `Title` overrides the app fallback:

```csharp
protected override Component? Head => Title["Welcome — My Rask App"];
```

> **Guardrails:** two compile-time checks catch the common mistakes (full list in
> [diagnostics](diagnostics.md)) — **RASK021** if the root renders the shell itself, and **RASK019** if
> you pass children to `Head()` instead of using the override.

> **Already have an app?** Delete the shell from your root's `Render()` and return what was inside
> `<body>` (usually just `Router()`). Its pieces move to the overrides that own them: the `lang` on
> `Html(...)` becomes `HtmlLang`, the `Class` on `Body(...)` becomes `BodyClass`, the `Head()` slot just
> goes away (your head contributions were already in the `Head` override), and anything left over
> becomes a `Shell` override. `Doctype`, `Html`, `Head`, and `Body` are still ordinary tag components —
> they're what you build a document out of by hand (`ToHtml()`, an email body), just not the app's page.

## 8. Add a route

Put `[Route("/path")]` on a component to register it as a page (`Rask.Core.Routing` is the one namespace
you bring in explicitly). `[RouteParam]` and `[QueryParam]` bind URL pieces to properties, and every
route gets a generated, type-safe URL builder:

```csharp
using Rask.Core.Routing;

[Route("/users/{id}")]
public sealed partial class UserPage : Component
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }

    protected override Component? Render() =>
        Span[$"User #{Id} — {Tab ?? "overview"}"];
}

// elsewhere — type-safe, refactor-proof:
NavLink.Href(UserPage(id: 42))["View user"];
```

The `Router()` in your root component matches the current path and renders the page. To navigate from
an event handler, inject the `Navigator` service through the constructor and call
`nav.NavigateTo(HomePage())`, `nav.SetQuery("tab", "settings")`, and so on. For nested layouts
(`[ParentRoute]` + `Outlet()`), 404 pages (`[NotFound]`), and the full routing model, see
[routing](routing.md).

## Troubleshooting

The snags you're most likely to hit on a fresh project:

- **The IDE flags `HomePage()`, `Counter()`, or `NavLink(...)` as undefined.** These are
  *source-generated* — the chain surface for every component, the URL builder for every `[Route]`. They don't
  exist until the generator runs, which happens on build. Run `dotnet build` once, then reload the
  solution / restart the language server.

- **`net10.0` / `net10.0-browser` won't restore, or a WASM publish fails.** You're missing the .NET 10
  SDK (`dotnet --version` must be ≥ `10.0`) or, for WASM, the workload — install it with
  `dotnet workload install wasm-tools`.

- **A scoped `.css` / `.ts` file isn't taking effect.** The sibling file must sit in the **same folder**
  as its component and share the **base name** (`Card.cs` ↔ `Card.css`). A mismatch is a build error
  (`RASK015`–`RASK018`) — check the build output.

- **Blank page or 404s on `/_rask/...` assets behind a reverse proxy or sub-path.** The app is running
  under a URL prefix the framework doesn't know about — set `PathBase` ([configuration](configuration.md)),
  and build any hand-written asset URL as `LiveOptions.PathBase + "/…"` ([JS interop](js-interop.md)).
  For a WASM bundle published under a prefix (GitHub Pages project sites), publish with
  `-p:RaskPathBase=/my-repo` — see [Deploying to a sub-path](pwa.md#deploying-github-pages--sub-paths).

## Next steps

You now have a running, routed, interactive app. From here, the One Person Framework path takes it to a
shipped product — and the **[zero-to-deploy tutorial](tutorial/00-overview.md)** walks that whole path
step by step (database, auth, jobs, email, cache, events, and deployment). In short:

1. **Build a feature** → [tutorial chapter 2](tutorial/02-first-feature.md) writes a full CQRS + EF Core CRUD vertical
   slice (entity, value objects, validation, list/create/edit pages — and, with `--tests`, a test project)
   in one command, wiring the DI into `Program.cs` for you.
2. **Make SQLite production-ready** → [Why one server, no PaaS](sqlite.md) — WAL, busy-timeout, and
   continuous backup so one SQLite file is your production database.
3. **Ship to one server** → a `--docker` template emits a production Dockerfile; deploy the whole app to
   one box.

Read **[the doctrine](one-person-framework.md)** for the why. Reference guides for the next thing you need:

- **Build a form** → [forms](forms.md) — `Form<T>`, `Input(() => model.X)`, validation.
- **Add more routes / layouts** → [routing](routing.md) — nested layouts, route/query params, `Navigator`.
- **Load or save data** → [data access](data-access.md) — EF Core + SQLite in a Server app.
- **Run code on mount / after render** → [lifecycle](lifecycle.md) — `OnMount*` / `OnRendered*`, async hooks.
- **Share state without prop-drilling** → [composition](composition.md) — context, callbacks, `VirtualizeModel`.
- **Add a login** → [authentication](authentication.md) — cookie/JWT/OIDC on Server and WASM.
- **Test your components** → [testing](testing.md) — unit-testing components and rendered HTML.
- **Write idiomatic Rask** → [best practices](best-practices.md) — patterns and pitfalls that keep an app correct, secure, and fast.
- **Decode a build error** → [diagnostics](diagnostics.md) — every RASK0xx analyzer ID and its fix.

**Keep handy while you build:** the [cheat sheet](cheatsheet.md) (every command + wiring line on one
page) and the [recipes](recipes.md) (task-first "how do I do X?").
