# AGENTS.md — building this app with an AI assistant

This is a **Rask** native mobile app (iOS + Android) for .NET 10. Rask is the .NET One Person Framework —
one C# codebase, one server, every UI surface. This app is a **WebView hybrid**: your C#
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
  notch / status bar / home indicator — keep both together if you edit the shell. `App.cs` also declares the
  native header/footer bars (see "Native header & footer bars" below).
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
  (iOS `UIActivityViewController`, Android `Intent.ACTION_SEND`), `NativeGeolocation` for `IGeolocation`
  (iOS `CLLocationManager`, Android `LocationManager`), and `NativeNotifications` / `NativeBadge` for
  `INotifications` / `IBadge` (iOS `UNUserNotificationCenter`, Android `NotificationManager` + a badge
  notification) under `Platforms/`; register your own the same way. Geolocation needs the location
  permission and notifications need `POST_NOTIFICATIONS` on Android 33+ (both already in
  `AndroidManifest.xml` / `Info.plist`; `MainActivity` requests the runtime grants). Further native
  backends (biometrics, push) are a framework work-in-progress.

## Native header & footer bars
- A native page is a small **composed tree**: the native bars (`NativeHeaderBar` / `NativeTabBar` /
  `NativeToolbar`) as siblings of a **`NativeWebView`**, which hosts the ordinary page shell
  (`Doctype`/`Html`/`Head`/`Body`). `App.cs` shows the shape. The bars are ordinary factory-built components —
  compose them in `Render()`, they are not magic base-class slots.
- The native host projects the bars to **real platform bars** — a `UINavigationBar` + `UITabBar` on iOS, a top
  bar + bottom tab bar on Android — and serializes the `NativeWebView`'s HTML into the WebView between them.
  Build bars from `NativeBarButton` / `NativeTab` / `NativeBackButton` and type-safe `NativeIcon`s. A
  `NativeTab` also takes an optional `Badge` string (unread count) → `UITabBarItem.BadgeValue` / icon overlay.
  `NativeHeaderBar` takes optional `Segments` (shown in place of the title) → a `UISegmentedControl` / button
  row, controlled via `SelectedSegment` + `OnSegmentChanged(int)`. A `NativeMenuButton` bar item (with
  `NativeMenuItem` entries) opens a native overflow pull-down → `UIMenu` (iOS) / `PopupMenu` (Android). A
  `NativeBackButton` (header `Leading`) pops WebView history like hardware Back.
- **Style the bars** with `NativeColor` (the color sibling of `NativeIcon`: `Hex` / `Rgba` / `Adaptive(light,
  dark)` / `System`) — set `Background` / `Tint` / `TitleColor` per bar (`NativeTabBar` also `UnselectedTint`),
  or register an app-wide `NativeTheme` on `host.Services`. Per-bar wins, then the theme, then the platform
  default; an unset color keeps the OS look.
- **Opt-in wiring (already done in the heads):** host `webView.ChromeView` (not `webView.View`) and register
  the WebView head as `INativeChrome` on `host.Services` before `RunLocalAsync`. With no `INativeChrome`
  registered the bars are inert (they render nothing; the WebView fills the screen) — fully backward compatible.
- Tabs navigate their type-safe `To:` route; bar buttons run their `OnClick`. Put a native chrome component
  **inside** the HTML (an element child, or inside `NativeWebView`'s content) and you get **RASK032** — bars
  belong at the layout level, as siblings of `NativeWebView`.
- Sharing an app across web + native? Branch with `IsNative` / `IsServer` / `IsWasm` / `IsIOS` / `IsAndroid`
  (or `HostShell` / `HostEngine` / `HostPlatform`): compose the native tree under `IsNative`, return the plain
  shell otherwise.

## Build & run (needs the iOS/Android SDK workloads)
```bash
dotnet workload install ios android
dotnet build -t:Run -f net10.0-android     # emulator
dotnet build -t:Run -f net10.0-ios         # simulator (macOS + Xcode)
```

If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md
