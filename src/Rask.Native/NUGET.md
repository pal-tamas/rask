# Rask.Native

**Native mobile host for [Rask](https://github.com/pal-tamas/rask) *(preview)*.** Ship the *same* C#
component code that runs on the Rask Server and in the browser as a real, store-distributable
**iOS/Android app**. It's a **WebView hybrid**: your C# runs natively on the device, and the UI
renders in a platform WebView driven by Rask's live diff pipeline — the same render → diff → payload
path (`LiveSessionBase`) the Server and WASM hosts use.

The library is transport-agnostic and targets plain `net10.0` (no iOS/Android workloads to build/test);
the platform WebView lives behind the `INativeWebView` bridge, implemented per platform in the app
head. Includes `Rask.Core` and the Rask source generators.

## Get started

The `rask` CLI's native template scaffolds both platform heads for you:

```bash
dotnet tool install -g Rask.Cli
rask new MyApp --template native
cd MyApp

dotnet workload install ios android          # the iOS/Android SDK workloads (one-time)
dotnet build -t:Run -f net10.0-android       # Android emulator
dotnet build -t:Run -f net10.0-ios           # iOS simulator (macOS + Xcode)
```

To add the host to an existing app head instead:

```bash
dotnet add package Rask.Native
```

## Use

```csharp
using Rask.Native;

// Native + Local — the app runs in-process on the device (offline, store-distributable).
var host = NativeAppHost.CreateDefault();
// host.Services.AddSingleton<IMyService, MyService>();   // register app services before RunLocalAsync
NativeApp app = await host.RunLocalAsync<App>(webView);   // webView: your INativeWebView

// Native + Server — the WebView is a thin native shell over a remote Rask Server (wss://).
NativeServerShell shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));
```

## Notes

- **Preview / pre-1.0** — the host + template + iOS/Android heads run end-to-end; APIs may still shift.
  **Native device *backends*** have started landing: the OS share sheet (`IShare`) has a native
  `UIActivityViewController` / `Intent.ACTION_SEND` head backend (registered before `RunLocalAsync`,
  overriding the JS default); native geolocation/push/biometrics follow behind the same seam.
- **WebView hybrid by default** — C# runs native, the view is a WebView (same architecture as MAUI Blazor
  Hybrid / Capacitor). What it buys over a PWA: App Store / Play Store distribution, native device APIs,
  and real background execution.
- **Pure-native screens, per route** — `NativeScreen` replaces `NativeWebView` on the routes you want fully
  native, and the twelve-component view family inside it (`NativeStack`, `NativeLabel`, `NativeButton`,
  `NativeTextField`, `NativeSwitch`, `NativeList`, …) describes a real `UIView`/`android.view.View` tree
  instead of HTML. One app mixes both — a tab bar can hold a web page and a native screen — and neither
  surface is torn down when switching, so returning to a web route does not reload it. `Router` works
  inside a screen unchanged. Every callback has an awaited `OnXAsync` form.
  *The iOS/Android surface backends are not implemented yet, so a `NativeScreen` currently paints nothing
  on a device; register no `INativeSurface` and the family stays inert.*
- **Native header / tab / tool bars** — compose `NativeHeaderBar` / `NativeTabBar` / `NativeToolbar` as
  siblings of a `NativeWebView` and they project to real `UINavigationBar`/`UITabBar` (iOS) and platform bars
  (Android). Style them with type-safe, dark-mode-aware `NativeColor` (background / tint / title color) per
  bar or via an app-wide `NativeTheme`; unset colors keep the platform default.
- The typed `Rask.Core.Browser` device wrappers (`IGeolocation`, `IClipboard`, `IVibration`, …) work
  through the WebView's JS engine with no extra code.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/native.md>
