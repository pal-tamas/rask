# The `rask` CLI

`Rask.Cli` is a [.NET tool](https://learn.microsoft.com/dotnet/core/tools/global-tools) that gives
Rask a short, task-focused command line on top of the .NET SDK. It is a thin, dependency-free wrapper
— every command shells out to `dotnet` — so it never gets in the way of the tools you already use.

## Install

```bash
dotnet tool install -g Rask.Cli
```

That puts a `rask` command on your `PATH`. Update it later with `dotnet tool update -g Rask.Cli`.

> The CLI is optional. Everything it does, you can still do by hand with `dotnet new` and
> `dotnet watch` — `rask` just makes the common paths shorter and Rask-aware.

## `rask new` — scaffold a project

```bash
rask new MyApp                       # a server-rendered app (the default template)
rask new MyApp --auth --docker       # + cookie auth + a production Dockerfile
rask new Spa --template wasm --pwa   # an installable browser-WASM PWA
rask new Shop --template wasm-hosted # a WASM SPA with an ASP.NET host
rask new Field --template native     # a native iOS + Android app
```

`rask new` resolves the friendly `--template` name to the matching `dotnet new` template, forwards the
feature flags that template supports, and installs the `Rask.Templates` package automatically the first
time if it isn't present.

| Option | Meaning |
|--------|---------|
| `<name>` (or `--name`) | The project name. Required. |
| `--template`, `-t` | `server` (default), `wasm`, `wasm-hosted`, or `native`. |
| `--auth` | Scaffold a cookie login/session (web templates). |
| `--pwa` | Scaffold a web app manifest + service worker (web templates). |
| `--cqrs` | Wire up `Rask.Cqrs` (the `server` template only). |
| `--docker` | Emit a production `Dockerfile` + `.dockerignore` (web templates). |
| `--output`, `-o` | Target directory (defaults to a folder named after the project). |

Requesting a flag a template doesn't support (for example `--cqrs` on `wasm`) fails fast with the list
of flags that template *does* support, rather than passing an unknown option through to `dotnet new`.

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

## `rask info` — environment report

```bash
rask info
```

```text
  Rask CLI         0.16.1
  .NET SDK         10.0.201
  Rask templates   installed
  OS               macOS 26.5.1
```

A quick check when diagnosing a machine: the tool version, the .NET SDK version, whether the Rask
templates are installed, and the OS. `rask --version` prints just the tool version.

## Roadmap

The CLI is the front door for Rask's "one person framework" tooling. Planned commands include
`rask generate` (scaffold a CRUD feature slice), `rask db` (migrations), and `rask deploy`
(one-command deploy). See the [development workflow](development-workflow.md) for how the framework is built.
