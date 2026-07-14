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

## `rask generate` — scaffold code

```bash
rask generate page Products                  # → Features/Products/ProductsPage.cs  ([Route("/products")])
rask generate page Products --route /catalog # a custom route
rask generate component PriceTag             # → Components/PriceTag.cs
rask generate component PriceTag -o Widgets  # into a chosen folder
rask generate page Orders --dry-run          # print what would be written, write nothing

# A full CRUD vertical slice — entity + DbContext + list/create/edit pages
rask generate feature Product --fields "Name:string,Price:decimal,InStock:bool"
```

`rask generate` writes idiomatic files into the current project. It finds the owning `.csproj` by
walking up from the working directory, derives each file's namespace from its folder (root namespace +
folder path, the C# convention), and **refuses to overwrite an existing file** unless you pass `--force`.

| Artifact | Emits | Class / namespace |
|----------|-------|-------------------|
| `page <Name>` | `Features/<Name>/<Name>Page.cs` — a routed page `Component` with a `Head` title | `<Name>Page` in `<Root>.Features.<Name>` |
| `component <Name>` | `Components/<Name>.cs` — a plain `Component` | `<Name>` in `<Root>.Components` |
| `feature <Name> --fields …` | `Features/<Plural>/` — a POCO entity, a `DbContext`, and list / create / edit pages wired to EF Core | in `<Root>.Features.<Plural>` |

| Option | Meaning |
|--------|---------|
| `--fields`, `-f` | `feature` only: the entity's fields as `Name:type,…`. Types: `string`, `int`, `long`, `decimal`, `double`, `bool`, `DateTime`, `Guid` (aliases like `text`/`number`/`money`/`date` too). An `Id` is added automatically. |
| `--context`, `-c` | `feature` only: reference an existing `DbContext` by name instead of generating a feature-local one (then add a `DbSet` to it). |
| `--plural`, `-p` | `feature` only: the plural used for the folder, DbSet, list page, and route. Give the entity a **singular** name (`Product`) and this defaults to a simple pluralization (`Products`); override it when that guess is wrong (`--plural People`). |
| `--route`, `-r` | `page` only: the `[Route]` path (default: kebab-case of the name, e.g. `/products`). |
| `--output`, `-o` | Write into this folder instead of the default (the namespace follows the folder). |
| `--force` | Overwrite existing file(s). |
| `--dry-run` | Print the file(s) that would be written, and write nothing. |

The generated code compiles as-is in any `dotnet new rask-*` project — the factory methods and the
`Component` base come from Rask's implicit usings, and pages navigate with the type-safe generated
`Routes.*()` URLs. A generated `feature` needs EF Core referenced; after scaffolding, `rask generate`
prints the one DI registration and the `dotnet ef` migration to run before it works.

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

The CLI is the front door for Rask's "one person framework" tooling. Next up: `rask db` (migrations)
and `rask deploy` (one-command deploy). See the [development workflow](development-workflow.md) for how
the framework is built.
