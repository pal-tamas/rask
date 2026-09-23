# Rask.Meta.Hosting

**ASP.NET Core host for a meta framework front end** — Nuxt, TanStack Start, SolidStart, SvelteKit, Analog
or Next.js — running beside your C# in **one container on one port**. Part of [Rask](https://rask.sh/), a
full-stack .NET web framework for teams of any size.

- **Kestrel owns the public port** and answers `/_rask` itself; the framework's own Node server runs as a
  supervised child on loopback, so publishing the container cannot expose it.
- **Everything else is forwarded** to that Node server, with WebSocket upgrades and unbuffered streaming
  intact.
- **Pairs with Rask.Cqrs.Server**, which gives server-side rendering a typed wire back into your C#.
- Distinct from Rask.Spa.Hosting, which serves a static bundle and needs no Node at runtime.

## Install

```bash
dotnet add package Rask.Meta.Hosting
```

Or scaffold the whole thing: `rask new Shop --template nuxt` (or `nextjs`, `sveltekit`, `solidstart`,
`tanstack-start`, `analog`).

## Use

The framework is named once, in the project file:

```xml
<RaskMetaFramework>nuxt</RaskMetaFramework>
```

```csharp
// Program.cs
builder.Services.AddRaskMeta();

var app = builder.Build();
// Map your API endpoints FIRST — UseRaskMeta ends the pipeline with a fallback that forwards
// everything it has not answered to the framework.
app.UseRaskMeta();
app.Run();
```

Guide: [Meta framework front ends](https://rask.sh/docs/guides/meta)
