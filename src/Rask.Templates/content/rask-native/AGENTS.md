# AGENTS.md — building this app with an AI assistant

This is a **Rask** native mobile app (iOS + Android) for .NET 10. It's a **WebView hybrid**: your C#
runs natively on the device via `Rask.Native`, and the UI renders in a platform WebView driven by Rask's
live diff pipeline. The **same component code** as any other Rask host works here. Full docs:
https://github.com/pal-tamas/rask/tree/main/docs — native specifics: docs/native.md.

## Mental model
- Components are **plain C# classes** deriving from `Component`. Override `Component? Render()` and return
  a tree built with **generated factory methods** — no `.razor`, no JSX. Use factories, never `new`
  (RASK014). Children go through the indexer: `Div()[Span()["hi"], "text"]`. Props are factory params
  (nullable ⇒ optional, non-nullable no-initializer ⇒ required).
- A page/root component must render the **full shell** `[Doctype(), Html(...)[Head(...), Body(...)]]`
  (RASK021). The framework injects its runtime automatically.
- Route with `[Route("/path")]`; navigate only from event handlers via the injected `Navigator`. Inject
  services (`HttpClient`, `IJSRuntime`, the typed `Rask.Core.Browser` APIs, your own) through the ctor.

## Native structure — don't restructure these
- **Shared components** (`App.cs`, `HomePage.cs`, `Counter.cs`) compile for both `net10.0-ios` and
  `net10.0-android`. Keep platform-specific types OUT of them. `App.cs` pads `Body` by
  `env(safe-area-inset-*)` (paired with the `viewport-fit=cover` viewport meta) so content clears the
  notch / status bar / home indicator — keep both together if you edit the shell.
- **Platform heads** live under `Platforms/iOS/` and `Platforms/Android/`. Each boots a
  `NativeAppHost`, calls `host.RunLocalAsync<App>(webView)`, and provides the `INativeWebView`
  implementation for its WebView (`WKWebView` on iOS, `android.webkit.WebView` on Android). Register app
  services on `host.Services` in the head's `StartAsync` before `RunLocalAsync`.
- **Two modes:** `RunLocalAsync<App>(webView)` runs the app in-process (offline). To be a shell over a
  remote Rask Server instead, `NativeAppHost.ConnectToServer(uri)` and load that URL in the WebView.

## Device APIs
- Inject the typed `Rask.Core.Browser` wrappers (`IGeolocation`, `IClipboard`, `IVibration`,
  `IBrowserStorage`, `INotifications`, `IBadge`, `IWakeLock`, …) — they work through the WebView's JS engine.
- Sharing: use the headless `Shareable` (`Rask.Core`) to attach share behaviour to your own element, or
  inject `IShare` (`Rask.Client.Browser`) to share from code. Both hit the OS share sheet.
- **Native backends** override a JS default with real platform code. The head registers one on
  `host.Services` **before `RunLocalAsync`** (last-wins). The template ships `NativeShare` for `IShare`
  (iOS `UIActivityViewController`, Android `Intent.ACTION_SEND`) under `Platforms/`; register your own the
  same way. Further native backends (geolocation, biometrics, push) are a framework work-in-progress.

## Build & run (needs the iOS/Android SDK workloads)
```bash
dotnet workload install ios android
dotnet build -t:Run -f net10.0-android     # emulator
dotnet build -t:Run -f net10.0-ios         # simulator (macOS + Xcode)
```

If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md
