# Rask.Spa.Hosting

**ASP.NET Core host for a Rask WebAssembly app** written in C# — or any other built single-page app.
Part of [Rask](https://rask.sh/), a full-stack .NET web framework for teams of any size.

- **One call serves the build output** with correct MIME types, cache headers that follow what the build
  guarantees (content-hashed assets cached hard, `index.html` never cached), precompressed siblings, and a SPA
  fallback that still answers 404 for a missing asset.
- **The WebAssembly client is published by the host's build.** Reference the client project and `dotnet build`
  publishes it; `MapRaskSpa` recognises the bundle and serves the runtime files with the right types and
  compression. Under `rask dev` the client's build output is served instead, so hot reload reaches the browser.
- Depends on nothing else in Rask.

## Install

```bash
dotnet add package Rask.Spa.Hosting
```

Or start from a scaffold that already references it: `rask new Shop --template wasm-hosted`.

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

Guide: [Single-page apps](https://rask.sh/docs/guides/spa)
