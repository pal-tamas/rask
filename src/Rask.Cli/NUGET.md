# Rask.Cli

**The `rask` command-line tool** — a productivity front-door for the
[Rask](https://github.com/pal-tamas/rask) framework. It is a thin, dependency-free wrapper over the
.NET SDK: scaffold a project, run it with hot reload, and inspect your environment, all with short,
Rask-aware commands.

## Install

```bash
dotnet tool install -g Rask.Cli
```

## Use

```bash
# Scaffold a new server-rendered app with auth + a Dockerfile
rask new MyApp --auth --docker

# Scaffold a browser-WASM PWA instead
rask new MyApp --template wasm --pwa

# Run it with hot reload (dotnet watch)
rask dev

# Show CLI / SDK / template environment info
rask info
```

## Commands

| Command | What it does |
|---|---|
| `rask new <name>` | Create a project from a Rask template (`--template server\|wasm\|wasm-hosted\|native`), forwarding `--auth` / `--pwa` / `--cqrs` / `--docker`. Installs `Rask.Templates` on demand. |
| `rask dev` | Run the app with C# Hot Reload (`dotnet watch run`); `--no-hot-reload` for a plain run. Args after `--` reach the app. |
| `rask info` | Report the CLI version, .NET SDK version, template status, and OS. |

Run `rask <command> --help` for command-specific usage, or `rask --version` for the tool version.

## Notes

- **No external dependencies** — pure BCL over the `dotnet` SDK you already have.
- More commands (`generate`, `db`, `deploy`) are on the roadmap as Rask grows into a complete
  one-person framework.
