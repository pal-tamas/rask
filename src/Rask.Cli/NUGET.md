# Rask.Cli

**The `rask` command-line tool** — a productivity front-door for the
[Rask](https://github.com/pal-tamas/rask) framework. It is a thin, dependency-free wrapper over the
.NET SDK: scaffold a project, run it with hot reload, manage migrations, and deploy it to a single host
over SSH — all with short, Rask-aware commands.

## Install

```bash
dotnet tool install -g Rask.Cli
```

## Use

```bash
# Scaffold a new server-rendered app with a SQLite database + a Dockerfile
rask new MyApp --data --docker

# Scaffold a browser-WASM PWA instead
rask new MyApp --template wasm --pwa

# Scaffold a routed page, a component, or a full CRUD feature
rask generate page Products
rask generate component PriceTag
rask generate feature Product Name:string Price:decimal

# Create and apply its EF Core migration
rask db add InitialCreate
rask db update

# Run it with hot reload (dotnet watch)
rask dev

# Ship it to a single host over SSH — auto-HTTPS, zero-downtime
rask deploy --host deploy@box --domain app.example.com

# Show CLI / SDK / OS environment info
rask info
```

## Commands

| Command | What it does |
|---|---|
| `rask new <name>` | Create a project from a Rask template (`--template server\|wasm\|wasm-hosted\|native`), forwarding `--auth` / `--pwa` / `--cqrs` / `--data` / `--docker` (`--data` pre-wires a SQLite `AppDbContext`). Every template is generated directly — no `dotnet new` needed. |
| `rask generate <page\|component> <Name>` | Scaffold a routed page or a component into the current project (folder-based namespace, no-overwrite, `--dry-run`). |
| `rask generate <job\|email> <Name>` | Scaffold a background job (`IJob` + handler) or an email-body component, adding the `Rask.Jobs` / `Rask.Mail` package. Aliases: `rask g j` / `rask g e`. |
| `rask generate feature <Name> <field:type> …` | Scaffold a full CQRS + EF Core CRUD vertical slice — encapsulated entity (`Create`/`Update`, Guid id), `DbContext`, commands/queries + handlers, and pages that dispatch via `IDispatcher`. Aliases: `rask g f`. |
| `rask db <add\|remove\|list\|update\|drop>` | Manage EF Core migrations — a friendly `dotnet ef` wrapper that finds the project and installs `dotnet-ef` on demand. |
| `rask deploy` | Build and run the app on a single host over SSH (`docker -H ssh://…`). Sets a bare box up first (Docker, a non-root deploy login, firewall, SSH hardening) after asking. `--domain` fronts it with auto-HTTPS Caddy; deploys are zero-downtime and multiple apps share one box. `--github-actions` writes a workflow that deploys on push. |
| `rask dev` | Run the app with C# Hot Reload (`dotnet watch run`); `--no-hot-reload` for a plain run. Args after `--` reach the app. |
| `rask info` | Report the CLI version, .NET SDK version, and OS. |
| `rask completion <bash\|zsh\|fish>` | Print a shell completion script, generated from the live command + option set. |

Run `rask <command> --help` for a full reference — arguments, a described options table, and examples
— or `rask --version` for the tool version. Output is colorized on a terminal and plain when piped or
under `NO_COLOR`.

## Notes

- **No external dependencies** — pure BCL, no NuGet packages of its own. It drives tools you already have: the
  `dotnet` SDK, and for `rask deploy` the Docker CLI and `ssh` (host setup installs Docker on the *remote* box
  from [get.docker.com](https://get.docker.com), never on yours).
- The CLI is the front door to Rask, the .NET One Person Framework — the whole lifecycle from `new` to `deploy`.
