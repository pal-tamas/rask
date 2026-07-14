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
> pre-1.0: APIs may shift. **Native device *backends*** ship for share, geolocation, clipboard, vibration,
> wake lock, and network info — one `host.UsePlatform(new ApplePlatform(…))` / `new AndroidPlatform(this)`
> wires them all, and the framework resolves each native-first over the WebView's JS (see
> [Native device backends](#native-device-backends)) — with biometrics/push still to come. The native client
> now shares the
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
dotnet new rask-native -n MyApp              # --host local (default) | --host server
cd MyApp

dotnet workload install ios android          # the iOS/Android SDK workloads (one-time)
dotnet build -t:Run -f net10.0-android       # Android emulator
dotnet build -t:Run -f net10.0-ios           # iOS simulator (macOS + Xcode)
```

The **`--host`** parameter picks the mode (see [Two modes](#two-modes-local-and-server)):
`--host local` (default) scaffolds the in-process app below; `--host server` scaffolds a thin shell over a
remote Rask Server with the [native capability bridge](#native-device-apis-from-a-server-app-the-capability-bridge)
(its heads are `Platforms/{Android/ServerActivity,iOS/ServerAppDelegate}.cs`, and there are no `App.cs`
components — the server renders them).

`dotnet new rask-native --host local` scaffolds a project that multi-targets `net10.0-ios;net10.0-android`:

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

> **Run the in-repo showcase.** Two examples make native a peer of the Server and WASM showcase samples,
> both mounting the *same* `Rask.Example.Shared.App`: `samples/Rask.Example.Native` (Native + Local,
> in-process — the peer of the WASM sample) and `samples/Rask.Example.Native.Server` (Native + Server, a
> thin shell over a running `Rask.Example.Server` — the peer of the Server sample). They multi-target
> `net10.0-ios;net10.0-android` (so they sit outside `Rask.slnx`). Build/run either directly — the
> `-p:RaskNativeHeads=true` makes `Rask.Native` build its platform heads from source:
> `dotnet build samples/Rask.Example.Native/Rask.Example.Native.csproj -t:Run -f net10.0-android -p:RaskNativeHeads=true`
> (or `-f net10.0-ios`). The Local one shows how [a full app's assets](#serving-a-full-apps-assets) are
> served on-device. (Template users don't need the flag — the published package already carries the heads.)

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
[capability bridge](#native-device-backends) into the page. Scaffold the Server-mode head with the
`--host` parameter:

```bash
dotnet new rask-native -n MyApp --host server   # (--host local is the default)
```

The generated head (`Platforms/Android/ServerActivity.cs`, `Platforms/iOS/ServerAppDelegate.cs`) points its
WebView at your `ConnectToServer(...)` URL and wires the bridge with the two `NativeCapabilities` helpers:

- **`NativeCapabilities.BridgeScript`** — injected at each navigation **only for your trusted origin**, so
  the page sees `window.__raskNative.capabilities` + `invoke(name, data)`.
- **`NativeCapabilities.TryHandleAsync(messageJson, share)`** — the WebView's script-message handler routes
  every posted message here with a native `IShare` (the scaffold's `NativeShare`).

Now the *same* `Shareable` component that renders on the server fires the device's native
`UIActivityViewController` / `Intent.ACTION_SEND` when run in the shell — the "superpower". **Security:** the
generated head injects `BridgeScript` only for your origin and opens off-origin links in the system browser
(`WKNavigationDelegate.decidePolicyForNavigationAction` / `shouldOverrideUrlLoading`), so the WebView never
leaves your origin and no other page can reach native; the bridge is a fixed component envelope, not open
native RPC. (For a local `http://10.0.2.2:<port>` dev server, allow cleartext in `AndroidManifest.xml`.)

## The `INativeWebView` bridge

`Rask.Native` **ships the platform WebView heads** — `RaskWkWebView` (iOS, `WKWebView`) and
`RaskAndroidWebView` (Android, `android.webkit.WebView`) — under `Platforms/{iOS,Android}`, built when the
package is packed with the mobile workloads. Your app head just news one up, so you almost never implement
the bridge yourself. Both are the platform-specific implementation of one contract:

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
its `Dispatch` boundary. Implement it yourself only for a custom WebView; the shipped heads already do — each
serves a **real origin** (a `WKUrlSchemeHandler` on iOS, `WebViewClient.ShouldInterceptRequest` on Android)
so secure-context device APIs (`localStorage`, `crypto.subtle`) work, and marshals `ApplyRenderAsync`/
`EvaluateJavaScriptAsync` onto the UI thread.

## Wiring a platform head

The app head (a `net10.0-ios` / `net10.0-android` project) is just an entry point that composes the shipped
pieces — the WebView bridge, the [native share backend](#native-device-backends), and (Local mode) the host:

```csharp
// Android MainActivity / iOS AppDelegate (Native + Local):
var webView = new RaskAndroidWebView(this);          // or new RaskWkWebView() on iOS
var host = NativeAppHost.CreateDefault();
host.UsePlatform(new AndroidPlatform(this));         // native backends: share, geolocation, clipboard, …
var app = await host.RunLocalAsync<App>(webView);
webView.LoadShell();
```

The heads serve the boot shell + client + your bundled assets through
[`NativeOriginAssets`](#serving-a-full-apps-assets) (below), so there is nothing else to wire. See
`samples/Rask.Example.Native` for a complete head, and the `rask-native` template for a fresh one.

## Serving a full app's assets

The boot shell + client are only two files; a real app also loads **scoped CSS/JS** (`/_rask/a/{hash}.{ext}`),
your `wwwroot` static files, Bootstrap (`/_content/Rask.Bootstrap/*`) and fetches `data/*.json`. `Rask.Native`
ships the request table so your scheme handler is a one-liner — **`NativeOriginAssets.Resolve`**:

```csharp
// In your WebViewClient.ShouldInterceptRequest / WKUrlSchemeHandler:
var path = new Uri(url).AbsolutePath;
if (NativeOriginAssets.Resolve(path, ReadBundledAsset) is { } asset)
    return Respond(asset.Body, asset.ContentType);   // shell/client + scoped assets + your static files
return EmptyOk();                                     // under-origin miss → don't hang the page
```

It resolves the shell/client (`NativeClientAssets`) and scoped assets (`ScopedAssetRegistry`) itself, and
delegates everything else to your `Func<string, byte[]?>` reader — typically the app's **bundled assets**
(Android `AssetManager.Open`, iOS a path under `NSBundle.MainBundle`), so it all works **offline**. For the
in-process demo `HttpClient` (so data-driven pages resolve `data/*.json` off the network too), register it
over **`NativeAssetHttpHandler`** with the same reader and `BaseAddress` = your app origin. See
`samples/Rask.Example.Native` for a complete working head.

## Device capabilities

The `IJSRuntime`-backed browser wrappers in `Rask.Core.Browser` (storage, media query, the observers,
crypto, …) work **through the WebView's JS engine** with no extra code — `NativeAppHost` registers them and
`NativeJSRuntime` dispatches them over the bridge. On top of that, `Rask.Native` **ships native C# backends**
for the interfaces where a native API beats the WebView (or the WebView doesn't expose one at all); the
framework wires them ahead of the JS defaults, so you inject the ordinary interface and get the native
implementation. See [Native device backends](#native-device-backends).

## Native device backends

A **native backend** is a C# class that implements a `Rask.Core.Browser` (or `Rask.Client.Browser`)
interface against the platform SDK — `CLLocationManager`, `UIPasteboard`, `ClipboardManager`, and friends —
instead of the WebView's JS. These live in `Rask.Native/Platforms/{iOS,Android}` and compile only for the
head TFMs (the base `net10.0` build stays workload-free). You never register them one by one: a **platform
module** does it, and the framework resolves native-first.

```csharp
// Platforms/iOS/AppDelegate.cs — before RunLocalAsync
host.UsePlatform(new ApplePlatform(() => Window?.RootViewController));

// Platforms/Android/MainActivity.cs — before RunLocalAsync
host.UsePlatform(new AndroidPlatform(this));
```

`ApplePlatform` / `AndroidPlatform` implement `INativePlatform`; `NativeAppHost.RunLocalAsync` invokes them
**before** wiring the JS-backed fallbacks, and every registration uses `TryAdd`. So an interface a platform
backs natively **wins** (native-first), an explicit `host.Services` registration you add yourself wins over
even that, and every interface no one backed falls back to the WebView's JS — the framework picks the best
implementation per interface with no per-API wiring.

The shipped native backends (both platforms):

| Interface | iOS | Android |
|---|---|---|
| `IShare` | `UIActivityViewController` | `Intent.ACTION_SEND` |
| `IGeolocation` | `CLLocationManager` | `LocationManager` |
| `IClipboard` | `UIPasteboard` | `ClipboardManager` |
| `IVibration` | system vibration (AudioToolbox) | `Vibrator` / `VibratorManager` |
| `IWakeLock` | `UIApplication.IdleTimerDisabled` | window `FLAG_KEEP_SCREEN_ON` |
| `INetworkInfo` | `NWPathMonitor` | `ConnectivityManager` |

So `await geolocation.GetCurrentPositionAsync()` returns a native fix (real permission prompt +
`CLLocationManager` / `LocationManager` accuracy) instead of `navigator.geolocation`, `clipboard.WriteTextAsync`
hits `UIPasteboard` / `ClipboardManager` (no WebView gesture gate), and so on. Geolocation and network info
need platform permissions — add `ACCESS_FINE_LOCATION` / `ACCESS_NETWORK_STATE` (Android) and
`NSLocationWhenInUseUsageDescription` (iOS), and the head requests the location runtime grant.

The **declarative** `Shareable` still reaches the native share sheet through the **capability bridge**: the
native client advertises `window.__raskNative.capabilities` and an `invoke(name, data)` that posts a
`{ type: "capability" }` message; `NativeAppHost` routes it (via `NativeCapabilities.TryHandleAsync`) to the
resolved `IShare` (`invoke("share", …)` → `IShare.ShareAsync`) — so a plain `Shareable` button pops the
native sheet with no host-specific code. The **same** `NativeCapabilities` toolkit lets a **Native + Server**
head inject the bridge into a remote page, so a plain Server app reaches device natives too — see
[Native device APIs from a Server app](#native-device-apis-from-a-server-app-the-capability-bridge).

**To add your own backend** (or override a shipped one), implement the interface in your head and register it
on `host.Services` before `RunLocalAsync` — it wins over the platform module's version. Further native
backends behind the *same* interfaces (biometrics, native push via APNs/FCM) are a follow-up (see
[Roadmap](#roadmap)).

## Native header & footer

A native page is a small **composed tree**: the native bars (`NativeHeaderBar` / `NativeTabBar` /
`NativeToolbar`) as siblings of a **`NativeWebView`**, which hosts the ordinary page shell
(`Doctype`/`Html`/`Head`/`Body`). The native host projects the bars to a **real `UINavigationBar` +
`UITabBar`/`UIToolbar`** on iOS, and a top bar + bottom tab/tool bar on Android, and serializes the
`NativeWebView`'s HTML into the WebView between them. The bars are ordinary factory-built components — you
compose them in `Render()`, they work like any other component:

```csharp
protected override Component? Render() =>
[
    NativeHeaderBar(Title: "Dashboard", Trailing: [NativeBarButton(Icon: NativeIcon.Add, OnClick: OnAdd)]),
    NativeWebView()[
        Doctype(),
        Html("en")[Head(), Body()[Router()]]
    ],
    NativeTabBar(Tabs: [
        NativeTab(Title: "Home", Icon: NativeIcon.Home, To: Features.Routes.HomePage()),
        NativeTab(Title: "Me",   Icon: NativeIcon.Person, To: Features.Routes.MePage()),
    ], Selected: 0)
];
```

- **`NativeWebView` hosts the HTML** — its children are the normal page shell; only native bars may sit outside
  it. A bar nested inside the HTML (an element child, or inside `NativeWebView`'s content) is a **RASK032**
  compile error — bars belong at the layout level, as siblings of `NativeWebView`.
- **Type-safe icons** — `NativeIcon` pairs an iOS SF Symbol with an Android drawable/Material name; use a
  curated member (`NativeIcon.Home`) or an escape hatch (`NativeIcon.Custom(sfSymbol, drawable)` /
  `NativeIcon.SfSymbol(...)` / `NativeIcon.Drawable(...)`). Routes are type-safe too (`Features.Routes.*`).
- **Bar buttons** run their `OnClick` on the render thread and re-render, like any Rask callback. **Tabs**
  navigate to their route; the page recomputes `Selected` from the current route on the next render. Each
  projected bar view carries a stable **accessibility identifier** (the tab/button title, or
  `rask-native-header`), so screen readers — and UI tests like the Appium on-device E2E — can address it.
- **Bars render no HTML** — they are collected during the render walk (so their factories are DI-correct and
  callbacks wire to their owner); the last bar of a kind wins. Only the settled build's chrome is pushed, and
  an unchanged bar never re-pushes (no flicker on a counter tick).
- **Opt-in + inert elsewhere** — register an `INativeChrome` backend on `host.Services` before `RunLocalAsync`
  (the platform WebView heads implement it; assign `webView.ChromeView` instead of `webView.View`), exactly
  like `IShare`. With no backend registered the bars render nothing. Sharing an app across web + native? Branch
  with `IsNative`: compose the native tree under the native shell and return the plain shell on Server/WASM.
  This is a **bounded native-widget surface** (a header + footer), not a general native-control renderer.

## Honest framing

This is a **WebView hybrid** (the same architecture as .NET MAUI Blazor Hybrid, Ionic/Capacitor): C# runs
natively, the view is a WebView. It is **not** a general native-control renderer — Rask components render HTML,
and that HTML renders in a WebView. The one exception is the bounded **native header & footer** surface above
(real platform bars around the WebView); a *full* native-control renderer (mapping the whole component tree to
UIKit/Android views) would require a parallel non-HTML component library and is out of scope. What the hybrid
buys over the [PWA story](pwa.md): App Store / Play Store distribution, native device APIs beyond the browser
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
4. **Showcase examples + on-device E2E** — ✅ *done.* Two runnable examples mirror the Server/WASM
   pairing, both mounting the **same** `Rask.Example.Shared.App`: `samples/Rask.Example.Native`
   (Native + Local, in-process) and `samples/Rask.Example.Native.Server` (Native + Server, a thin shell
   over `Rask.Example.Server`). They serve the full showcase's assets on-device through the framework's
   [`NativeOriginAssets`](#serving-a-full-apps-assets). E2E is **Appium** (`tests/Rask.Native.Appium.Tests`):
   it installs and drives the *real* app on an Android emulator / iOS simulator. In the **WebView** context it
   asserts the showcase rendered with its scoped CSS + Bootstrap; in the **native** context it asserts the
   [native header/tab bar](#native-header--footer) projected to real platform bars and that **tapping a native
   tab navigates the WebView** (the round trip through the bridge into the router, read back from
   `document.location`). **Android** runs per-PR in the Ubuntu
   `native-appium` CI job (KVM emulator); **iOS** (XCUITest on a macOS simulator) runs nightly + on-demand
   in `native-ios-e2e.yml` — kept off the per-PR path because macOS minutes and the Microsoft.iOS↔Xcode SDK
   coupling make it too costly/fragile to gate every PR, the same nightly cadence MAUI uses for device UI
   tests. A per-PR `native` job additionally compiles both examples for both TFMs. Appium replaced an earlier
   headless Playwright-in-Chromium shim, and immediately surfaced a device-only bug the shim had masked
   (the boot shell loads at `/index.native.html`, a path `NativeOriginAssets` now serves).
   *(Native + Server needs no separate suite: in that mode the WebView loads a remote Rask Server and
   speaks the ordinary Server (`rask.js`/WS) protocol — the native client isn't involved — so it's
   already covered by `ServerExampleTests`; its only native-specific surface, the real platform
   WebView, is a device-only concern.)*
5. **Native device backends** — *two shipped.* The OS **share sheet** (`IShare`, iOS
   `UIActivityViewController` / Android `Intent.ACTION_SEND`) and **native geolocation** (`IGeolocation`, iOS
   `CLLocationManager` / Android `LocationManager`) both have native head backends that override the
   JS-backed default via a head registration before `RunLocalAsync` (see
   [Native device backends](#native-device-backends)) — the second proving the pattern holds for a
   request/response + subscription capability, not just fire-and-forget. This establishes the reusable
   framework-default-→-native-head-override seam; biometrics and native push (APNs/FCM) follow behind it.
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
