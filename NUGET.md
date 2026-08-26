<div align="center">

<img alt="Rask" src="https://raw.githubusercontent.com/pal-tamas/rask/main/assets/rask-logo.svg" width="280">

### The .NET One Person Framework — build, run, and ship a whole product solo, in C#, on one server.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/pal-tamas/rask/blob/main/LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

</div>

Write components as plain C# classes. Return a tree of HTML from `Render()`. **No `.razor`, no JSX,
no JavaScript to write** — and the *same* component code runs server-rendered with live WebSocket
updates or fully client-side on WebAssembly.

```csharp
[Route("/counter")]
public sealed class Counter : Component
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

## Install

> **Prerequisites:** the **.NET 10 SDK** (`dotnet --version` ≥ `10.0`); the `wasm-tools` workload
> (`dotnet workload install wasm-tools`) for the WASM templates.

```bash
dotnet tool install -g Rask.Cli          # the rask CLI — scaffold, migrate, run, deploy
rask new MyApp                            # or: --template wasm | wasm-hosted
rask db add Initial && rask db update     # migrations, via dotnet-ef
rask dev                                  # run with hot reload
rask deploy --host you@box --domain app.example.com       # build + run on one box, over SSH
```

Or add to an existing project. **Pick a host:**

```bash
dotnet add package Rask.Server            # server-rendered over WebSockets
dotnet add package Rask.Wasm              # client-side WebAssembly
dotnet add package Rask.Wasm.Hosting      # host a published WASM bundle on ASP.NET
```

**Then the batteries you want** — each is opt-in, and every one is a `AddRaskX<AppDbContext>()` call plus
a `modelBuilder.AddRaskX()` schema line:

```bash
dotnet add package Rask.Data              # base entity + EF interceptors (soft delete, concurrency, events)
dotnet add package Rask.Cqrs              # source-generated CQRS/mediator (queries, commands, notifications)
dotnet add package Rask.Cqrs.Client       # dispatch a message to the server from a WASM client
dotnet add package Rask.Cqrs.Server       # host the endpoint those clients dispatch to
dotnet add package Rask.Jobs              # durable background jobs
dotnet add package Rask.Mail              # transactional email queue
dotnet add package Rask.Cache             # read-through cache
dotnet add package Rask.Outbox            # transactional outbox for domain events
dotnet add package Rask.Logging           # durable log store (its own SQLite file)
dotnet add package Rask.Dashboard         # the /_rask operator dashboard over every pillar
dotnet add package Rask.WebPush           # send Web Push notifications from the backend
dotnet add package Rask.Signaling         # host the WebRTC signaling relay ISignaling connects to
```

**Database** — SQLite, treated as a real production database:

```bash
dotnet add package Rask.SQLite                        # production pragmas (WAL, busy_timeout) via UseRaskSqlite
dotnet add package Rask.SQLite.EntityFrameworkCore    # the EF Core provider glue
dotnet add package Rask.SQLite.Litestream             # managed continuous replication
dotnet add package Rask.SQLite.Snapshots              # scheduled Online-Backup-API copies
dotnet add package Rask.SQLite.Browser                # a persistent SQLite database inside a WASM app
```

**UI and testing:**

```bash
dotnet add package Rask.Bootstrap                     # typed Bootstrap 5.3 components
dotnet add package Rask.Validation.DataAnnotations    # or Rask.Validation.FluentValidation
dotnet add package Rask.Testing                       # render + drive components in unit tests
```

## Why Rask

After 15+ years building full-stack .NET apps — WebForms, MVC, Angular and React over a C# API — I wanted the front end
back in C# without `.razor` mixing markup and code. So Rask makes a component a plain C# class that returns a tree, runs
the *same* code on Server or WASM, and treats the network as the bottleneck (a state change ships a minimal diff, not
the page). It's a craft project built in the open, deep on Roslyn source generators and tree diffing.

- **One component model, two hosts** — the same C# component runs Server (live diff over WS) or WASM.
- **Markup is a chain** — a Roslyn generator emits `Div.Class("card")`, `Counter.Start(3)` and type-safe routes, so the IDE lists every step and a missing one is a compile error.
- **Scoped CSS & JS** — sibling `Component.css`/`Component.js`, content-addressed and cached.
- **Routing, lifecycle, forms, validation, auth** — batteries included, no JavaScript required.
- **Toast messages** — inject `IToaster` for transient messages that survive a client-side navigation.
- **Tiny live updates** — a minimal edit-op diff ships instead of the whole page.
- **Slow-link aware** — WASM boot shows download progress; a slow Server round-trip surfaces a pending bar.
- **Optional typed Bootstrap** — `Rask.Bootstrap` adds typed Bootstrap 5.3 components (`BsButton`/`BsCard`/`BsModal`/…), layout primitives (`BsContainer`, the `BsRow`/`BsCol` responsive grid, and `BsStack`), `IFormControl<T>`-bound inputs including the zero-JS `BsSelect`/`BsMultiSelect` comboboxes (full keyboard/listbox a11y, opt-in `Filter` search, `OptionGroup` grouping, `OptionDisabled` per-option disable, and a multiselect `SelectAll` header), a typed `BsIcon`, typed utility classes, and a `BsDataGrid<T>` for list screens (typed columns, sorting, paging, footer totals, master-detail), with interactive components driven by the live runtime — no JavaScript. See [docs/bootstrap.md](https://github.com/pal-tamas/rask/blob/main/docs/bootstrap.md).

## Links

- 📖 **[Documentation](https://github.com/pal-tamas/rask/tree/main/docs)** ·
  [Getting started](https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md) ·
  [Configuration](https://github.com/pal-tamas/rask/blob/main/docs/configuration.md) ·
  [Observability](https://github.com/pal-tamas/rask/blob/main/docs/observability.md) ·
  [Accessibility](https://github.com/pal-tamas/rask/blob/main/docs/accessibility.md)
- 🚀 **[Live demo](https://pal-tamas.github.io/rask/docs/)**
- 💻 **[Source & README](https://github.com/pal-tamas/rask)**
- 🤖 **[AI assistant guide](https://github.com/pal-tamas/rask/blob/main/llms.txt)**

Licensed under MIT.
