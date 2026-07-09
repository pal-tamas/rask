# 📱 Native mobile apps with Rask (`Rask.Native`)

**Ship real, store-distributable iOS/Android apps from the same Rask component code — no Swift, Kotlin,
React Native, or MAUI.** `Rask.Native` runs your app on the device inside a platform WebView, driven by
the *same* render → diff pipeline as the Server and WASM hosts. Your C# runs **natively** on the device
(App Store / Play Store distribution, native device APIs, real background execution); only the *view* is
a WebView. Every existing Rask component — `Div()[Span(), …]`, forms, routing, scoped CSS/JS — works
unchanged.

> **Status.** This page documents the **foundation** shipped in `Rask.Native`: the transport-agnostic
> host, the `INativeWebView` bridge, and the native client runtime, all unit-tested on `net10.0`. The
> platform app heads (`WKWebView` / Android `WebView`), the `dotnet new rask-native` template, the sample,
> and native device-API backends are **in-progress follow-ups** (see [Roadmap](#roadmap)). If you're
> here to understand the design or start a platform head, read on.

- [How it fits](#how-it-fits)
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
   UI-thread bridge. **Verified end-to-end on Android**: a signed APK boots the app in the emulator,
   renders the component tree over the native bridge, routes, and updates live (the Counter increments via
   a diff on tap). The iOS head compiles against the Microsoft.iOS bindings; a full iOS build/simulator run
   awaits an Xcode-provisioned environment.
3. **Client parity** — lift the transport-neutral DOM helpers (rAF input/scroll coalescing, keyboard/drag/
   file events, scoped-CSS FOUC gating, scoped-JS invoke gating) shared with `rask.wasm.js` into a common
   module so the native client reaches full parity instead of re-copying them.
4. **`dotnet new rask-native` template + sample** — an app head multi-targeting `net10.0-ios;net10.0-android`
   that builds an `.ipa`/`.apk` from shared components (`Rask.Example.Native`, Local + Server heads).
5. **Native device backends** — CoreLocation/Android geolocation, native share, biometrics, native push,
   behind the existing `Rask.Core.Browser` interfaces + new native-only ones.
