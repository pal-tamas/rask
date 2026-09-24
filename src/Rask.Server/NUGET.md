# Rask.Server

The **ASP.NET Core host** for [Rask](https://rask.sh/), a full-stack .NET web framework for teams of any
size. Pages and components are plain C#; this package renders them into the first HTTP response and keeps
every page live over a WebSocket, sending only the DOM changes a click or a timer produced — with an HTTP
fallback where a socket cannot open.

- Includes **Rask.Core** (components, the element chain, routing, forms, scoped CSS/TypeScript) and the
  Rask **source generators**, so one reference is the whole framework on the server.
- Event handlers, data access and secrets stay on the server; the browser receives HTML and diffs.
- Settings bind from `appsettings.json` under `Rask:` (`Rask:Server`, `Rask:Culture`, …).

## Install

```bash
dotnet add package Rask.Server
```

Or scaffold a complete app with the CLI — `rask new Shop` — see
[installation](https://rask.sh/docs/guides/installation).

## Use

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRask();

var app = builder.Build();
app.MapStaticAssets();
app.MapRask<App>();   // App is your root component; its Render() returns Router
app.Run();
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

Put `UseAuthentication()` and `UseAuthorization()` before `MapRask<App>()` so the page and its live socket
see the signed-in user.

Guides: [getting started](https://rask.sh/docs/guides/getting-started) ·
[live pages](https://rask.sh/docs/guides/render-modes) · [routing](https://rask.sh/docs/guides/routing)
