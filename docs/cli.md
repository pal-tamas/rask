# The `rask` CLI

`Rask.Cli` is a [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools) that gives
Rask a short, task-focused command line on top of the .NET SDK. It generates, or shells out to `dotnet`,
for almost everything it does — its one package reference is SQLite, which `rask db backup` needs to take
a consistent copy of a live database — and it never gets in the way of the tools you already use.

> **In a hurry?** The [cheat sheet](cheatsheet.md) lists every command on one page, and the
> [recipes](recipes.md) answer "how do I do X?" with the command and the wiring line.

## Install

```bash
dotnet tool install -g Rask.Cli
```

That puts a `rask` command on your `PATH`. Update it later with `dotnet tool update -g Rask.Cli`.

> `rask` is a thin, Rask-aware layer over the .NET SDK: it owns scaffolding end to end (`rask new`,
> and shells out to `dotnet` for the rest — `rask dev` wraps `dotnet watch`, `rask db`
> wraps `dotnet ef`.

## Getting help

`rask <command> --help` (or `-h`) prints that command's full reference — its arguments, an aligned
table of every option with a one-line description, and copy-pasteable examples:

```bash
rask                       # on a terminal: the new-project wizard. Piped: the command list.
rask --help                # the command list, always
rask new --help            # arguments, options, and examples for `new`
rask deploy --help
```

Help (and other output) is colorized when `rask` is writing to a terminal, and falls back to plain
text when the output is piped or when the [`NO_COLOR`](https://no-color.org) environment variable is
set — so `rask info | cat` and CI logs stay clean. Long descriptions wrap to the terminal's width;
piped output is never reflowed, so a line you grep for stays on one line.

## Short names mean one thing

A short flag means the same option on every command that has it, so muscle memory carries between
them:

| short | option |
| --- | --- |
| `-h` | `--help` (reserved CLI-wide; no command may claim it) |
| `-p` | `--project` |
| `-o` | `--output` |
| `-n` | `--name` |
| `-t` | `--template` |
| `-f` | `--follow` |
| `-y` | `--yes` |

A few options have no short name on purpose, because the letter belongs to something else: `rask dev
--open` (`-o` is `--output`).

`--force` means *overwrite files* (`rask new`). Skipping a destructive confirmation is
`--yes` (`rask db drop`, `rask db restore`) — a different word, because it is a different power.

A test enforces all of this, so a new option cannot quietly reuse a letter.

## `--dry-run` and `--json`

**`--dry-run` lists what would happen and changes nothing**, in the same shape everywhere: one
`[dry-run] would …` line per action. It is on `new`, `dev`, `db` and `deploy`.

```bash
rask db drop --dry-run        # the exact `dotnet ef` command, without the database going anywhere
rask dev --dry-run            # the `dotnet watch` command line and the environment it sets
rask new Shop --dry-run                                        # the files it would write
```

A dry run never prompts — it does nothing, so there is nothing to consent to.

**`--json` prints a document and nothing else**, so it pipes into `jq` without filtering banners out:

```bash
rask info --json
rask deploy status --json
rask db list --json
```

Errors still go to stderr and the exit code still distinguishes `2` (you typed something wrong) from
`1` (what you asked for failed), so a script never has to parse prose to find out what happened. Fields
that have no value are **absent** rather than carrying a human placeholder — `rask info --json` on a
machine with no SDK simply has no `dotnetSdk` key, where the human report prints `not found`.

## `rask new` — scaffold a project

```bash
rask                                 # the wizard, from a blank slate
rask new                             # the same wizard
rask new MyApp                       # a server-rendered app (the default template)
rask new MyApp --auth --docker       # + cookie auth + a production Dockerfile
rask new Blog --data --docker        # + a SQLite database, ready for your first feature
rask new Spa --template wasm --pwa   # an installable browser-WASM PWA
rask new Shop --template wasm-hosted # a WASM SPA with an ASP.NET host
rask new Shop --template react       # a React client on an ASP.NET host (needs Node.js)
rask new Field --template native     # a native iOS + Android app
rask new Kiosk --template native --platform android   # Android only
```

Run `rask` (or `rask new`) with no project name and — on a terminal — it walks you through a short
wizard: the project name, an arrow-key **project type** picker, **styling** (Rask.Bootstrap or plain
elements), whether to add a **Dockerfile**, and a checklist of **batteries** you toggle with space. A
database picker follows if anything you ticked needs one. It then scaffolds exactly as if you'd passed
the flags.

Picking `native` asks its own two questions instead: where the UI comes from, and which platforms to
target. Everything else on that path is skipped, because the native template has no component library,
no Dockerfile and no batteries — and the summary lists only what was actually decided, rather than
reporting defaults for questions it never asked.

The wizard **fills gaps rather than re-asking**: anything already on the command line is kept and its
question skipped, so `rask new --template wasm --auth` asks only for the name. Piped or in a script (no
terminal), a missing name is a plain error instead, and bare `rask` prints the command list — so
automation stays predictable.

Every project also gets a `.gitignore`, an `.editorconfig`, and a `.slnx` solution, and is initialized
as a git repository with one commit — `--no-git` skips that, and it is skipped automatically inside an
existing repository. `--no-bootstrap` swaps the `Bs*` components for plain elements against a small
stylesheet in the app shell, and drops the `Rask.Bootstrap` reference.

The CLI writes the project's files itself, pins the `Rask.*` package references, and runs `dotnet
restore` so the output builds immediately. `wasm-hosted` emits a three-project solution — `MyApp.Client`
(the browser-WASM SPA), `MyApp.Server` (the ASP.NET host you run and deploy), and `MyApp.Shared` (a class
library both reference).

`react` is the one template that does **not** write its own client. It runs the framework's own
scaffolder — `npx create-vite@latest MyApp.Client --template react-ts` — and overlays four files onto
what that produces, so the skeleton is whatever Vite ships today rather than a copy Rask maintains. It
therefore needs **Node.js and a network** at `rask new` time, and it emits two projects rather than
three: the client's half of every contract is generated TypeScript, so there is nothing for a `.Shared`
to hold. `react-ts`, never `react`: Rask supports **TypeScript** SPA clients, and a client with no
TypeScript configuration is refused at build time with `RASKSPA004`. See
[TypeScript front ends](spa.md).

A new project is deliberately **minimal** — nothing to delete before you start — and everything it
scaffolds already follows the vertical-slice layout the guides build on: feature code under
`Features/<Name>/`, cross-cutting code under `Features/Shared/`.

```
MyApp/
  MyApp.csproj
  Program.cs
  appsettings.json                logging levels (incl. Rask's own diagnostic categories)
  appsettings.Production.json     overrides applied when deployed
  Features/
    Shared/App.cs                 the root component every page renders through
    Home/HomePage.cs              a [Route("/")] welcome page that teaches the CLI
  Properties/launchSettings.json
```

The shell lives in `Features/Shared/`; the welcome page is its own `Features/Home/` slice, styled with
Bootstrap. `--auth` adds a `Features/Auth/` slice and `--data` an `AppDbContext` under `Features/Shared/`.
Add pages and components to taste — the [tutorial](tutorial/00-overview.md) shows the shapes.

| Option | Meaning |
|--------|---------|
| `<name>` (or `--name`) | The project name. Required. |
| `--template`, `-t` | `server` (default), `wasm`, `wasm-hosted`, `native`, or `react`. |
| `--auth` | Scaffold a cookie login/session (web templates). |
| `--pwa` | Web app manifest + service worker + icon, and the wiring to serve them (web templates). |
| `--cqrs` | Wire up `Rask.Cqrs` — `AddRaskCqrs()` + the package reference (the `server` template only). |
| `--data` | Pre-wire a SQLite database: an empty `AppDbContext`, `AddRaskData()`, and a `UseRaskSqlite` (WAL + `busy_timeout`) DbContext factory — so your first entity attaches to it and is immediately runnable with `rask db add`/`update`. It also wires **continuous backup** ([Litestream](sqlite.md#continuous-backup-with-litestream)) — inert until you set `Litestream:ReplicaUrl`, so turning it on is one env var at deploy time (`rask deploy --env "Litestream__ReplicaUrl=s3://bucket/app"`), and `--docker` puts the replicator binary in the image. Implies `--cqrs` (the `server` template only). |
| `--jobs` | Durable background jobs on the app's own database: `AddRaskJobs<AppDbContext>()` + `modelBuilder.AddRaskJobs()`. Implies `--data`. |
| `--mail` | Transactional email on the app's own database, delivered off the request thread; the dev default writes `.eml` files to `./mail-pickup` instead of needing SMTP. Implies `--data`. |
| `--cache` | A database-backed cache — the standard `IDistributedCache` plus a typed `ICache`. Implies `--data`. |
| `--outbox` | A transactional outbox for durable domain-event delivery. Also turns **off** the in-process publisher, so events aren't delivered twice. Implies `--data`. |
| `--push` | Server-sent Web Push (VAPID) with `/_push/key`, `/_push/subscribe`, `/_push/unsubscribe` and a subscription store. Implies `--pwa`. |
| `--snapshots` | Scheduled point-in-time SQLite backups via the Online Backup API — a second line of defence alongside the continuous backup `--data` already wires. Implies `--data`. |
| `--logs` | A [durable log store](logging.md) in a SQLite file of its own, so the application log survives a restart — buffered off the request thread, with retention by age and row count. The **only** battery that does *not* imply `--data`: it takes a connection string rather than a `DbContext`, so it needs no migration and works on an app with no database. |
| `--ops` | An [operator dashboard](dashboard.md) at `/_rask` over every battery's table — queue depth, dead letters and the error behind each, the log, the live SQLite pragmas. With `--auth` it also emits the authorization policy that gates it; without, that line is scaffolded commented out and the dashboard denies everyone outside Development. Implies `--data`. |
| `--all-batteries` | Every battery above — the full One Person Framework stack in one app. |
| `--docker` | Emit a production `Dockerfile` + `.dockerignore` (web templates). |
| `--platform` | The `native` template only, repeatable: `ios`, `android`, or both (the default when omitted). Only the platforms you name get a target framework, a manifest and a head — an unselected platform leaves no files behind. |
| `--host` | The `native` template only: **where the UI comes from**. `native` (default) runs your components on the device, so the app works offline and an edit costs a restart. `server` and `wasm-hosted` scaffold **both halves as one solution** — the Rask app *and* a `{name}.Mobile` project carrying the heads that point at it — so the UI ships when you deploy rather than when the store approves. The two heads are the same (the WebView takes a trusted origin and doesn't care what serves it); what differs is what survives losing the network, because a live server renders over a WebSocket while a published WASM bundle keeps working once loaded. |
| `--output`, `-o` | Target directory (defaults to a folder named after the project). |
| `--dry-run` | Print the files that would be created and write nothing (skips `dotnet restore`). |
| `--force` | Scaffold into a directory that already contains files, overwriting on collision. Without it, any existing file the template would overwrite stops the command. |
| `--no-restore` | Skip `dotnet restore` (for offline use). Without it, a restore failure is reported as a failure — the files are written, but the project won't build until it succeeds. |

The flags wire a feature up; they don't scaffold sample pages for you to delete.

Every server app also gets `Features/Shared/ErrorPage.cs` and `app.UseExceptionHandler("/error")` outside
Development. `ErrorBoundary` already catches anything thrown *inside* a component tree; this covers
everything outside it, which would otherwise be a bare 500 with an empty body. The page renders through
your app shell and shows a **correlation id and nothing else** — the exception goes to `ILogger`, where you
match it by that id. Locally the handler stays off, because the developer exception page is strictly more
useful than a page designed to reveal nothing.

### Which template supports which flag

| Flag | `server` | `wasm` | `wasm-hosted` | `native` | `react` |
| --- | :-: | :-: | :-: | :-: | :-: |
| `--auth` | ✅ | ✅ | ✅ | — | — |
| `--pwa` | ✅ | ✅ | ✅ | — | — |
| `--docker` | ✅ | ✅ | ✅ | — | ✅ |
| `--cqrs`, `--data` | ✅ | — | ✅ | — | ✅¹ |
| `--jobs`, `--mail`, `--cache`, `--outbox`, `--snapshots`, `--logs`, `--ops` | ✅ | — | ✅ | — | ✅ |
| `--push` | ✅ | — | — | — | — |

¹ `react` always wires CQRS — the typed wire *is* the template — so `--cqrs` is accepted but changes
nothing. `--auth` and `--pwa` are refused rather than half-scaffolded: both need work on the client
(a React login flow, a service worker through `vite-plugin-pwa`) that the template does not write yet.
| `--all-batteries` | ✅ | — | ✅ | — |
| `--host`, `--platform` (see below) | — | — | — | ✅ |

The wizard only offers what the chosen template supports, so an interactive run cannot assemble a
combination that is then rejected. On the command line, asking for one is a usage error that names both
halves rather than silently dropping the flag:

```console
$ rask new X --template wasm --data
Template 'wasm' does not support: --data. Supported flags: --auth, --docker, --pwa.
```

The battery flags need an ASP.NET host to put a database in, which the `server` template is and the
`wasm-hosted` template's `.Server` project is too — a pure browser-WASM SPA has neither. Each implies what
it needs (`--jobs` implies `--data` implies `--cqrs`), so you can ask for the pillar you want without also
remembering its dependencies:

```bash
rask new Shop --all-batteries --auth --docker    # every pillar, wired in the right order
rask new Shop --jobs --mail                      # just background work and email
```

On `wasm-hosted` the batteries land in the `.Server` project and the client keeps calling the host over
its API, exactly as it already does for auth. `--ops` additionally mounts the [operator
dashboard](dashboard.md) there — server-rendered at `/_rask`, with the WASM client still serving every
other route:

```bash
rask new Shop --template wasm-hosted --ops       # SPA in the browser, dashboard on the host
```

`--push` is the one battery `wasm-hosted` does not take. Web Push needs the subscribe endpoints and a
service worker that posts to them, and in this template those live in two different projects — a real
feature rather than a wiring gap, so it is left out rather than half-scaffolded.

The generated `Program.cs` composes them in an order that is load-bearing rather than stylistic — the
outbox registered before the `DbContext` factory (so its interceptor joins the `SaveChanges` pipeline),
`ApplyRaskConventions()` after the entity configurations (it walks the model as it stands), and the
Litestream restore before anything opens the database. Those are pinned by tests, not left to chance.

Requesting a flag a template doesn't support (for example `--cqrs` on `wasm`) fails fast with the list
of flags that template *does* support, rather than passing an unknown option through.

## Writing code — by hand, from the guides

`rask` scaffolds a **project**; it does not scaffold code inside one. There is no `rask generate`.

Pages, components, CRUD slices, background jobs, emails and cached accessors are all ordinary C# you
write yourself, and every one of them is documented as code you can copy:

| What you want | Where the code is |
| --- | --- |
| A routed page, a reusable component | [Composition](composition.md), [Routing](routing.md) |
| A CRUD slice — entity, commands, queries, pages | [Tutorial ch.2](tutorial/02-first-feature.md), [Rask.Data](data.md), [CQRS](cqrs.md) |
| A background job | [Tutorial ch.4](tutorial/04-background-jobs.md), [Jobs](jobs.md) |
| A transactional email | [Tutorial ch.5](tutorial/05-email.md), [Mail](mail.md) |
| A cached read | [Tutorial ch.6](tutorial/06-cache.md), [Cache](cache.md) |
| Domain events through the outbox | [Tutorial ch.7](tutorial/07-outbox-events.md), [Outbox](outbox.md) |

The [tutorial](tutorial/00-overview.md) builds all of it in order, and the finished result is committed
as [`samples/Rask.Example.Shop`](https://github.com/pal-tamas/rask/tree/main/samples/Rask.Example.Shop) —
a working app to read whenever a snippet needs its surroundings.

> **Why no generator?** Scaffolded code is read far more often than it is written, and a generator's
> output has to be understood line by line the first time you meet it anyway. Teaching the same code in
> the guides means there is one version of it — the one you can read, adapt, and keep — instead of a
> generated one plus a document describing it, drifting apart.

### When you get it wrong

A rejected command line always names what was wrong, what is allowed, and what to run next — and,
where there's an obvious candidate, what you probably meant:

```console
$ rask deplyo
Unknown command 'deplyo'. Did you mean 'deploy'?

$ rask new Shop --template srever
Option '--template' does not accept 'srever'. Did you mean 'server'? Choose one of: server, wasm, wasm-hosted, native, react.
Usage: rask new <name> [options]
Run 'rask new --help' for details.

$ rask db
Specify a 'rask db' action: add, remove, list, update, drop, backup, restore.
```

`-h` is `--help` for every command, so no option has `-h` as a short name (`rask deploy --host`
has no short form). Anything after `--` is your app's, so `rask dev -- --help` passes it through.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success. |
| `1` | The command ran and what you asked for failed — a build error, an unreachable host, a refused deploy. |
| `2` | The command line was wrong — an unknown command, option, action, or value; a missing value; options that contradict each other. |
| `130` | Interrupted with Ctrl+C. |

The `1` / `2` split is what lets a script tell a broken invocation from a broken deploy. The line
between them is where the bad input came from: anything decidable from the arguments alone is `2`,
while a value that could have come from `.rask/deploy.json` — or from the state of the disk, the
network, or the host — is `1`.

## `rask dev` — run with hot reload

```bash
rask dev                             # find the project, run it under dotnet watch
rask dev --open                      # …and open a browser once it's listening
rask dev --project src/MyApp/MyApp.csproj
rask dev --urls http://localhost:5005
rask dev -- --my-app-flag            # everything after -- goes to the app
```

`rask dev` runs `dotnet watch run`, so editing a component's `Render()` (or a scoped `.css` / `.js`) and
saving re-renders the open page live — see [what hot-reloads](#what-hot-reloads) below.

It finds the project for you. In a **wasm-hosted** solution it picks the `.Server` host (the client is
built into it); in a **native** app it refuses, because `dotnet watch` cannot drive a simulator or
emulator, and points you at `dotnet build "-t:Build;Run" -f net10.0-android` instead.

In a **react** solution it runs **two** processes: `dotnet watch` for the host, and the bundler's own
dev server for the client. The browser talks to the **bundler**, on `http://localhost:5173`, and the
scaffolded `vite.config.ts` proxies `/_rask` back to the host on `:5000` — so HMR is native and instant,
and the browser only ever sees one origin, which is why there is no CORS to configure. `--open` opens
the bundler's URL rather than the host's, and the dev server is killed with the host so a stale one
cannot be picked up by the next session.

The production bundle is skipped for that session (`-p:RaskSpaBuild=false`): the dev server owns the
client, and paying for a full bundle on every save would make watch unusable. The **generated
contracts are still written**, because a dev server compiling the previous build's contracts is exactly
the failure that pipeline exists to prevent.

It also sets up the environment the loop needs: `ASPNETCORE_ENVIRONMENT=Development` when you have not
set an environment yourself, and `HotReloadAutoRestart` so an edit hot reload *can't* apply restarts the
app instead of stopping at an interactive prompt. Pass `--no-restart` to be asked instead.

| Flag | What it does |
| --- | --- |
| `--project`, `-p` | Project to run. Accepts a `.csproj` or a directory. |
| `--urls` | URLs to listen on (sets `ASPNETCORE_URLS`). |
| `--launch-profile` | launchSettings profile to use. |
| `--open` | Open a browser once the app answers. Skipped if the launch profile already opens one. |
| `--no-open` | Never open a browser. |
| `--no-hot-reload` | Keep watching, but restart on change instead of applying live. |
| `--no-restart` | Ask before restarting on an edit hot reload can't apply. |
| `--once` | Run once without watching (a plain `dotnet run`). |
| `--no-banner` | Suppress the startup banner. |

> **Changed in this release.** `--no-hot-reload` used to mean "a plain `dotnet run`" — it stopped watching
> altogether, and cleared `DOTNET_WATCH`, which is what the framework keys its own dev-time behaviour off.
> It now means what it says: keep watching, restart instead of applying edits live. Use **`--once`** for
> the old behaviour.

### What hot-reloads

C# Hot Reload applies new IL to the running process; Rask then refreshes what the generators registered
at startup and repaints every open session. Some edits the runtime cannot apply at all — those are *rude
edits*, and `rask dev` restarts the app for you and the browser reloads itself.

| Edit | What happens |
| --- | --- |
| A component's `Render()`, or anything it calls | ✅ Applied live; the page repaints in place. |
| A scoped `.css` / `.js` sibling | ✅ Applied live; the bundle URL changes and the `<link>` is swapped. |
| Deleting a scoped `.css` | ✅ The rules disappear from the page. |
| A `[Route]` template | ✅ The route table is rebuilt. |
| A CQRS command/query/notification handler body | ✅ The next dispatch runs the new code. |
| A job or outbox event type's body | ✅ Applied live. |
| **Adding or removing a type** — a new component, page, handler, job | ⚠️ Rude edit → the app restarts, and the browser reloads itself. |
| **Changing a signature** — a new factory parameter, a changed method signature | ⚠️ Rude edit → restart. |
| Renaming a job or outbox event type | ✅ Applied. The old name stops resolving too. |

Two things it does not cover:

- **A native app on a device has no watch channel.** `dotnet watch` cannot drive a simulator or a device,
  and applying new IL to one needs a device-side delta agent that .NET doesn't ship — so `rask dev`
  refuses a native project and points at `dotnet build "-t:Build;Run"` instead. Restart a
  the exception**: that head loads a remote Rask Server, so it is a browser as far as hot reload is
  concerned — point it at your dev machine and `rask dev` on the *server* project repaints the device
  exactly as it does a browser tab.
- **A rude edit is not announced.** `dotnet watch` restarts the process, so nothing in Rask observes the
  edit; what you see is the app coming back and the page reloading.

**WASM is covered** — a wasm-hosted app hot-reloads under `rask dev` like a Server one. To make that
possible the host serves the client's *build* output for the session rather than its published bundle:
the published bundle is trimmed, and trimming disables the runtime's metadata-update support outright,
so no applied edit could ever reach the page. It also drops the nested `dotnet publish` from the inner
loop, which is most of the wait. `--no-hot-reload` and `--once` keep the published bundle.

In Development you get a small "Hot reload applied" pill in the corner when an edit lands, so a save that
changed nothing visible is distinguishable from one that didn't apply. It is never present in production,
and it looks and behaves the same on every transport — Server, WASM and native share one implementation.

### When the build fails

A save that doesn't compile takes the app down — and until now the browser reported that as a *network*
problem: "Reconnecting…", then "Still trying to reconnect…" and a **Retry now** button that could never
succeed. It is a compile problem, and it now says so:

```
┌───────────────────────────────────────────────────────────┐
│ Build failed                              Stack   Dismiss │
├───────────────────────────────────────────────────────────┤
│ 2 build errors                                            │
│ Features/Products/ProductPage.cs(31,13): error CS0103:    │
│ The name 'titel' does not exist in the current context    │
└───────────────────────────────────────────────────────────┘
```

Fix the file and it disappears on its own — the reconnect keeps running underneath the panel, so the app
comes back the moment it compiles. No reload, no clicking anything.

**How it reaches the browser.** Nothing in the app can report this, because the app is what died. So
`rask dev` reads `dotnet watch`'s output as it passes it through to your terminal, and serves what it
learned from a small read-only endpoint on `127.0.0.1` that it owns for as long as the session lasts. Its
URL is stamped onto every page the app serves (`data-rask-dev-status` on `<body>`), which is what lets the
browser still ask after the server that sent it has gone. Development only: production HTML never carries
the attribute, so there is nothing to poll and nothing to leak. If the endpoint can't be bound, `rask dev`
runs exactly as before — the browser just falls back to the reconnect overlay.

### When your code throws

An unhandled exception from an event handler or an async lifecycle hook shows the same style of panel —
**over** the running app, which stays mounted, scrolled where it was, with your form input intact. That is
the state that produced the bug, so it is the state worth keeping. Dismiss the panel and keep clicking; it
counts repeats, so a handler throwing on every click is visible as such.

A fault during *render* still replaces the page, in development as in production: re-rendering the subtree
that just threw would only throw again. In production every fault gets the styled error page and a `500`,
and no stack ever reaches the browser.

> **If nothing ever applies, suspect the path.** `dotnet watch` produces an empty hot-reload delta —
> silently, reporting success at every step — when the project path traverses a symlink. `rask dev`
> resolves the path for you, so this only bites if you drive `dotnet watch` yourself; run it against the
> resolved path (on macOS, `/private/var/…` rather than `/var/…`) and edits apply again.

## `rask db` — migrations, and getting the database in and out

```bash
rask db add InitialCreate            # create a migration for the current model
rask db list                         # list migrations and which are applied
rask db update                       # apply pending migrations to the database
rask db update 20240101_Init         # migrate up/down to a specific migration
rask db remove                       # undo the last (unapplied) migration
rask db drop --yes                 # drop the database (a dev reset)
rask db backup                       # a consistent copy of the local database
rask db backup --remote -o backups/  # ...of the deployed one, pulled down
rask db restore backups/app-20260805-081500.db --remote
```

A friendly wrapper over `dotnet ef` for the everyday migration lifecycle, meant to pair with what
your feature code needs. It finds the project for you (the single `.csproj` at or above the
current directory — override with `--project`), and if the EF Core tools aren't installed it installs
`dotnet-ef` globally the first time you run it.

| Action | Wraps | Notes |
| --- | --- | --- |
| `add <Name>` | `dotnet ef migrations add` | `--output <dir>` sets the migrations folder |
| `remove` | `dotnet ef migrations remove` | undo the last migration |
| `list` | `dotnet ef migrations list` | show migrations and applied state |
| `update [<target>]` | `dotnet ef database update` | apply pending, or migrate to a named point |
| `drop` | `dotnet ef database drop` | drops the database; prompts unless `--yes` |
| `backup` | — | a consistent copy; `--output/-o` a file or directory, `--remote` for the deployed one |
| `restore <file>` | — | replaces the database with a copy; prompts unless `--yes` |

Shared options: `--project/-p` (the project owning the `DbContext`), `--startup-project/-s` (the app
that configures it; defaults to `--project`), and `--context/-c` (when the app has more than one
`DbContext`). Anything after `--` is forwarded to `dotnet ef` verbatim (e.g. `rask db update -- --verbose`).

The EF Core tools need the startup project to reference `Microsoft.EntityFrameworkCore.Design` —
projects scaffolded with `--data` already do, and `rask db` adds it for you (via `dotnet add
package`) if it's missing. `backup` and `restore` need none of that: they copy a database rather than
migrate one, so they never install `dotnet-ef`.

### Backup and restore

```bash
rask db backup                                  # ./<app>-20260805-081500.db
rask db backup --output backups/                # into a directory, same generated name
rask db backup --output nightly.db              # a name you choose
rask db backup --remote                         # the deployed database, pulled down
rask db restore nightly.db                      # replace the local database
rask db restore nightly.db --remote --yes     # ...and the deployed one, unattended
```

**A file copy of a live SQLite database is not a backup.** With WAL on — and every Rask app has it, it is
one of the [production pragmas](sqlite.md) — committed transactions live in the `-wal` sidecar until a
checkpoint, so the `.db` file on its own is torn or stale. Both paths go through SQLite instead: locally
via the Online Backup API, remotely via `VACUUM INTO`. Either way what lands is a single self-contained
file with the WAL already folded in, taken while the app keeps serving.

The remote path needs **nothing installed on the host**. It runs the copy inside a throwaway container
mounted on the app's data volume — the same shape the deploy's readiness probe uses — and brings the
result down over the existing `docker -H ssh://…` connection. The host does need to be able to pull
`alpine`, which it already does for every deploy. Host and app name come from `.rask/deploy.json`, so a
repeat backup is a bare `rask db backup --remote`; override with `--host` and `--app`.

**Restore replaces a database, so it behaves like `rask db drop`**: it asks first, takes `--yes` to skip
the prompt, and refuses outright when there's no terminal to ask on rather than guessing. A remote restore
also **stops the app first and starts it again afterwards** — replacing the file under a live writer
leaves the running process holding the database it thinks it has, and its next checkpoint writes that
belief back over the restored one. If it can't stop the app, it refuses. The stale `-wal`/`-shm` sidecars
go with the old file for the same reason: left behind, SQLite replays them over the restored database.

> Backups are a copy at a moment; [Litestream](sqlite.md#continuous-backup-with-litestream) is continuous
> replication for when the box dies. They answer different questions — "let me look at what production
> has" and "the server is gone" — and an app that matters wants both.

## `rask deploy` — ship to a single host over SSH

```bash
rask deploy --host root@box --domain app.example.com      # bare VPS → live HTTPS site (sets the box up first)
rask deploy --host deploy@box --port 8080                 # no domain: publish a port, bring your own TLS
rask deploy                                               # redeploy: host/domain remembered
rask deploy --github-actions                              # write a workflow that deploys on push to main
rask deploy --dry-run --host deploy@box --domain app.example.com   # print the docker commands, run nothing
```

One command builds your app's Docker image **on the box** and runs it. Every deploy step is
`docker -H ssh://<host> …`, so there's no registry, no local Docker daemon, and no image tarball to
copy — the build context ships to the host's daemon over SSH and builds there. It deploys the
`Dockerfile` that `rask new --docker` scaffolds (point at another with `--dockerfile`).

**Handed a box that isn't ready, it sets it up** — installs Docker, creates a non-root `deploy` login
with your keys, configures a firewall (one that covers Docker's published ports, which ufw does not
reach on its own), and hardens SSH — after showing you the list and asking once. So
a fresh VPS goes live without you opening an SSH session. It's idempotent (a ready box is left alone,
with no prompt), and nothing that could lock you out happens until a fresh connection has proved the new
login works — with a rollback timer on the box as the backstop. See
[deployment.md](deployment.md#the-first-deploy-to-a-bare-box) for the full story.

**With `--domain`** Rask runs a shared [Caddy](https://caddyserver.com) reverse proxy on the box that
fetches an automatic Let's Encrypt certificate, so you get a live HTTPS site with nothing else to
configure. Deploys are **zero-downtime**: the new container starts alongside the old one (blue-green),
is waited on until its container is running **and answers an HTTP health check** (`GET /health` by
default — the endpoint `rask new` scaffolds), then Caddy is reloaded to point at it before the old one
is removed. If the new container fails to start, or fails its health probe, the previous version keeps
serving. Probe a different path with `--health-path`, or skip the probe with `--no-health-check`.
HTTP requests are zero-downtime; **live sessions re-establish**, because a session lives in the container
being replaced and cannot hand over. The retiring container announces its shutdown first, so open pages
show "Updating…" and reload onto the new one at their previous scroll position, with whatever the user
had typed put back — see
[the shutdown ladder](deployment.md#the-shutdown-ladder).

**Multiple apps share one box.** Each app container is labelled, so the proxy's routing is regenerated
from the host's live containers on every deploy — deploying a second app (a different `--domain`)
leaves the first untouched. Without `--domain`, the app is published on `--port` (default `8080`) and
you put your own TLS/reverse proxy in front (there's no zero-downtime swap on a single published port).
That downtime is inherent to publishing one port; *staying* down is not. If the new container fails to
start or fails its health check, port mode brings `:previous` back automatically — the last image that
passed the same gate — and still exits non-zero, so a bad image costs you a blip rather than an outage.
Use `rask deploy rollback` to undo a deploy that *did* come up healthy.

**Your database survives redeploys.** Each deploy runs a fresh container, so `rask deploy` mounts a
per-app named volume and points the app at it (`ConnectionStrings:App` → `Data Source=/data/app.db`) — the
SQLite database persists across container replacements. The old container keeps serving for a moment after
the proxy switches (so a request already in flight to it isn't cut), then is stopped gracefully (SIGTERM →
its Litestream flush + WAL checkpoint) before removal. The `rask new --docker` Dockerfile prepares a
writable `/data`; a custom Dockerfile needs `RUN mkdir -p /data && chown $APP_UID:$APP_UID /data`. Add
[`Rask.SQLite.Litestream`](sqlite.md#continuous-backup-with-litestream) to also stream it off the box.

| Option | Purpose |
| --- | --- |
| `--host user@box` | SSH target. Required on the first deploy, then remembered in `.rask/deploy.json`. |
| `--domain <host>` | Front the app with auto-HTTPS Caddy. Omit to publish `--port` directly. |
| `--port <n>` | Host port when there's no domain (default `8080`). |
| `--container-port <n>` | The port your app listens on **inside** the container — what the proxy is pointed at and what the readiness probe hits (default `8080`, which every `rask new --docker` Dockerfile uses). Only needed for a hand-written Dockerfile that exposes something else. Remembered in `.rask/deploy.json`, and recorded on the container so a host running apps on different ports keeps each one's routing correct. |
| `--name <slug>` | Image/container name (default: the project name). |
| `--project <path>` · `--dockerfile <path>` | The build context / Dockerfile, if not the current project. |
| `--env KEY=VALUE` · `--env-file <path>` | Runtime environment for the app container (repeat `--env`). |
| `--health-path <path>` | The path the readiness probe hits before switching traffic (default `/health`). Remembered in `.rask/deploy.json`. |
| `--no-health-check` | Gate only on the container running (skip the HTTP probe) — for apps without a health endpoint. Remembered. |
| `--github-actions` | Write `.github/workflows/deploy.yml` (deploy on push to main) and print the secrets to add. Touches no host. |
| `--dry-run` | Print the exact docker commands without running them. |

### After it's live — `status`, `logs`, `rollback`

```bash
rask deploy status            # what's running on the box (every app, not just this one)
rask deploy logs             # the live container's last 100 lines
rask deploy logs --follow    # ...and stream new ones
rask deploy rollback         # put the previous image back, health-gated
```

These read the same `rask.*` container labels a deploy writes, so they describe the box **as it actually
is** rather than as `.rask/deploy.json` remembers it. They need a host (from the config or `--host`) and
nothing else — no Dockerfile, no build.

`status` lists every Rask-managed app sharing the box, with its URL or published port, its blue/green
colour, and how long it has been up — and tells you whether a rollback is currently possible.

`rollback` exists for the failure the blue-green swap can't catch. That swap protects you from a release
that *fails* — one that won't start, or won't answer its health check. It can do nothing about a release
that starts, answers, and is simply **wrong**. Each deploy therefore moves the image it replaces to
`<app>:previous` before building, and `rask deploy rollback` starts that image back up through the same
gates a deploy uses (running → healthy → reload the proxy → retire the old container). It then swaps the
two tags, so running it again undoes the rollback rather than repeating it.

| Option | Applies to | Purpose |
| --- | --- | --- |
| `--tail <n\|all>` | `logs` | Lines to show (default `100`). |
| `--follow` | `logs` | Stream new lines until interrupted. |

Options that describe *what to deploy* (`--domain`, `--container-port`, `--dockerfile`, `--dry-run`, …)
are rejected on these verbs rather than silently ignored — they operate on what is already deployed.

Host setup options — these only matter the first time you deploy to a box:

| Option | Purpose |
| --- | --- |
| `--setup-host` | Prepare the host without asking. Needed when there's no terminal to confirm on. |
| `--no-setup-host` | Never change the host; fail with instructions instead. What the generated CI workflow uses. |
| `--deploy-user <name>` | The non-root login to create and deploy as when given a root host (default: `deploy`). |
| `--no-deploy-user` | Keep deploying as the `--host` login instead of creating a non-root one. |
| `--no-firewall` | Don't configure `ufw` on the host, and don't put Docker's published ports behind it. |
| `--no-harden-ssh` | Don't disable SSH password login and root login on the host. |

**Prerequisites.** The [Docker CLI](https://docs.docker.com/get-docker/) installed locally (it's the
client for every remote `docker` call, even though nothing builds on your machine), and key-based SSH
to the host so `ssh user@box` works non-interactively. **The host needs nothing else** — Docker and the
rest are installed for you on the first deploy. Point your domain's DNS `A`/`AAAA` record at the host
before the first `--domain` deploy so the certificate can be issued. `.rask/deploy.json` remembers the
host/domain/port for repeat deploys but **never stores secrets** — pass those via `--env`/`--env-file`
each time.

**Deploying from CI.** `rask deploy --github-actions` writes a workflow that runs this same command on
every push to `main`, and prints the two `gh secret set` lines it needs (an SSH key and the host's
fingerprint). Everything else comes from the committed `.rask/deploy.json`. It deploys with
`--no-setup-host`: prepare the box once from your own machine, so CI never reconfigures a host.

## `rask doctor` — check before you hit it

```bash
rask doctor          # what's here, what's missing, and what only some commands need
rask doctor --json   # the same verdict, for CI
```

Every probe it runs already existed, each reachable only from the command that needed it — so the way
to find out whether your machine could run something was to run it and see where it stopped, halfway
through, having already done some of the work.

```
  ok    rask                0.20.1
  ok    dotnet sdk          10.0.302
  ok    dotnet-ef           installed
  warn  docker              not found
                            Only `rask deploy` needs it — https://docs.docker.com/get-docker/
  ok    project             /src/Shop
  ok    database            SQLite
  fail  .rask/deploy.json   isn't valid JSON: 'o' is an invalid start of a property name…
                            Until it parses, its remembered settings are silently ignored.
```

**Warnings aren't failures.** Docker missing is fatal to `rask deploy` and irrelevant to everyone else,
so only a genuinely broken thing sets the exit code (`1`); a machine that can start every command exits
`0`.

**It is read-only.** It reports; it never installs or fixes. A doctor that quietly installed the tooling
it found missing would be doing the thing you ran it to avoid.

One thing it exists to catch: a corrupt `.rask/deploy.json` used to be swallowed — the loader falls
back to defaults, so a typo'd file looked exactly like no file, and the remembered host vanished with
nothing said. It now says so in passing, and `doctor` reports it as a failure.

## `rask info` — environment report

```bash
rask info
```

```text
  Rask CLI         0.17.0
  .NET SDK         10.0.201
  OS               macOS 26.5.1
```

A quick check when diagnosing a machine: the tool version, the .NET SDK version, and the OS.
`rask --version` prints just the tool version.

## `rask completion` — shell completion

```bash
rask completion bash >> ~/.bashrc
rask completion zsh  > "${fpath[1]}/_rask"
rask completion fish > ~/.config/fish/completions/rask.fish
```

Prints a completion script for `bash`, `zsh`, or `fish`. It's generated from the live command list and
each command's option schema, so it always matches the installed CLI — completing command names and
their `--options`. Re-run it after upgrading `rask` to pick up new commands and flags.

## Roadmap

The CLI is the front door for Rask's "one person framework" tooling — from `rask new` to `rask deploy`,
the whole lifecycle lives here. See the [development workflow](development-workflow.md) for how the
framework is built.
