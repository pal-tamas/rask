# Rask.Spa.Hosting

**ASP.NET Core host for a built single-page app** — a front end from any bundler (React, Preact,
Vue, Solid, Svelte, Lit, Angular) or a Rask WebAssembly app written in C#. Part of
[Rask](https://rask.sh/), a full-stack .NET web framework for teams of any size.

- **One call serves the build output** with correct MIME types, cache headers that follow what the build
  guarantees (content-hashed assets cached hard, `index.html` never cached), precompressed siblings, and a SPA
  fallback that still answers 404 for a missing asset.
- **A Rask WebAssembly app is a SPA too.** Reference the client project and the host's build publishes it;
  `MapRaskSpa` recognises the bundle and serves the runtime files with the right types and compression.
- **The build runs the front end's own toolchain.** `dotnet build` runs `npm ci` and `npm run build` in the
  `client` folder; `dotnet publish` copies the bundle into `wwwroot`.
- Depends on nothing else in Rask.

## Install

```bash
dotnet add package Rask.Spa.Hosting
```

Or start from a scaffold that already references it — a Rask host with the framework's TypeScript client
in `client/`: `rask new Shop --template react` (or `preact`, `vue`, `angular`, `solid`, `svelte`, `lit`).

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

Guide: [Single-page app front ends](https://rask.sh/docs/guides/spa)
