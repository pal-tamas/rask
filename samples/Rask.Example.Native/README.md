# Rask.Example.Native

The **native** showcase host — the mobile sibling of `Rask.Example.Server` (ASP.NET) and
`Rask.Example.Wasm` (browser WASM). It mounts the **same** `Rask.Example.Shared.App` showcase every
other host mounts, but onto a [`NativeAppHost`](../../src/Rask.Native) that runs the app **in-process**
behind an `INativeWebView` bridge — the WebView-hybrid model (C# runs native, the view is a platform
WebView). See [`docs/native.md`](../../docs/native.md).

Unlike the other samples this is a plain `net10.0` library, not a runnable web app: on a real device
the app lives inside the `rask-native` template's iOS/Android app heads. This project is the shared
**composition root** — `NativeExampleHost.Create()` registers the showcase services
(`AddExampleServices`) and hands `Rask.Example.Shared.App` to `RunLocalAsync`, exactly as the template's
`AppDelegate`/`MainActivity` do.

## Running it on a device

Scaffold the template and point its app code at this showcase (or just use the template's own pages):

```bash
dotnet new rask-native -o MyApp
dotnet workload install ios android
dotnet build MyApp -t:Run -f net10.0-android   # or -f net10.0-ios
```

## How it's tested

`NativeExampleTests` (in `tests/Rask.Examples.E2E.Tests`) drives this host **headlessly, with no
emulator**: it runs the real `rask.native.js` client + `RunLocalAsync` pipeline in Chromium (the WebView
engine class Android ships) through a Playwright-backed `INativeWebView`
(`PlaywrightNativeWebView`), whose route handler (`NativeOriginServer`) serves the boot shell, the client,
scoped `/_rask/a/*` assets, `global.css`, and Bootstrap — the E2E stand-in for a device head's scheme
handler. It's a CI E2E shard alongside `ServerExampleTests` / `WasmExampleTests`, reusing the shared
showcase journey.

The `wwwroot/` here (global.css, `img/`, `data/`) mirrors the other hosts' static assets so the shared
showcase renders identically; it's served to the WebView by the host's asset handler (the E2E route
handler here, a scheme handler on device).
