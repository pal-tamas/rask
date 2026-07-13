<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/rask-logo-dark.svg">
  <img alt="Rask" src="assets/rask-logo.svg" width="300">
</picture>

### Live apps in C#. One codebase — server-rendered over WebSockets, client-side in the browser via WebAssembly, or a native iOS/Android app.

[![NuGet Rask.Server](https://img.shields.io/nuget/v/Rask.Server.svg?label=Rask.Server)](https://www.nuget.org/packages/Rask.Server)
[![NuGet Rask.Wasm](https://img.shields.io/nuget/v/Rask.Wasm.svg?label=Rask.Wasm)](https://www.nuget.org/packages/Rask.Wasm)
[![NuGet Rask.Native](https://img.shields.io/nuget/v/Rask.Native.svg?label=Rask.Native)](https://www.nuget.org/packages/Rask.Native)
[![NuGet Rask.Templates](https://img.shields.io/nuget/v/Rask.Templates.svg?label=Rask.Templates)](https://www.nuget.org/packages/Rask.Templates)
[![NuGet Rask.Bootstrap](https://img.shields.io/nuget/v/Rask.Bootstrap.svg?label=Rask.Bootstrap)](https://www.nuget.org/packages/Rask.Bootstrap)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)

# ▶ **[Try the live demo ↗](https://pal-tamas.github.io/rask/)** &nbsp;·&nbsp; 🛝 **[Playground ↗](https://pal-tamas.github.io/rask/playground/)** &nbsp;·&nbsp; 📖 **[Read the docs ↗](docs/)** &nbsp;·&nbsp; 🧪 **[Browse the examples ↗](samples/)**

</div>

---

Write components as plain C# classes. Return a tree of HTML from `Render()`. **No `.razor`, no JSX, no JavaScript to
write** — and the *same* component code runs server-rendered with live WebSocket updates or fully client-side on
WebAssembly.

```csharp
[Route("/counter")]
public sealed class Counter : Component
{
    private int _count;

    protected override Component? Render() =>
    [
        H1()["Counter"],
        P()[$"Current count: {_count}"],
        Button(OnClick: () => _count++)["Click me"]
    ];
}
```

<sub>☝️ A complete, live, interactive component — routing, state, and event handling in a single C# class.
**[See it running, and dozens more, in the live demo ↗](https://pal-tamas.github.io/rask/)**</sub>

---

<div align="center">

## 📱 Build mobile apps in C# — no Swift, Kotlin, React Native, or MAUI

**The same component above ships as an installable, offline mobile app.** A Rask **WASM** app is a
Progressive Web App: it **installs to the home screen**, **launches full-screen**, **works offline**,
sends **push notifications**, badges its **app icon**, keeps the **screen awake**, and reaches the
device — **vibration, share sheet, geolocation, clipboard, orientation** — through typed C#.

```bash
dotnet new rask-wasm --pwa     # → an installable, offline PWA, ready to deploy
```

**[📖 Build mobile apps with Rask →](docs/pwa.md)**  ·  **[Try the installable demo ↗](https://pal-tamas.github.io/rask/)**

Going further than a PWA? **`Rask.Native`** *(preview)* ships the *same* component code as a real
**native iOS/Android app** for App Store / Play Store distribution — a WebView hybrid where your C#
runs natively on the device. Scaffold it with `dotnet new rask-native` and run on an emulator with
`dotnet build -t:Run -f net10.0-android` — or run the full in-repo showcase, the native peers of the
Server/WASM samples: `samples/Rask.Example.Native` (in-process) and `samples/Rask.Example.Native.Server`.

**[📱 Native mobile with Rask →](docs/native.md)**

</div>

---

## ✨ Why Rask

I've spent 15+ years building full-stack .NET apps, and the front end always meant a second world — another language,
type system, and build chain, with a serialization seam in the middle. Blazor answers part of that, but `.razor` puts
markup and code back in one file with its own templating dialect. So Rask is built around a single conviction: **a
component is just a C# class that returns a tree.** No `.razor`, no JSX, no template language — `Div(...)[Span(...), "hi"]`
is plain, refactor-safe, IDE-native C#, and the *same* component runs server-rendered over a WebSocket or fully
client-side on WebAssembly. One codebase; pick the host per project.

It also treats the network as the real bottleneck: after first paint, a state change ships a minimal diff — a counter
tick on a 24 KB page goes out as **~41 bytes**, not 24 KB. In a 26-scenario head-to-head, Rask ships **fewer bytes on
the wire than Blazor on every one** (typically 2–5×, up to 66×) and, since the diff path stopped materialising the page
per update, allocates **~40× less per update** too. It even holds a **~30% leaner *retained* tree per mounted page** —
a pure-element page component keeps a compact frame snapshot instead of an object-per-element graph — so Rask now leads
on every measured axis. The full byte-for-byte numbers are in the
**[Rask vs Blazor baselines ↗](benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md)**.

*Rask* is the Norwegian/Danish/Swedish word for **fast**. **The [docs ↗](docs/) and the [live demo ↗](https://pal-tamas.github.io/rask/)
are the real tour — this README is just the front door.**

## 📦 Install

> **Prerequisites:** the **.NET 10 SDK** (`dotnet --version` ≥ `10.0`); the `wasm-tools` workload
> (`dotnet workload install wasm-tools`) for the WASM templates, or the `ios android` workloads
> (`dotnet workload install ios android`) for the native template. New to Rask? The
> **[getting started guide](docs/getting-started.md)** walks the whole path end to end.

### Scaffold a new project (recommended)

```bash
dotnet new install Rask.Templates

dotnet new rask-server       -n MyApp    # ASP.NET live-server app
dotnet new rask-wasm         -n MyApp    # standalone browser-WASM SPA
dotnet new rask-wasm-hosted  -n MyApp    # browser-WASM client + ASP.NET host
dotnet new rask-native       -n MyApp    # native iOS + Android app (WebView hybrid, preview)

cd MyApp && dotnet run                    # that's it (native: dotnet build -t:Run -f net10.0-android)
```

Add `--auth` for a cookie/JWT-wired starter, or `--pwa` (WASM) for an installable offline app.

### Add packages to an existing project

Pick one host package per project, then add opt-in packages as needed:

| Package                            | Project type                                                        | Entry-point API                                             |
|------------------------------------|---------------------------------------------------------------------|-------------------------------------------------------------|
| `Rask.Server`                      | `net10.0` ASP.NET                                                   | `services.AddRask()` + `app.UseRask<TApp>()`                |
| `Rask.Wasm`                        | `net10.0-browser`                                                   | `WasmHostBuilder.CreateDefault()` + `host.RunAsync<TApp>()` |
| `Rask.Wasm.Hosting`                | `net10.0` ASP.NET (with a `<ProjectReference>` to the WASM project) | `app.UseRask()`                                             |
| `Rask.Native` *(preview)*          | `net10.0-ios;net10.0-android` app head                             | `NativeAppHost.CreateDefault()` + `host.RunLocalAsync<TApp>(webView)` |
| `Rask.Validation.DataAnnotations`  | any host that hosts your forms                                      | drop `DataAnnotationsValidator()` inside a `Form<T>`        |
| `Rask.Validation.FluentValidation` | any host that hosts your forms                                      | drop `FluentValidationValidator(new MyValidator())` inside  |
| `Rask.Bootstrap`                   | any host with your components                                       | link `BootstrapStyles()` in `Head`, then use `Bs*` factories |
| `Rask.WebPush`                     | any backend (Server app or a WASM PWA's ASP.NET host)              | `services.AddRaskWebPush(...)` + inject `IWebPushSender`     |
| `Rask.Cqrs`                        | any .NET app (standalone; Server, WASM, or non-Rask)               | `services.AddRaskCqrs()` + inject `IDispatcher`             |
| `Rask.Testing`                     | your `*.Tests` project (references your app)                       | `RaskTest.Render(new MyComponent())` → assert on `.Html`    |

`Rask.Server`, `Rask.Wasm`, and `Rask.Native` pull in `Rask.Core` and the source generators transitively. Full setup,
host trade-offs, and sub-path hosting are covered in **[getting started](docs/getting-started.md)** and the **[docs ↗](docs/)**.

## 🧪 Examples

**The fastest way to understand Rask is to click through a real app and read its source.**

- **[Live demo ↗](https://pal-tamas.github.io/rask/)** — `Rask.Example.Wasm` is published to GitHub Pages on every push
  to `main`; click through a full multi-page Rask app in the browser before cloning anything.
- **[Playground ↗](https://pal-tamas.github.io/rask/playground/)** — write Rask component C# in the browser and see it
  compile & render live (Roslyn runs in WebAssembly, no server), with the framework's diagnostics inline. See
  [docs/playground.md](docs/playground.md).
- **[`samples/`](samples/)** — runnable showcase apps that exercise every feature end-to-end: the shared feature pages
  (`samples/Rask.Example.Shared/Features/`), EF Core + SQLite data access, and one auth sample per cell of the
  `{Cookie, JWT} × {Server, WASM}` matrix. Run one with, e.g.,
  `dotnet run --project samples/Rask.Example.Server` and open the printed URL.

## 📚 Documentation

Everything lives in **[`docs/`](docs/)** — start here, then dive into the topic you need:

| Guide | What it covers |
|-------|----------------|
| **[Getting started](docs/getting-started.md)** | Scaffold, first component, interactivity, routing — the end-to-end path. |
| **[Best practices](docs/best-practices.md)** | The patterns and pitfalls that keep an app correct, secure, and fast. |
| **[Elements & the DSL](docs/elements.md)** | Primitives, tag factories, universal props, and typed SVG — the render surface. |
| **[Composition](docs/composition.md)** · **[Lifecycle](docs/lifecycle.md)** | Component tiers (static/stateless/stateful), context, callbacks, children; mount/update/dispose. |
| **[Routing](docs/routing.md)** · **[Forms & validation](docs/forms.md)** · **[Building form controls](docs/building-form-controls.md)** | URLs, route params, the form pipeline, custom `IFormControl<T>` inputs. |
| **[Authentication](docs/authentication.md)** · **[Data access](docs/data-access.md)** · **[HTTP & files](docs/http-and-files.md)** · **[CQRS](docs/cqrs.md)** | Cookie/JWT on Server & WASM; EF Core + SQLite; a DI'd `HttpClient` + file upload/download; source-generated CQRS. |
| **[Bootstrap](docs/bootstrap.md)** | Typed Bootstrap 5.3 components, zero-JS interactivity, typed utility classes. |
| **[Browser APIs](docs/browser-apis.md)** · **[PWA](docs/pwa.md)** · **[Native mobile](docs/native.md)** · **[AOT](docs/aot.md)** | 43 typed Web-API wrappers; installable offline PWAs; native iOS/Android apps; opt-in full WASM AOT. |
| **[JS interop](docs/js-interop.md)** · **[Accessibility](docs/accessibility.md)** · **[Testing](docs/testing.md)** | Scoped JS + element refs; a11y; unit + E2E. |
| **[Migrating from Blazor](docs/migration-from-blazor.md)** | How the day-to-day differs, side by side. |
| **[Diagnostics](docs/diagnostics.md)** | Every RASK build error/warning and its fix. |
| **[Live rendering & the diff codec](docs/architecture/live-rendering.md)** | How the runtime works under the hood. |

## 📋 Status

Rask is pre-1.0. APIs may change between minor versions. It targets **.NET 10** (`net10.0` for ASP.NET hosts,
`net10.0-browser` for WASM, `net10.0-ios;net10.0-android` for native app heads). Unit suites cover the core,
generators, hosts (Server, WASM, Native), and validation packages, plus a Playwright E2E smoke suite;
`Rask.Example.Wasm` publishes with zero IL trimming warnings. The native host is preview-stage. Production use at your
own discretion — issues and PRs welcome.

## 📄 License

Rask is released under the [MIT License](LICENSE).

---

<div align="center">

⚡ **Rask** — *Norwegian/Danish/Swedish for "fast".*

**[Live demo ↗](https://pal-tamas.github.io/rask/)** · **[Docs ↗](docs/)** · **[Examples ↗](samples/)** · **[NuGet ↗](https://www.nuget.org/packages/Rask.Server)**

Built with .NET 10. Issues and PRs welcome.

</div>
