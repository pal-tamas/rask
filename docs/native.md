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
> pre-1.0: APIs may shift. **Native device *backends*** have started landing — the OS **share sheet**
> (`IShare`) now has a native `UIActivityViewController` / `Intent.ACTION_SEND` head backend (see
> [Native device backends](#native-device-backends)) — with native geolocation/push/biometrics still to
> come. The native client now shares the
> transport-neutral DOM behaviour — rAF input/scroll coalescing, keyboard + drag events, and
> scoped-CSS FOUC gating — with the Server and WASM clients (see [Roadmap](#roadmap)); only the
> scoped-JS invoke gate and file uploads remain host-specific.

- [How it fits](#how-it-fits)
- [Get started](#get-started)
- [Safe-area insets (notch / status bar)](#safe-area-insets-notch--status-bar)
- [Two modes: Local and Server](#two-modes-local-and-server)
- [The `INativeWebView` bridge](#the-inativewebview-bridge)
- [Wiring a platform head](#wiring-a-platform-head)
- [Device capabilities](#device-capabilities)
- [Native device backends](#native-device-backends)
- [Honest framing](#honest-framing)
- [Roadmap](#roadmap)

---

## How it fits

Rask has three client "dialects" that all speak one frame contract — a minimal diff (or full-HTML morph)
the client applies to the DOM. Even `Raw`/CodeSample-heavy pages (guides, markdown, highlighted code)
stay on the diff path: a changed sibling of a `Raw` block ships a scoped `MorphSubtree` op that re-morphs
only that one container's children, rather than falling back to a full-document morph — the same fix
benefits all three hosts but matters most here, where the full-document re-render was the costliest path.

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
  `Rask.Server` app, now installable with native device APIs available to the page (see below).

### Native device APIs from a Server app (the capability bridge)

In Server mode the C# runs on the server and the device is a WebView, so a mid-handler `IShare` call can't
work — but a plain **`Shareable`** button still pops the **native** sheet, because the head injects the
[capability bridge](#native-device-backends) into the page. The head does two things (`NativeCapabilities`
gives you both):

```csharp
// 1. Inject at document-start so the page sees window.__raskNative.capabilities + invoke() —
//    ONLY for your trusted origin (never for external navigations; that would expose native to any page).
webView.InjectAtDocumentStart(NativeCapabilities.BridgeScript, forOrigin: shell.ServerBaseUrl);

// 2. Route the WebView's script messages to the shared dispatcher, handing it a native backend.
var share = new NativeShare(/* presenter / activity */);
webView.OnScriptMessage = bytes => NativeCapabilities.TryHandleAsync(bytes, share);
```

Now the *same* `Shareable` component that renders on the server fires the device's native
`UIActivityViewController` / `Intent.ACTION_SEND` when run in the shell — the "superpower". **Security:**
inject `BridgeScript` only for your origin, and open off-origin links in the system browser
(`WKNavigationDelegate` / `shouldOverrideUrlLoading`); the bridge is a fixed component envelope, not open
native RPC.

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
Sharing has two entrypoints, both reaching the **native** sheet on device. The all-host, headless
**`Shareable`** (`Rask.Core`) attaches `data-rask-share` to your element; on the Native host its click is
routed through the **capability bridge** (`window.__raskNative.invoke`) to the registered `IShare` — so it
hits the head's native backend, not the WebView's `navigator.share`. The imperative **`IShare`**
(`Rask.Client.Browser`) shares from code — with the same **native** backend a head registers (below). Further
**native C# backends** (P/Invoke to CoreLocation / Android APIs, biometrics, native push via APNs/FCM)
behind the *same* interfaces — plus new native-only capabilities — are a follow-up (see [Roadmap](#roadmap)).

## Native device backends

`Rask.Native` stays workload-free (plain `net10.0`), so it can't contain iOS/Android P/Invoke. A **native
backend** is therefore a small piece of code in the **platform head** (which carries the workload) that
implements a device interface and registers it on `host.Services` **before `RunLocalAsync`**. DI is
last-registration-wins, so the head's implementation overrides the default the framework registered.

The shipped example is the OS **share sheet**. `IShare` / `ShareData` live in `Rask.Client.Browser` (the
home for in-process client APIs the WASM and Native hosts share; `Rask.Native` can't reference the
browser-targeted `Rask.Wasm`). The default backing is the Web Share API over the WebView; the
`rask-native` template's heads replace it with a native one:

```csharp
// Platforms/iOS/AppDelegate.cs — before RunLocalAsync
host.Services.AddSingleton<IShare>(_ => new NativeShare(() => Window?.RootViewController));

// Platforms/Android/MainActivity.cs — before RunLocalAsync
host.Services.AddSingleton<IShare>(_ => new NativeShare(this));
```

`NativeShare` (also in the template heads) implements `IShare` with `UIActivityViewController` on iOS and an
`Intent.ACTION_SEND` chooser on Android — no transient user activation needed, and it works even where the
WebView doesn't expose `navigator.share`. **The recipe generalises:** to add a native backend for any device
interface, implement it in the head against the platform API and register it before `RunLocalAsync`.

The **imperative** `IShare` calls this directly. The **declarative** `Shareable` reaches it through the
**capability bridge**: the native client advertises `window.__raskNative.capabilities` and an `invoke(name,
data)` that posts a `{ type: "capability" }` message; `NativeAppHost` routes it (via
`NativeCapabilities.TryHandleAsync`) to the registered service (`invoke("share", …)` → `IShare.ShareAsync`).
So a plain `Shareable` button pops the native sheet on device with no host-specific code. The **same**
`NativeCapabilities` toolkit (`BridgeScript` + `TryHandleAsync`) lets a **Native + Server** head inject the
bridge into a remote page, so a plain Server app reaches device natives too — see
[Native device APIs from a Server app](#native-device-apis-from-a-server-app-the-capability-bridge).

The **same recipe** is how the remaining backends (native geolocation, biometrics, push) will land — a
framework-registered default, overridden by a native head implementation.

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
3. **Client parity** — *mostly done.* The transport-neutral DOM helpers are now shared modules
   (`Rask.Core/Resources/rask-input.js` — rAF input/scroll coalescing; `rask-scoped.js` — scoped-CSS
   FOUC gating; keyboard + the four core drag events folded into `rask-events.js`), spliced into all
   three clients (`rask.js`, `rask.wasm.js`, `rask.native.js`) instead of re-copied — so the native
   client reached parity for them and the former Server↔WASM duplication collapsed. **Still per-host
   (deferred):** the scoped-JS `Rask.*` invoke gate (genuinely diverged — WASM tracks scoped `rsk-`
   scripts with a 30s backstop, Server skips them with a 5s timeout; reconciling changes error-boundary
   timing and needs its own pass) and file input/download (WASM JSExport pull vs Server `fetch` upload
   vs a not-yet-built native file bridge).
4. **Showcase sample + E2E** — ✅ *done (Native + Local).* `samples/Rask.Example.Native` mounts the
   **same** `Rask.Example.Shared.App` showcase the Server and WASM hosts mount, onto a `NativeAppHost`
   (see its `NativeExampleHost`). It's covered by the same Playwright E2E net **headlessly, with no
   emulator**: `NativeExampleTests` (in `Rask.Examples.E2E.Tests`) drives the *real* `rask.native.js`
   client + `RunLocalAsync` pipeline in Chromium (the WebView engine class Android ships) via a
   Playwright-backed `INativeWebView` (`PlaywrightNativeWebView`) whose route handler
   (`NativeOriginServer`) serves the shell + client + scoped `/_rask/a/*` assets + `global.css` +
   Bootstrap — the E2E stand-in for a device head's scheme handler. `NativeExampleTests` reuses the
   **same shared showcase walks** the browser hosts run — rendering, in-SPA navigation, composition,
   lifecycle, routing (URL push/back-forward), scoped CSS/JS interop (element-ref focus + sessionStorage),
   elements, CQRS, forms + keyboard, styling + the URL-routed Todos dialog, the Browser-APIs co-mount,
   Bootstrap components, guides, and the popstate in-session 404 (only the `HttpClient`-backed HTTP & files
   walk is skipped — Playwright can't intercept a .NET-side fetch). It's a native shard in CI alongside
   Server/WASM. **Three native-host bugs the E2E surfaced were fixed (see item 6).**
   *(Native + Server needs no separate suite: in that mode the WebView loads a remote Rask Server and
   speaks the ordinary Server (`rask.js`/WS) protocol — the native client isn't involved — so it's
   already covered by `ServerExampleTests`; its only native-specific surface, the real platform
   WebView, is a device-only concern.)*
5. **Native device backends** — *first one shipped.* The OS **share sheet** (`IShare`, in
   `Rask.Client.Browser`) now has a native head backend (iOS `UIActivityViewController`, Android
   `Intent.ACTION_SEND`), overriding the JS-backed default via a head registration before `RunLocalAsync`
   (see [Native device backends](#native-device-backends)). This establishes the reusable
   framework-default-→-native-head-override pattern; CoreLocation/Android geolocation, biometrics, and
   native push (APNs/FCM) follow behind the same seam.
6. **In-process interop + history** — ✅ *fixed (surfaced by item 4's E2E).* (a) An out-of-render
   `IJSRuntime` invoke that carries arguments was embedding `argsJson` as a raw JS literal instead of a
   string, so the client's `JSON.parse(argsJson)` choked — every handler-issued invoke *with args*
   (element-ref focus, storage set/get, …) failed. `NativeJSRuntime.DispatchOutsideRender` now quotes it
   (guarded by `NativeJsInteropTests`). (b) The native client now drives its own WebView history —
   `applyHistory` pushes/replaces each route change and a `popstate` listener feeds Back/forward into the
   router — so `location`/URL tracks the route, hardware Back works, and URL-routed UI (the Todos dialog,
   `Navigator.SetQuery`) works. (c) A **concurrent-render race** (the intermittent flake that first looked
   like "a value-returning read is unreliable", then like a full-HTML morph dropping content): native runs
   async lifecycle/handler continuations on the thread pool (`HandlerSyncContext.Post` uses `Task.Run`), so
   a mid-await render (`RenderInScopeCoreAsync`, or a second continuation's render) could run
   **concurrently** with the dispatch's render — and two renders walking the component tree at once raced
   `ComponentLifecycle.DisposeComponentTree`'s `PersistedChildren` enumeration (`Collection was modified`),
   throwing mid-render into the root error boundary and wiping the page. `NativeLiveSession` now has a
   `_renderLock` (as the Server host does; WASM is single-threaded so it needs none) that serializes every
   render+emit. With it, native drives the **full** shared showcase journey reliably (the JS-interop
   element-ref focus + sessionStorage round-trip, the URL-routed Todos dialog + Browser-APIs co-mount, the
   popstate in-session 404).
