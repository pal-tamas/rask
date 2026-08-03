# The `rask` CLI

`Rask.Cli` is a [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools) that gives
Rask a short, task-focused command line on top of the .NET SDK. It is dependency-free, it generates or
shells out to `dotnet` for everything it does, and it never gets in the way of the tools you already use.

> **In a hurry?** The [cheat sheet](cheatsheet.md) lists every command on one page, and the
> [recipes](recipes.md) answer "how do I do X?" with the command and the wiring line.

## Install

```bash
dotnet tool install -g Rask.Cli
```

That puts a `rask` command on your `PATH`. Update it later with `dotnet tool update -g Rask.Cli`.

> `rask` is a thin, Rask-aware layer over the .NET SDK: it owns scaffolding end to end (`rask new`,
> `rask generate`), and shells out to `dotnet` for the rest — `rask dev` wraps `dotnet watch`, `rask db`
> wraps `dotnet ef`.

## Getting help

`rask` on its own lists the commands. `rask <command> --help` (or `-h`) prints that command's full
reference — its arguments, an aligned table of every option with a one-line description, and
copy-pasteable examples:

```bash
rask                       # list all commands
rask new --help            # arguments, options, and examples for `new`
rask generate feature --help
```

Help (and other output) is colorized when `rask` is writing to a terminal, and falls back to plain
text when the output is piped or when the [`NO_COLOR`](https://no-color.org) environment variable is
set — so `rask info | cat` and CI logs stay clean.

## `rask new` — scaffold a project

```bash
rask new                             # interactive: prompts for name, template, and features
rask new MyApp                       # a server-rendered app (the default template)
rask new MyApp --auth --docker       # + cookie auth + a production Dockerfile
rask new Blog --data --docker        # + a SQLite database ready for `rask generate feature`
rask new Spa --template wasm --pwa   # an installable browser-WASM PWA
rask new Shop --template wasm-hosted # a WASM SPA with an ASP.NET host
rask new Field --template native     # a native iOS + Android app
```

Run `rask new` on its own (no name) and — on a terminal — it walks you through a short wizard: the
project name, a numbered template picker, and a yes/no for each feature the template supports. It then
scaffolds exactly as if you'd passed the flags. Piped or in a script (no terminal), a missing name is a
plain error instead, so automation stays predictable.

The CLI writes the project's files itself, pins the `Rask.*` package references, and runs `dotnet
restore` so the output builds immediately. `wasm-hosted` emits a three-project solution — `MyApp.Client`
(the browser-WASM SPA), `MyApp.Server` (the ASP.NET host you run and deploy), and `MyApp.Shared` (a class
library both reference).

A new project is deliberately **minimal** — four files, nothing to delete before you start:

```
MyApp/
  MyApp.csproj
  Program.cs
  App.cs                          the shell + a welcome home page that teaches the CLI
  Properties/launchSettings.json
```

`App.cs` holds both the root shell (which every page renders through) and the `[Route("/")]` welcome
page, styled with Bootstrap. Add pages and components to taste — `rask generate` is the fast path.

| Option | Meaning |
|--------|---------|
| `<name>` (or `--name`) | The project name. Required. |
| `--template`, `-t` | `server` (default), `wasm`, `wasm-hosted`, or `native`. |
| `--auth` | Scaffold a cookie login/session (web templates). |
| `--pwa` | Web app manifest + service worker + icon, and the wiring to serve them (web templates). |
| `--cqrs` | Wire up `Rask.Cqrs` — `AddRaskCqrs()` + the package reference (the `server` template only). |
| `--data` | Pre-wire a SQLite database: an empty `AppDbContext`, `AddRaskData()`, and a `UseRaskSqlite` (WAL + `busy_timeout`) DbContext factory — so the first `rask generate feature <Name> --context AppDbContext` is immediately runnable with `rask db add`/`update`. Implies `--cqrs` (the `server` template only). |
| `--docker` | Emit a production `Dockerfile` + `.dockerignore` (web templates). |
| `--host` | `local` (default) or `server` — which native mode to scaffold (the `native` template only). |
| `--output`, `-o` | Target directory (defaults to a folder named after the project). |
| `--dry-run` | Print the files that would be created and write nothing (skips `dotnet restore`). |

The flags wire a feature up; they don't scaffold sample pages for you to delete.

Requesting a flag a template doesn't support (for example `--cqrs` on `wasm`) fails fast with the list
of flags that template *does* support, rather than passing an unknown option through.

## `rask generate` — scaffold code

```bash
rask generate page Products                  # → Features/Products/ProductsPage.cs  ([Route("/products")])
rask generate page Products --route /catalog # a custom route
rask generate component PriceTag             # → Components/PriceTag.cs
rask generate component PriceTag -o Widgets  # into a chosen folder
rask generate job SendWelcomeEmail           # → Jobs/SendWelcomeEmail.cs (IJob + handler)
rask generate email WelcomeEmail             # → Emails/WelcomeEmail.cs (an email-body component)
rask generate page Orders --dry-run          # print what would be written, write nothing

# A full CQRS + EF Core CRUD vertical slice
rask generate feature Product Name:string Price:decimal InStock:bool 'Note:string?(500)'
rask g f Order Total:decimal --id long   # short aliases: g = generate, f = feature
```

`rask generate` writes idiomatic files into the current project. It finds the owning `.csproj` by
walking up from the working directory, derives each file's namespace from its folder (root namespace +
folder path, the C# convention), and **refuses to overwrite an existing file** unless you pass `--force`.

| Artifact | Emits | Class / namespace |
|----------|-------|-------------------|
| `page <Name>` | `Features/<Name>/<Name>Page.cs` — a routed page `Component` with a `Head` title | `<Name>Page` in `<Root>.Features.<Name>` |
| `component <Name>` | `Components/<Name>.cs` — a plain `Component` | `<Name>` in `<Root>.Components` |
| `job <Name>` | `Jobs/<Name>.cs` — a background job: an `IJob` record + its `ICommandHandler` (adds the `Rask.Jobs` / `Rask.Cqrs` packages). Alias: `rask g j` | `<Name>` in `<Root>.Jobs` |
| `email <Name>` | `Emails/<Name>.cs` — an email-body component rendered to HTML by `Email.Body(...)` (adds the `Rask.Mail` package). **Auto-wires** into your `DbContext` — registers `AddRaskMail<Ctx>` in `Program.cs` and maps the mail table in `OnModelCreating` — when it finds a single one (or `--context <Name>`); otherwise prints the steps. Alias: `rask g e` | `<Name>` in `<Root>.Emails` |
| `feature <Name> <field:type> …` | `Features/<Plural>/` — an encapsulated entity (`Create`/`Update`, Guid id) with **value objects** for required strings (built-in validation), an EF `IEntityTypeConfiguration`, a `DbContext`, **CQRS** create/update/delete commands + list/get queries with handlers, and list / create / edit pages that dispatch via `IDispatcher` | in `<Root>.Features.<Plural>` |

| Option | Meaning |
|--------|---------|
| `<field:type> …` (positional) | `feature` only: the entity's fields, given **positionally** after the name — `rask g f Product Name:string Price:decimal`. Types: `string`, `int`, `long`, `decimal`, `double`, `bool`, `date` (→ `DateOnly`), `time` (→ `TimeOnly`), `datetime` (→ `DateTime`), `Guid` (aliases like `text`/`number`/`money` too). A field is optional with a trailing `?` (`Note:string?`); strings get a default max length, overridable with `Name:string(100)`. Quote specs containing `?` or `(…)` so your shell doesn't expand them (`'Note:string?(500)'`). An `Id` is added automatically. |
| `<card> <Target> <field:type> …` | `feature` only: **relationships** — after the root's fields, name a cardinality, a target entity, and its fields to scaffold a *related* entity in the same run. `1:n`/`n:1`/`1:1` add the foreign key, navigation properties, and EF mapping; `n:n` maps a many-to-many through EF Core's implicit join table (no join entity). E.g. `rask g f Post Title:string 1:n Comment Body:text` generates both `Post` and `Comment`, with `Comment.PostId` + `Comment.Post` and `Post.Comments`. Cardinalities: `1:n`, `0:n`, `n:1`, `n:0`, `1:1`, `0:1`, `n:n` — a leading `0` makes the foreign key optional. |
| `--fields`, `-f` | `feature` only: the legacy comma-joined form of the fields above — `--fields "Name:string,Price:decimal"`. Equivalent to the positional args; you can't use both at once. |
| `--id` | `feature` only: the entity's key type — `guid` (default), `int`, or `long`. |
| `--modal` | `feature` only (implies `--bs`): create + update happen in a `BsModal` on the list page, instead of separate create/edit pages. |
| `--bs` | `feature` only: render the pages with Rask.Bootstrap `Bs*` components (`BsCard`/`BsTable`/`BsButton`/`BsInput`/`BsCheck`/`BsIcon`) + `Bs.Join(...)` utility classes instead of raw core + Bootstrap class strings. |
| `--validation` | `feature` only: `valueobjects` (default — required strings become value objects with built-in, dependency-free validation), `dataannotations` (POCO + `[Required]`/`[MaxLength]` + `DataAnnotationsValidator`), or `fluent` (POCO + a generated `AbstractValidator` + `FluentValidationValidator`). |
| `--soft-delete` | `feature` only: the entity implements `ISoftDeletable` (a `DeletedAt` stamp) so `Delete` becomes a soft delete (via `Rask.Data`'s interceptor + a global query filter), and the list page gains a "Show deleted" toggle + a `Restore` action for deleted rows. |
| `--concurrency` | `feature` only: the entity implements `IVersioned` (an `int Version` optimistic-concurrency token). The edit form round-trips the original `Version` (a hidden field) and the update handler applies it, so an edit that races another loses gracefully — a `DbUpdateConcurrencyException` is caught and shown as an inline "this record changed — reload" message. |
| `--events` | `feature` only: emit typed domain-event records (`<Entity>Created`/`Updated`/`Deleted`, `INotification`) that the aggregate raises on create/update/delete, plus a sample `INotificationHandler` stub. `Rask.Data`'s interceptor publishes them in-process after the change commits (auto-registered by `AddRaskCqrs()`). |
| `--outbox` | `feature` only: like `--events`, but the events implement `IOutboxEvent` and are delivered through a **durable transactional outbox** ([`Rask.Outbox`](outbox.md)) — written to an `OutboxMessage` table in the same transaction as the change, then published by a background processor (at-least-once, crash-safe). The generated `DbContext` maps the table; the next-steps wire `AddRaskOutbox` + disable the in-process publisher. |
| `--tests` | `feature` only: also emit xUnit tests in a sibling `<Project>.Tests` project — a domain test (`Create`/`Update` + value-object validation) and, when the `DbContext` is generated, a SQLite round-trip persistence test. The test project is created and wired (test SDK, xUnit, a reference to the app) on first use, so `dotnet test` runs as-is. |
| `--no-restore` | `feature` only: don't add the NuGet packages automatically (just print them). |
| `--context`, `-c` | `feature` only: reference an existing `DbContext` by name instead of generating a feature-local one (then add a `DbSet` to it). |
| `--plural`, `-p` | `feature` only: the plural used for the folder, DbSet, list page, and route. Give the entity a **singular** name (`Product`) and this defaults to a simple pluralization (`Products`); override it when that guess is wrong (`--plural People`). |
| `--route`, `-r` | `page` only: the `[Route]` path (default: kebab-case of the name, e.g. `/products`). |
| `--output`, `-o` | Write into this folder instead of the default (the namespace follows the folder). |
| `--force` | Overwrite existing file(s). |
| `--dry-run` | Print the file(s) that would be written, and write nothing. |
| `--save-defaults` | `feature` only: remember this run's feature flags in `.rask/generate.json` (see below). |

**Team defaults (`.rask/generate.json`).** So a project doesn't retype the same feature flags every
time, `rask generate feature` reads defaults from `.rask/generate.json` at the project root — e.g.
`{ "bs": true, "validation": "fluent", "tests": true }`. Explicit flags on the command line always win.
Write the file by hand, or let the CLI record your choices: `rask generate feature Order Total:decimal
--bs --tests --save-defaults` scaffolds *and* remembers `--bs`/`--tests` for next time. Booleans are
opt-in (an absent key means off).

The generated code compiles as-is in any project scaffolded by `rask new` — the factory methods and the
`Component` base come from Rask's implicit usings, and pages navigate with the type-safe generated
`Routes.*()` URLs. Every generated entity inherits [`Rask.Data`](data.md)'s `Entity<TId>` (Id +
audit stamps + a domain-events buffer), so a generated `feature` needs **EF Core + `Rask.Cqrs` +
`Rask.Data`** referenced — `rask generate` **adds those packages to the project for you**
(`dotnet add package` for EF Core + SQLite, `Rask.Cqrs`, `Rask.Data`, and — with `--bs`/`--validation` —
`Rask.Bootstrap` / the validation library; pass `--no-restore` to skip). It then **writes the DI
registration** (`AddRaskCqrs()` + `AddRaskData()` + `AddDbContextFactory` with the interceptors) into
`Program.cs` for you — falling back to printing it if it can't find the file — and prints the migration
to create and apply with [`rask db`](#rask-db--ef-core-migrations) before it works.

Every command has short aliases: `rask g` = `rask generate`, and `g f` / `g c` / `g p` scaffold a
feature / component / page.

## `rask dev` — run with hot reload

```bash
rask dev                             # dotnet watch run in the current project
rask dev --project src/MyApp/MyApp.csproj
rask dev --no-hot-reload             # a plain dotnet run
rask dev -- --urls http://localhost:5005   # everything after -- goes to the app
```

By default `rask dev` runs `dotnet watch run`, so editing a component's `Render()` (or a scoped
`.css` / `.js`) and saving re-renders live via C# Hot Reload. Pass `--no-hot-reload` for a one-shot run,
and forward any app arguments after a `--` separator.

## `rask db` — EF Core migrations

```bash
rask db add InitialCreate            # create a migration for the current model
rask db list                         # list migrations and which are applied
rask db update                       # apply pending migrations to the database
rask db update 20240101_Init         # migrate up/down to a specific migration
rask db remove                       # undo the last (unapplied) migration
rask db drop --force                 # drop the database (a dev reset)
```

A friendly wrapper over `dotnet ef` for the everyday migration lifecycle, meant to pair with what
`rask generate feature` scaffolds. It finds the project for you (the single `.csproj` at or above the
current directory — override with `--project`), and if the EF Core tools aren't installed it installs
`dotnet-ef` globally the first time you run it.

| Action | Wraps | Notes |
| --- | --- | --- |
| `add <Name>` | `dotnet ef migrations add` | `--output <dir>` sets the migrations folder |
| `remove` | `dotnet ef migrations remove` | undo the last migration |
| `list` | `dotnet ef migrations list` | show migrations and applied state |
| `update [<target>]` | `dotnet ef database update` | apply pending, or migrate to a named point |
| `drop` | `dotnet ef database drop` | drops the database; prompts unless `--force` |

Shared options: `--project/-p` (the project owning the `DbContext`), `--startup-project/-s` (the app
that configures it; defaults to `--project`), and `--context/-c` (when the app has more than one
`DbContext`). Anything after `--` is forwarded to `dotnet ef` verbatim (e.g. `rask db update -- --verbose`).

The EF Core tools need the startup project to reference `Microsoft.EntityFrameworkCore.Design` —
projects from `rask generate feature` already do, and `rask db` adds it for you (via `dotnet add
package`) if it's missing.

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
with your keys, configures a firewall, and hardens SSH — after showing you the list and asking once. So
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

**Multiple apps share one box.** Each app container is labelled, so the proxy's routing is regenerated
from the host's live containers on every deploy — deploying a second app (a different `--domain`)
leaves the first untouched. Without `--domain`, the app is published on `--port` (default `8080`) and
you put your own TLS/reverse proxy in front (there's no zero-downtime swap on a single published port).

**Your database survives redeploys.** Each deploy runs a fresh container, so `rask deploy` mounts a
per-app named volume and points the app at it (`ConnectionStrings:App` → `Data Source=/data/app.db`) — the
SQLite database persists across container replacements. The old container is stopped gracefully (SIGTERM →
its Litestream flush + WAL checkpoint) before removal. The `rask new --docker` Dockerfile prepares a
writable `/data`; a custom Dockerfile needs `RUN mkdir -p /data && chown $APP_UID:$APP_UID /data`. Add
[`Rask.SQLite.Litestream`](sqlite.md#continuous-backup-with-litestream) to also stream it off the box.

| Option | Purpose |
| --- | --- |
| `--host user@box` | SSH target. Required on the first deploy, then remembered in `.rask/deploy.json`. |
| `--domain <host>` | Front the app with auto-HTTPS Caddy. Omit to publish `--port` directly. |
| `--port <n>` | Host port when there's no domain (default `8080`). |
| `--name <slug>` | Image/container name (default: the project name). |
| `--project <path>` · `--dockerfile <path>` | The build context / Dockerfile, if not the current project. |
| `--env KEY=VALUE` · `--env-file <path>` | Runtime environment for the app container (repeat `--env`). |
| `--health-path <path>` | The path the readiness probe hits before switching traffic (default `/health`). Remembered in `.rask/deploy.json`. |
| `--no-health-check` | Gate only on the container running (skip the HTTP probe) — for apps without a health endpoint. Remembered. |
| `--github-actions` | Write `.github/workflows/deploy.yml` (deploy on push to main) and print the secrets to add. Touches no host. |
| `--dry-run` | Print the exact docker commands without running them. |

Host setup options — these only matter the first time you deploy to a box:

| Option | Purpose |
| --- | --- |
| `--setup-host` | Prepare the host without asking. Needed when there's no terminal to confirm on. |
| `--no-setup-host` | Never change the host; fail with instructions instead. What the generated CI workflow uses. |
| `--deploy-user <name>` | The non-root login to create and deploy as when given a root host (default: `deploy`). |
| `--no-deploy-user` | Keep deploying as the `--host` login instead of creating a non-root one. |
| `--no-firewall` | Don't configure `ufw` on the host. |
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
