# Rask.Server

The **ASP.NET Core host** for [Rask](https://rask.sh/), a full-stack .NET web framework for teams of any
size — with every battery. Pages and components are plain C#; this package renders them into the first
HTTP response and keeps every page live over a WebSocket, sending only the DOM changes a click or a timer
produced, with an HTTP fallback where a socket cannot open.

- **Batteries included, all on:** a database (SQLite by default; PostgreSQL or SQL Server by
  `Rask:Database:Provider`), the mediator and query cache, accounts, background jobs, transactional email,
  cache, file storage, the outbox, an operator dashboard at `/_rask`, durable logs, Web Push, SQLite
  snapshots and continuous backup. Referencing this package is what turns them on.
- Depends on [`Rask`](https://www.nuget.org/packages/Rask), the shared core (components, the chain,
  routing, forms, scoped CSS/TypeScript, the generators) and on the [`Rask.Ui`](https://www.nuget.org/packages/Rask.Ui) kit.
- Event handlers, data access and secrets stay on the server; the browser receives HTML and diffs.
- Settings bind from `appsettings.json` under `Rask:` (`Rask:Server`, `Rask:Mail`, `Rask:Database`, …).

## Install

```bash
dotnet add package Rask.Server
```

Or scaffold a complete app with the CLI — `rask new Shop` — see
[installation](https://rask.sh/docs/guides/installation).

## Use

```csharp
// Program.cs — the whole file
RaskApp.Create(args).Run<App>();
```

The file says only what this app does *without*, or configures differently:

```csharp
var app = RaskApp.Create(args);

app.Configure(c =>
{
    c.Jobs.Off();                                            // no background work here
    c.Mail.Configure(o => o.From = "no-reply@example.com");
});

app.Run<App>();
```

```csharp
[Route("/")]
public sealed partial class Home : Component
{
    int _count;

    protected override Component? Render() =>
        Div[
            H1["Hello, Rask"],
            Button.OnClick(() => _count++)[$"Clicked {_count} times"]
        ];
}
```

`RaskApp` wraps `WebApplicationBuilder` rather than replacing it: `app.Services` is the same collection, and
`AddRask()` / `MapRask<App>()` stay public for a host assembled by hand.

Guides: [getting started](https://rask.sh/docs/guides/getting-started) ·
[live pages](https://rask.sh/docs/guides/render-modes) · [routing](https://rask.sh/docs/guides/routing) ·
[configuration](https://rask.sh/docs/guides/configuration)
