# Rask.Cli

**The `rask` command-line tool** — a productivity front-door for the
[Rask](https://github.com/pal-tamas/rask) framework. It is a thin wrapper over the .NET SDK: scaffold a
project, run it with hot reload, manage migrations, back the database up and restore it, and deploy it to
a single host over SSH — all with short, Rask-aware commands.

## Install

```bash
curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh
```

That adds the .NET 10 SDK, this tool, and the dependencies it shells out to (`dotnet-ef`, the
`wasm-tools` workload, Node for the SPA templates) — all under `$HOME`, no `sudo`. Already have the
.NET 10 SDK and want only the tool:

```bash
dotnet tool install -g Rask.Cli
```

## Use

```bash
# Scaffold a new server-rendered app with a SQLite database + a Dockerfile
rask new MyApp --auth

# Scaffold a browser-WASM PWA instead
rask new MyApp --template wasm

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
| `rask new <name>` | Create a project from a Rask template (`--template server\|wasm`) with **every battery the template supports** — database, CQRS, jobs, mail, cache, outbox, snapshots, logs, the operator dashboard, PWA, Web Push, Docker, localization. `--auth` and the styling flags are the only things it asks you; `--no-<battery>` leaves one out. On the browser-WASM templates localization is the one opt-in (`--culture <tag>`), because it ships ICU — about a megabyte of extra download. Every template is generated directly — no `dotnet new` needed. |
| `rask db <add\|remove\|list\|update\|drop>` | Manage EF Core migrations — a friendly `dotnet ef` wrapper that finds the project and installs `dotnet-ef` on demand. |
| `rask deploy` | Build and run the app on a single host over SSH (`docker -H ssh://…`). Sets a bare box up first (Docker, a non-root deploy login, firewall, SSH hardening) after asking. `--domain` fronts it with auto-HTTPS Caddy; deploys are zero-downtime and multiple apps share one box. `--github-actions` writes a workflow that deploys on push. |
| `rask dev` | Run the app with C# Hot Reload (`dotnet watch run`). Finds the project itself, restarts on edits hot reload can't apply, `--open` for a browser. `--once` for a plain run. Args after `--` reach the app. |
| `rask info` | Report the CLI version, .NET SDK version, and OS. |
| `rask completion <bash\|zsh\|fish>` | Print a shell completion script, generated from the live command + option set. |

Run `rask <command> --help` for a full reference — arguments, a described options table, and examples
— or `rask --version` for the tool version. On a terminal the output is colorized and long descriptions
wrap; piped or under `NO_COLOR` it is plain text with no escape codes and no reflowing, so `rask doctor |
grep` and CI logs read exactly as they always have.

Run `rask` with no arguments on a terminal and it opens the new-project wizard: name, project type,
styling, Docker, and a checklist of batteries. Anything you already passed on the command line is kept
and its question skipped, so `rask new --template wasm` only asks for what's left. Piped or scripted,
bare `rask` still prints the help page.

## Notes

- **Two dependencies, and no services.** [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite)
  for `rask db backup`, which needs SQLite's Online Backup API to copy a live WAL database without tearing it, and
  [Spectre.Console](https://spectreconsole.net) for the terminal surface — the help pages, the `deploy status` and
  `doctor` tables, the progress spinners, and the `rask new` wizard. Everything else is the BCL and tools you already
  have: the `dotnet` SDK, and for `rask deploy` the Docker CLI and `ssh` (host setup installs Docker on the *remote*
  box from [get.docker.com](https://get.docker.com), never on yours).
- The CLI is the front door to Rask, the .NET One Person Framework — the whole lifecycle from `new` to `deploy`.
