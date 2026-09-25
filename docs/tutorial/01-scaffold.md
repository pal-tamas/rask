# Chapter 1 — Scaffold the app

> **Goal:** create the Shop project, run it, and understand what the template gave you.
> **You'll run:** `rask new Shop`

## Create the project

The `rask` CLI scaffolds projects. We'll use the default **server** template (one ASP.NET project,
components render on the server, live updates ship over a WebSocket):

```bash
rask new Shop
cd Shop
```

**The batteries come as standard.** That one command wires every One Person Framework pillar into the
project: a SQLite database, background jobs, transactional email, a cache, a durable outbox, scheduled
snapshots, continuous backup, a durable log store, the operator dashboard, an installable PWA with Web
Push, and a production `Dockerfile`. It also creates and applies the database's **first migration**, so
every battery's tables exist before you run anything. Each chapter from here on teaches you what one of
them is *for*; none of them needs a wiring detour first.

Two things you might expect to choose are not choices:

- **Accounts come with the app.** Register, sign in and sign out work already, `/login`, `/register` and
  `/logout` are routed, and the first account you create is the administrator.
  ([authentication](../authentication.md).)
- The pages are styled with **Tailwind**, which every project gets — the compiler ships inside the host
  package, so there is no flag, no package to add, and nothing to turn on or off.

> **Want less?** Every battery has a `--no-` — `rask new Shop --no-push --no-ops` leaves those two out.
> It doesn't drop a package: it writes the off-switch into `Program.cs` (`app.Configure(c => c.Ops.Off())`),
> so turning one back on later is deleting a line. Turning one off takes its dependents with it
> (`--no-data` also drops jobs, mail, cache, outbox, snapshots and the dashboard). See [the CLI guide](../cli.md).

Open `Program.cs`. Past a `using` and a comment, it is one line:

```csharp
using Shop.Features.Shared;

RaskApp.Create(args).Run<App>();
```

That line is the whole host: every battery, the middleware in the order that works, health checks, the
database, sign-in, the dashboard. Nothing you add in this tutorial goes here — each chapter's pillar is
already on, and its settings live in `appsettings.json` under `"Rask"`.

> **Other hosts.** `--template wasm` builds the same components as a browser-WebAssembly SPA instead,
> and `rask new Shop --template wasm-hosted` keeps the server for the API and data while the pages run in WebAssembly from
> a `Client/` folder in this one project. This tutorial uses `server`, whose pages the server renders
> live, because it runs with no extra tooling. See [the CLI guide](../cli.md) for the full template matrix.

## Run it

```bash
rask dev
```

`rask dev` runs the app under `dotnet watch`, so **C# Hot Reload** is on: edit a component and save, and the
change applies to the running app and re-renders the open page — no manual rebuild, no browser refresh.

Open the URL printed in the console. You'll see a single **"Hello, Rask! 👋"** welcome card — a
card that lists the CLI commands you'll use next. That's the whole
starter app: no example pages to delete, just a clean shell to build on. Because every app has accounts, you
also have a working **`/login`** page and a protected **`/members`** page.

> **The first build is slower and your IDE may look broken — that's expected.** The first build is when
> Rask's source generators run; until then the IDE may flag generated methods (`HomePage()`, `Login()`, …)
> as undefined. Build once, reload the solution, and IntelliSense catches up. More in
> [Getting started → Troubleshooting](../getting-started.md#troubleshooting).

## What the template generated

The `server` template is deliberately small — a handful of files, no example pages to clean up:

- **`Program.cs`** — `RaskApp.Create(args).Run<App>()`, which builds the host with every battery on and
  mounts your root component. None of the batteries needs code: each reads its own section of
  `appsettings.json` (`Rask:Mail`, `Rask:Jobs`, …), so when a later chapter tunes a pillar, that section is
  what it edits. The database is SQLite at `Rask:ConnectionStrings:App` — `Data Source=app.db` while you
  develop.
- **No database context.** Rask's own, `RaskAppDbContext`, maps every aggregate you declare plus every
  battery's tables — those are what the first migration created — so there is no `DbSet` property or
  configuration class to add as you go. Code that needs the context itself injects
  `IDbContextFactory<RaskAppDbContext>`; you'll meet that in [Chapter 4](04-background-jobs.md).
- **`Features/Shared/App.cs`** — the **root component**: it renders into `<body>` (Rask builds the
  document around it, filling `<head>` from every component's `Head` override) and drops a `Router()`
  where the current page appears. It lives in `Features/Shared/` — the bucket for cross-cutting code the
  whole app shares.
- **`Features/Home/HomePage.cs`** — the `/` welcome page, its own feature slice. Edit or replace it.
- **The [Rask.Ui](../ui-kit.md) kit** — it comes with `Rask.Server`, is imported everywhere by the
  `global using Rask;` line in `GlobalUsings.cs`, and its stylesheet is linked first in `App.cs`. Every page in this
  tutorial is built from its components — `Ui.Input`, `Ui.Button`, `Ui.Card`, `Ui.DataGrid` — rather than from
  raw tags and class strings.
- **`Features/Auth/`** — the sign-in, registration, sign-out, confirmation, password-reset and devices pages,
  as your own code: the flows come from `Rask.Auth`, the pages are yours to restyle.

Everything the CLI generates lands under `Features/`: a screen is its own `Features/<Name>/` slice, and
cross-cutting code (the app root, the `User` account, components, jobs, emails) sits in `Features/Shared/`.
You'll add your first `Features/<Name>/` slice in the next chapter.

For the component model itself — state, event handlers, the chain, routing — see
[Getting started](../getting-started.md). This tutorial focuses on everything *behind* the UI.

## Verify

- `rask dev` prints a URL and the app loads with the "Hello, Rask! 👋" welcome card.
- A `Migrations/` folder exists and `app.db` sits next to the project — the first migration already ran.
- Browsing to `/login` shows a sign-in form and `/register` offers to claim the app (proof the accounts battery
  wired in).
- Editing `HomePage` in `Features/Home/HomePage.cs` and saving updates the page without a manual refresh.

**Learn more:** [the `rask` CLI](../cli.md) · [authentication](../authentication.md)

Next → **[Chapter 2: Your first feature](02-first-feature.md)**
