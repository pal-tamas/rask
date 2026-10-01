# Rask.Spa.Hosting

**ASP.NET Core host for a Rask WebAssembly app** — the browser app written in C#, served by the same
server that answers its API. Part of [Rask](https://rask.sh/), a full-stack .NET web framework for teams
of any size.

- **One call serves the published bundle** with correct MIME types for the .NET runtime files, cache
  headers that follow what the publish guarantees (content-hashed assets cached hard, `index.html` never
  cached), precompressed siblings, and a fallback to `index.html` that still answers 404 for a missing asset.
- **Reference the client project and the host's build publishes it** into `wwwroot`, where `MapRaskSpa`
  finds it with no arguments.
- **Pairs with Rask.Cqrs.Server**, which answers the messages the browser app dispatches.
- Depends on nothing else in Rask.

## Install

```bash
dotnet add package Rask.Spa.Hosting
```

Or scaffold a host with its client: `rask new Shop --template wasm-hosted`.

## Use

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRaskSpaHost();

var app = builder.Build();
// Map your API endpoints FIRST — MapRaskSpa ends the pipeline with a fallback to index.html,
// so anything mapped after it is answered with HTML.
app.MapRaskSpa();
app.Run();
```

Guide: [Serving a WebAssembly app](https://rask.sh/docs/guides/deployment#serving-a-webassembly-app)
