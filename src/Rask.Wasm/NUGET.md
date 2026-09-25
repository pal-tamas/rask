# Rask.Wasm

The **browser runtime** for [Rask](https://rask.sh/), a full-stack .NET web framework for teams of any
size. It runs a Rask app's C# components client-side on .NET WebAssembly — the same components, chain
markup and router as the server host — and publishes to a static folder any web server or CDN can serve.

- Ships the JS boot scripts, the page shell, and the MSBuild integration that stages them into the
  published bundle.
- Depends on [`Rask`](https://www.nuget.org/packages/Rask), the shared core (components, the chain, routing,
  forms, scoped CSS/TypeScript, the generators), and brings the trim-safe client batteries on by default:
  the mediator and query cache, remote dispatch, accounts, validation and the `Rask.Ui` kit.
  `host.Configure(c => c.Query.Off())` is how an app does without one.
- Opt in with `<RaskPrerender>true</RaskPrerender>` and every route is prerendered to real HTML at publish,
  with a `sitemap.xml`, so visitors and crawlers get the page instead of a boot spinner.

## Install

```bash
dotnet add package Rask.Wasm
```

Or scaffold one with the CLI — `rask new Shop --template wasm` (a static site) or `--template wasm-hosted`
(a browser app with an ASP.NET host beside it).

## Use

```csharp
// Program.cs
var host = WasmHostBuilder.CreateDefault();
await host.RunAsync<App>();   // App is your root component; its Render() returns Router
```

```csharp
[Route("/")]
public sealed partial class Home : Component
{
    int _count;

    protected override Component? Render() =>
        Div[
            H1["Hello from WebAssembly"],
            Button.OnClick(() => _count++)[$"Clicked {_count} times"]
        ];
}
```

The WebAssembly build tooling is a one-time install: `dotnet workload install wasm-tools`.

Guides: [getting started](https://rask.sh/docs/guides/getting-started) ·
[prerendering](https://rask.sh/docs/guides/prerendering) · [Mobile & PWA](https://rask.sh/docs/guides/pwa)
