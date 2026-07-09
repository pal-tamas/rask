# 📱 Native mobile apps with Rask (`Rask.Native`)

**Ship real, store-distributable iOS/Android apps from the same Rask component code — no Swift, Kotlin,
React Native, or MAUI.** `Rask.Native` runs your app on the device inside a platform WebView, driven by
the *same* render → diff pipeline as the Server and WASM hosts. Your C# runs **natively** on the device
(App Store / Play Store distribution, native device APIs, real background execution); only the *view* is
a WebView. Every existing Rask component — `Div()[Span(), …]`, forms, routing, scoped CSS/JS — works
unchanged.

> **Status — preview / pre-1.0.** The host, the `dotnet new rask-native` template (with the iOS
> `WKWebView` and Android `WebView` app heads), and the native client runtime **ship and run
> end-to-end on both platforms** — a scaffolded app boots, renders the component tree over the native
> bridge, routes, and updates live (see [Roadmap](#roadmap) for the verification detail). It's still
> pre-1.0: APIs may shift, and **native device *backends*** (native geolocation/push/biometrics behind
> the browser-API interfaces) plus full client-parity are the remaining follow-ups.

- [How it fits](#how-it-fits)
- [Get started](#get-started)
- [Safe-area insets (notch / status bar)](#safe-area-insets-notch--status-bar)
- [Two modes: Local and Server](#two-modes-local-and-server)
- [The `INativeWebView` bridge](#the-inativewebview-bridge)
- [Wiring a platform head](#wiring-a-platform-head)
- [Device capabilities](#device-capabilities)
- [Honest framing](#honest-framing)
- [Roadmap](#roadmap)

---

## How it fits

Rask has three client "dialects" that all speak one frame contract — a minimal diff (or full-HTML morph)
the client applies to the DOM:

| Host | Transport | Where the app runs |
| --- | --- | --- |
| `Rask.Server` | WebSocket | on the server |
| `Rask.Wasm` | in-process (JSImport) | in the browser (WASM) |
| **`Rask.Native`** | **in-process (WebView bridge)** | **natively on the device** |

The render → diff → payload pipeline lives in `LiveSessionBase` (Rask.Core) and is shared by all three.
`Rask.Native` adds only the native transport: `NativeLiveSession` pushes each frame to a platform WebView,
and turns WebView events back into handler/navigate dispatches — structurally a mirror of
`WasmLiveSession`. Because the C# host is transport-agnostic, the `Rask.Native` library targets plain
`net10.0` and builds/tests with **no iOS/Android SDK workloads**; the WebView itself is abstracted behind
[`INativeWebView`](#the-inativewebview-bridge), implemented per platform in the app head.

## Get started

Scaffold a native app from the template, then run it on an emulator/simulator:

```bash
dotnet new install Rask.Templates
dotnet new rask-native -n MyApp
cd MyApp

dotnet workload install ios android          # the iOS/Android SDK workloads (one-time)
dotnet build -t:Run -f net10.0-android       # Android emulator
dotnet build -t:Run -f net10.0-ios           # iOS simulator (macOS + Xcode)
```

`dotnet new rask-native` scaffolds a project that multi-targets `net10.0-ios;net10.0-android`:

```
MyApp.csproj                  # multi-targets net10.0-ios;net10.0-android; refs Rask.Native
App.cs, HomePage.cs, Counter.cs   # your Rask components — shared across both platforms
Platforms/
  iOS/       AppDelegate.cs · RaskWkWebView.cs (INativeWebView over WKWebView) · Info.plist
  Android/   MainActivity.cs · RaskAndroidWebView.cs (INativeWebView over WebView) · AndroidManifest.xml
```

The shared components (`App.cs` and your pages) are ordinary Rask components — identical in shape to
any other host. Only the two `Platforms/…` heads are platform-specific; each boots a `NativeAppHost`,
calls `RunLocalAsync<App>(webView)`, and provides the WebView bridge.

Two ordering rules the generated heads already follow — keep them if you edit a head:

- **Register app services on `host.Services` *before* `RunLocalAsync`.** `RunLocalAsync` builds the DI
  provider, so registrations made after it won't take effect.
- **Wire the session *before* loading the shell.** The first render fires when the WebView's client
  posts its `ready` message, so `RunLocalAsync<App>(webView)` must run before the head loads the boot
  shell — otherwise the shell load races the handshake.

## Safe-area insets (notch / status bar)

The boot shell requests an **edge-to-edge viewport** (`viewport-fit=cover`), so without padding the
UI would render *under* the status bar, notch / Dynamic Island, and home indicator. The template's
`App.cs` pads `Body` by the device safe-area insets so content always clears them:

```csharp
protected override Component? Head =>
[
    Title()["Rask App"],
    Meta("utf-8"),
    Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover")
];

protected override Component? Render() =>
[
    Doctype(),
    Html("en")[
        Head(),
        // Pad the body by the device safe-area insets so content clears the status bar / notch /
        // home indicator (the boot shell requests an edge-to-edge viewport with viewport-fit=cover).
        Body(Style: "margin:0;padding:env(safe-area-inset-top) env(safe-area-inset-right) " +
                    "env(safe-area-inset-bottom) env(safe-area-inset-left)")[
            /* nav, router, … */
        ]
    ];
```

If you restructure `App.cs`, keep the `viewport-fit=cover` meta and the `env(safe-area-inset-*)`
padding together — dropping either brings content back under the notch.

## Two modes: Local and Server

```csharp
using Rask.Native;

// Native + Local — the app runs in-process on the device (offline, store-distributable).
var host = NativeAppHost.CreateDefault();
// host.Services.AddSingleton<IMyService, MyService>();   // register app services
NativeApp app = await host.RunLocalAsync<App>(webView);   // webView: your INativeWebView

// Native + Server — the WebView is a thin native shell over a remote Rask Server (wss://).
NativeServerShell shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));
// the platform head navigates its WebView to shell.ServerBaseUrl; the server serves its own client.
```

- **Local** is the offline native app: the whole app (routing, state, handlers) runs on-device; navigations
  and state changes never touch the network. `RunLocalAsync` builds the DI container, wraps your `App` in a
  `RootErrorBoundary`, seeds the route, and wires the WebView. The first render fires when the WebView's
  client posts its `ready` message, so it's safe to call before the WebView finishes loading.
- **Server** makes the device a native, store-distributable shell over a server-driven app — the same
  `Rask.Server` app, now installable with native device APIs available to the page.

## The `INativeWebView` bridge

The one thing `Rask.Native` does *not* contain is the platform WebView. The app head implements this
contract over the concrete control:

```csharp
public interface INativeWebView
{
    ValueTask ApplyRenderAsync(ReadOnlyMemory<byte> frameUtf8);   // .NET → WebView: push a rendered frame
    ValueTask EvaluateJavaScriptAsync(string javaScript);         // .NET → WebView: IJSRuntime interop
    Func<byte[], Task>? OnMessage { get; set; }                   // WebView → .NET: events / jsResult / …
}
```

`ApplyRenderAsync` hands a frame (UTF-8 JSON) to the WebView's `window.__raskNative.applyRender`.
`OnMessage` is invoked by the platform whenever the page posts back (a component event, a `navigate`, a
`ready` handshake, an IJSRuntime `jsResult`). Both sides speak the same wire format the WASM host uses over
its `Dispatch` boundary. **Implementations must marshal `ApplyRenderAsync`/`EvaluateJavaScriptAsync` onto
the platform UI thread** (WebView JS evaluation is UI-thread-affine); the render pipeline runs off it.

## Wiring a platform head

The app head (a `net10.0-ios` / `net10.0-android` project) serves the boot shell + client and bridges the
WebView. Serve the assets from a **real origin** so secure-context device APIs (`localStorage`,
`crypto.subtle`) work — a `WKURLSchemeHandler` (`app://…`) on iOS, a `WebViewAssetLoader`
(`https://appassets.androidplatform.net/…`) on Android — not `LoadHtmlString`/`loadData` (opaque origin).
The two assets come from the library:

```csharp
string shell = NativeClientAssets.IndexHtml;   // the boot shell (loads rask.native.js)
string client = NativeClientAssets.ClientJs;    // the spliced native client runtime
```

Inject `window.__raskSend` at document start (it forwards a JSON string to your script-message handler),
point that handler at `INativeWebView.OnMessage`, and implement `ApplyRenderAsync` by evaluating
`window.__raskNative.applyRender(<json>)`. Sketch:

- **iOS (`WKWebView`)** — a `WKUserScript` (atDocumentStart) defines `window.__raskSend = s =>
  window.webkit.messageHandlers.rask.postMessage(s)`; a `WKScriptMessageHandler` forwards to `OnMessage`;
  `ApplyRenderAsync` calls `EvaluateJavaScript`.
- **Android (`WebView`)** — `addJavascriptInterface` exposes a `@JavascriptInterface dispatch(String)`
  that forwards to `OnMessage`; an injected `window.__raskSend` calls it; `ApplyRenderAsync` calls
  `evaluateJavascript` on the UI thread.

## Device capabilities

The 27 `IJSRuntime`-backed browser wrappers in `Rask.Core.Browser` (`IGeolocation`, `IClipboard`,
`IVibration`, storage, notifications, badge, wake lock, …) work **through the WebView's JS engine** with
no extra code — `NativeAppHost` registers them and `NativeJSRuntime` dispatches them over the bridge.
**Native C# backends** (P/Invoke to CoreLocation / Android APIs, native share sheet, biometrics, and
native push via APNs/FCM) behind the *same* interfaces — plus new native-only capabilities — are a
follow-up (see [Roadmap](#roadmap)).

## Honest framing

This is a **WebView hybrid** (the same architecture as .NET MAUI Blazor Hybrid, Ionic/Capacitor): C# runs
natively, the view is a WebView. It is **not** a native-control renderer — Rask components render HTML, and
that HTML renders in a WebView. A true native-control renderer (mapping the component tree to UIKit/Android
views) would require a parallel non-HTML component library and is out of scope. What the hybrid buys over
the [PWA story](pwa.md): App Store / Play Store distribution, native device APIs beyond the browser
sandbox, and real background execution — without giving up "the same component runs everywhere".

## Roadmap

1. ✅ **Foundation** — `NativeAppHost` / `NativeLiveSession` / `NativeJSRuntime`, the `INativeWebView`
   bridge, the `rask.native.js` client dialect + boot shell, unit-tested on `net10.0`.
2. ✅ **Platform heads + template** — the `rask-native` template ships `WKWebView` (iOS) and `WebView`
   (Android) implementations of `INativeWebView`, with custom-scheme / asset-loader serving and the
   UI-thread bridge. **Verified end-to-end on both platforms**: the app boots, serves the shell + client
   from the app origin, renders the component tree over the native bridge, routes, and updates live (the
   Counter increments via a diff on click). Android: a signed APK on the emulator (verified by screenshot).
   iOS: on the simulator, verified by reading `document.body.innerText` and dispatching a click — `simctl`
   screenshots don't capture `WKWebView`'s out-of-process content, so the DOM is inspected directly.
3. **Client parity** — lift the transport-neutral DOM helpers (rAF input/scroll coalescing, keyboard/drag/
   file events, scoped-CSS FOUC gating, scoped-JS invoke gating) shared with `rask.wasm.js` into a common
   module so the native client reaches full parity instead of re-copying them.
4. **Showcase sample** — a `Rask.Example.Native` app under `samples/` that exercises the feature pages
   end-to-end (Local + Server heads), so native is covered by the same showcase + E2E net as the other hosts.
5. **Native device backends** — CoreLocation/Android geolocation, native share, biometrics, native push,
   behind the existing `Rask.Core.Browser` interfaces + new native-only ones.
