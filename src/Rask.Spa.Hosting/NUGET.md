# Rask.Spa.Hosting

**ASP.NET Core host for a built single-page app** — a TypeScript front end from any bundler (React, Preact,
Vue, Solid, Svelte, Lit, Angular) or a Rask WebAssembly app written in C#. Part of
[Rask](https://rask.sh/), a full-stack .NET web framework for teams of any size.

- **One call serves the build output** with correct MIME types, cache headers that follow what the build
  guarantees (content-hashed assets cached hard, `index.html` never cached), precompressed siblings, and a SPA
  fallback that still answers 404 for a missing asset.
- **A Rask WebAssembly app is a SPA too.** Reference the client project and the host's build publishes it;
  `UseRaskSpa` recognises the bundle and serves the runtime files with the right types and compression.
- **Pairs with Rask.Cqrs.Server**, which gives the client a typed JSON wire and generates its TypeScript
  from your C# message records.
- Depends on nothing else in Rask.

## Install

```bash
dotnet add package Rask.Spa.Hosting
```

Or scaffold a host with its client: `rask new Shop --template react` (or `preact`, `vue`, `solid`, `svelte`,
`lit`, `angular`, `wasm-hosted`).

## Use

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRaskSpaHost();

var app = builder.Build();
// Map your API endpoints FIRST — UseRaskSpa ends the pipeline with a fallback to index.html,
// so anything mapped after it is answered with HTML.
app.UseRaskSpa();
app.Run();
```

Guide: [TypeScript front ends](https://rask.sh/docs/guides/spa)
