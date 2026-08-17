# Chapter 1 — Scaffold the app

> **Goal:** create the Shop project, run it, and understand what the template gave you.
> **You'll run:** `rask new Shop --all-batteries --auth --docker`

## Create the project

The `rask` CLI scaffolds projects. We'll use the default **server** template (one ASP.NET project,
components render on the server, live updates ship over a WebSocket):

```bash
rask new Shop --all-batteries --auth --docker
cd Shop
```

These are all **scaffold-time** choices — they wire into `Program.cs` and the `DbContext` as the project is
created, so you pick them up front rather than bolting them on later:

- **`--all-batteries`** turns on every One Person Framework pillar at once: a SQLite database and the CQRS
  mediator, plus background jobs, transactional email, a cache, a durable outbox, scheduled snapshots,
  continuous backup, and Web Push. Each chapter from here on teaches you what one of them is *for*; this
  flag means none of them needs a wiring detour first.
- **`--auth`** adds a working cookie-authentication flow — a `/login` page, a sign-out action, a protected
  members area, and the services in `Program.cs` to back them. ([authentication](../authentication.md).)
- **`--docker`** drops a production `Dockerfile` (and `.dockerignore`) into the project. We won't touch it
  until [Chapter 11](11-deploy.md), where `rask deploy` uses it to ship the app to a server.

> **Prefer one at a time?** Every battery is its own flag — `rask new Shop --data --jobs` gives you a
> database and background work and nothing else. Each implies what it needs (`--jobs` implies `--data`
> implies `--cqrs`), so you never have to remember a dependency. See [the CLI guide](../cli.md).

Open `Program.cs` and skim it. It's long — a dozen commented registrations — but the comments explain
*why* each one sits where it does, and a few of those orderings are load-bearing rather than stylistic. We
come back to the sharpest one in [Chapter 7](07-outbox-events.md).

> **Other hosts.** `--template wasm` and `--template wasm-hosted` build the same components as a
> browser-WebAssembly SPA instead. Everything in this tutorial works on all three; we use `server` because
> it runs with no extra tooling. See [the CLI guide](../cli.md) for the full template matrix.

## Run it

```bash
rask dev
```

`rask dev` runs the app under `dotnet watch`, so **C# Hot Reload** is on: edit a component and save, and the
change applies to the running app and re-renders the open page — no manual rebuild, no browser refresh.

Open the URL printed in the console. You'll see a single **"Hello, Rask! 👋"** welcome card — a
[Rask.Bootstrap](../bootstrap.md) `BsCard` that lists the CLI commands you'll use next. That's the whole
starter app: no example pages to delete, just a clean shell to build on. Because you passed `--auth`, you
also have a working **`/login`** page and a protected **`/members`** page.

> **The first build is slower and your IDE may look broken — that's expected.** The first build is when
> Rask's source generators run; until then the IDE may flag generated methods (`HomePage()`, `Login()`, …)
> as undefined. Build once, reload the solution, and IntelliSense catches up. More in
> [Getting started → Troubleshooting](../getting-started.md#troubleshooting).

## What the template generated

The `server` template is deliberately small — a handful of files, no example pages to clean up:

- **`Program.cs`** — host setup. `builder.Services.AddRask()` registers the framework and
  `app.UseRask<App>()` mounts your root component. This is where every pillar you add in later chapters gets
  one line of registration. `--auth` already added the cookie-authentication services here.
- **`Features/Shared/App.cs`** — the **root component**: it renders into `<body>` (Rask builds the
  document around it, filling `<head>` from every component's `Head` override) and drops a `Router()`
  where the current page appears. It lives in `Features/Shared/` — the bucket for cross-cutting code the
  whole app shares.
- **`Features/Home/HomePage.cs`** — the `/` welcome page, its own feature slice. Edit or replace it.
- **`Features/Auth/`** — from `--auth`: `LoginPage` (`/login`), the protected `MembersPage` (`/members`),
  and a `DemoCredentialStore` you'll swap for a real user store later.

Everything the CLI generates lands under `Features/`: a screen is its own `Features/<Name>/` slice, and
cross-cutting code (the app root, components, jobs, emails, the `DbContext`) sits in `Features/Shared/`. You'll
add your first `Features/<Name>/` slice in the next chapter.

For the component model itself — state, event handlers, the chain, routing — see
[Getting started](../getting-started.md). This tutorial focuses on everything *behind* the UI.

## Verify

- `rask dev` prints a URL and the app loads with the "Hello, Rask! 👋" welcome card.
- Browsing to `/login` shows a login form and `/members` redirects there when signed out (proof `--auth`
  wired in).
- Editing `HomePage` in `Features/Home/HomePage.cs` and saving updates the page without a manual refresh.

**Learn more:** [the `rask` CLI](../cli.md) · [authentication](../authentication.md)

Next → **[Chapter 2: Your first feature](02-first-feature.md)**
