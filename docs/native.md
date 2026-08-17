# 📱 Native mobile apps with Rask (`Rask.Native`)

**Ship real, store-distributable iOS/Android apps from the same Rask component code — no Swift, Kotlin,
React Native, or MAUI.** `Rask.Native` runs your app on the device inside a platform WebView, driven by
the *same* render → diff pipeline as the Server and WASM hosts. Your C# runs **natively** on the device
(App Store / Play Store distribution, native device APIs, real background execution); by default the
*view* is a WebView. Every existing Rask component — `Div()[Span(), …]`, forms, routing, scoped CSS/JS —
works unchanged.

A page can also skip the WebView entirely: [pure-native screens](#pure-native-screens-no-webview) render
real `UIView`/`android.view.View` trees from the same component model, and an app may mix the two —
one route served as HTML, the next fully native.

> **Status — preview / pre-1.0.** The host, the `native` template (with the iOS
> `WKWebView` and Android `WebView` app heads), and the native client runtime **ship and run
> end-to-end on both platforms** — a scaffolded app boots, renders the component tree over the native
> bridge, routes, and updates live (see [Roadmap](#roadmap) for the verification detail). It's still
> pre-1.0: APIs may shift. **Native device *backends*** ship for fifteen interfaces — share, geolocation,
> clipboard, vibration, wake lock, network info, battery, speech synthesis/recognition, screen info, device
> orientation/motion, notifications, badge, and permissions — one
> `host.UsePlatform(new ApplePlatform(…))` / `new AndroidPlatform(this)`
> wires them all, and the framework resolves each native-first over the WebView's JS (see
> [Native device backends](native-devices.md#native-device-backends)) — with biometrics/push still to come. The native client
> now shares the
> transport-neutral DOM behaviour — rAF input/scroll coalescing, keyboard + drag events, and
> scoped-CSS FOUC gating — with the Server and WASM clients (see [Roadmap](#roadmap)); only the
> scoped-JS invoke gate and file uploads remain host-specific.

## On this page

- [Modes & the JS bridge](native-bridge.md) — Local vs Server, INativeWebView, platform heads, asset serving.
- [Device capabilities & chrome](native-devices.md) — safe-area insets, device backends, native header/footer.

Also in this doc: [How it fits](#how-it-fits), [Pure-native screens](#pure-native-screens-no-webview),
[Get started](#get-started), [Honest framing](#honest-framing), [Roadmap](#roadmap).

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
[`INativeWebView`](native-bridge.md#the-inativewebview-bridge), implemented per platform in the app head.

## Pure-native screens (no WebView)

> **Status — both backends ship; neither has been run on a device.** The component family, the render →
> view-tree → diff → patch pipeline, the surface/event contract and the mixed-surface switching all ship,
> covered by unit tests against a test-double backend. The **iOS** (`RaskWkWebView`, UIKit) and **Android**
> (`RaskAndroidWebView`, framework widgets) `INativeSurface` backends both ship and both compile against
> their real platform SDKs — but neither has yet been run on a simulator, emulator or device, so treat the
> on-screen result as unverified. Register no `INativeSurface` and your app keeps rendering through the
> WebView exactly as before.

`NativeScreen` is the pure-native counterpart of `NativeWebView`, and sits in the same slot — a sibling of
the native bars. Everything inside it is a real platform view: no WebView, no HTML, no JavaScript.

```csharp
protected override Component? Render() =>
[
    NativeHeaderBar.Title("Profile"),
    NativeScreen[
        NativeStack.Spacing(12).Padding(16)[
            NativeLabel.Text($"Signed in as {_user.Name}").FontWeight(NativeFontWeight.Semibold),
            NativeTextField.Value(_note).Placeholder("Add a note").OnInput(v => _note = v),
            NativeSwitch.On(_notify).OnChanged(v => _notify = v),
            NativeButton.Text("Save").OnClickAsync(SaveAsync)]],
    NativeTabBar.Tabs([...])
];
```

### The components

| Component | iOS | Android |
| --- | --- | --- |
| `NativeScreen` | `UIStackView` | `LinearLayout` |
| `NativeStack` | `UIStackView` | `LinearLayout` |
| `NativeScroll` | `UIScrollView` + stack | `ScrollView` + stack |
| `NativeList` | `UIScrollView` + stack | `ScrollView` + stack |
| `NativeLabel` | `UILabel` | `TextView` |
| `NativeButton` | `UIButton` | `Button` |
| `NativeTextField` | `UITextField` | `EditText` |
| `NativeSwitch` | `UISwitch` | `Switch` |
| `NativeImage` | `UIImageView` | `ImageView` |
| `NativeActivityIndicator` | `UIActivityIndicatorView` | `ProgressBar` |
| `NativeDivider` | hairline `UIView` | hairline `View` |
| `NativeSpacer` | flexible space | flexible space |

Android uses framework widgets only — no AndroidX/Material dependency — matching how the native bars are
built, so a pure-native screen themes with the app's default theme and adds nothing to the APK.

Every callback comes in both shapes — `OnClick` (`Action`) and `OnClickAsync` (`Func<Task>`), `OnInput` /
`OnInputAsync`, `OnChanged` / `OnChangedAsync`. The async form is **awaited before the frame is built**,
so state a handler sets after an `await` paints in that same frame rather than a later one.

### Routing is unchanged

`Router` and `Outlet` render no HTML of their own, so they work inside a `NativeScreen` exactly as on the
web: `NativeScreen[Router]` gives you `[Route]` pages, route parameters, guards and type-safe
`Features.Routes.*` navigation, with no native-specific routing API.

### Mixing screens and WebView pages

One app can serve some routes as HTML and others as pure-native — a tab bar whose first tab is a web page
and whose second is a native screen is the intended setup. Compose a `NativeWebView` on the routes you
want as markup and a `NativeScreen` on the ones you want native; the host swaps surfaces as you navigate.

**Neither surface is torn down on the switch.** The WebView and the native content view both stay alive
and are merely hidden, so returning to a web route does not reload the page (its DOM, scroll position and
JS state survive) and returning to a native route patches the retained view tree instead of rebuilding it.

Putting HTML inside a `NativeScreen` is a compile error ([RASK048](diagnostics.md#rask048)), as is putting
a native component inside the HTML tree ([RASK032](diagnostics.md#rask032)).

### Wiring it up

The surface backend is opt-in, exactly like `INativeChrome` — register it before `RunLocalAsync` and the
platform head implements it alongside the WebView bridge:

```csharp
host.Services.AddSingleton<INativeSurface>(webView);
```

With no `INativeSurface` registered the native view family is inert and every frame paints through the
WebView, so an existing app is unaffected.

### Building the heads locally

`Platforms/**` is excluded from the default `net10.0` build, so the ordinary solution build, the pre-commit
gate and the pre-push E2E all skip the platform backends entirely — a head can be completely broken while
every default gate stays green. Build them explicitly:

```bash
# iOS — needs the ios workload
dotnet build src/Rask.Native/Rask.Native.csproj -p:RaskNativeHeads=ios

# Android — needs the android workload AND explicit SDK/JDK paths
dotnet build src/Rask.Native/Rask.Native.csproj -p:RaskNativeHeads=android \
  -p:AndroidSdkDirectory=/opt/homebrew/share/android-commandlinetools \
  -p:JavaSdkDirectory=/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home
```

Android needs **three** things and .NET finds none of them by default: the workload
(`sudo dotnet workload install android`), an SDK, and a JDK. `JavaSdkDirectory` must be the **inner**
`libexec/openjdk.jdk/Contents/Home` — pointing at the Homebrew prefix fails with the same `XA5300` as
having no JDK at all, which reads like the JDK is missing rather than misaddressed.

Check `bin/Debug/net10.0-android/Rask.Native.dll` actually exists before believing a green build: if a
`RaskNativeHeads` value is missing from the singular `TargetFramework` condition in the csproj, the build
reports success having compiled only `net10.0`, where `Platforms/**` is excluded.

### Keys on lists

Give every row in a `NativeList` a `Key`. Keyed rows reconcile by identity, so inserting, removing or
reordering **moves** the existing row views — keeping scroll position, focus and in-flight animations —
instead of rewriting each row's contents. Without keys the rows match by position and a reorder repaints
all of them.

`NativeList` does **not** recycle rows: every row is a real view that is built once and kept. That suits
the tens-of-rows lists most screens have, not thousands of them. Cell reuse needs the platform's recycling
collection, whose data-source model doesn't fit a patch-addressed tree; it's a tracked follow-up.

## Get started

Scaffold a native app from the template, then run it on an emulator/simulator:

```bash
dotnet tool install -g Rask.Cli              # one-time: install the rask CLI
rask new MyApp --template native             # --host local (default) | --host server
cd MyApp

dotnet workload install ios android          # the iOS/Android SDK workloads (one-time)
dotnet build -t:Run -f net10.0-android       # Android emulator
dotnet build -t:Run -f net10.0-ios           # iOS simulator (macOS + Xcode)
```

The **`--host`** parameter picks the mode (see [Two modes](native-bridge.md#two-modes-local-and-server)):
`--host local` (default) scaffolds the in-process app below; `--host server` scaffolds a thin shell over a
remote Rask Server with the [native capability bridge](native-bridge.md#native-device-apis-from-a-server-app-the-capability-bridge)
(its heads are `Platforms/{Android/ServerActivity,iOS/ServerAppDelegate}.cs`, and there are no `App.cs`
components — the server renders them).

`rask new MyApp --template native --host local` scaffolds a project that multi-targets `net10.0-ios;net10.0-android`:

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
> (or `-f net10.0-ios`). The Local one shows how [a full app's assets](native-bridge.md#serving-a-full-apps-assets) are
> served on-device. (Template users don't need the flag — the published package already carries the heads.)

Two ordering rules the generated heads already follow — keep them if you edit a head:

- **Register app services on `host.Services` *before* `RunLocalAsync`.** `RunLocalAsync` builds the DI
  provider, so registrations made after it won't take effect.
- **Wire the session *before* loading the shell.** The first render fires when the WebView's client
  posts its `ready` message, so `RunLocalAsync<App>(webView)` must run before the head loads the boot
  shell — otherwise the shell load races the handshake.
- **On-device data with SQLite.** Because the C# runs on the device, you can persist to a local SQLite
  database. Register [`Rask.SQLite`](sqlite.md#sqlite-on-mobile-rasknative)'s `AddRaskSqlite($"Data
  Source={sandboxPath}")` on `host.Services` (the raw, reflection-free path — safe under iOS AOT) and
  inject `IRaskSqliteConnectionFactory`. The showcase's **Todos** tab does exactly this
  (`SqliteTodoStore`), so it survives an app restart on device while staying in-memory on Server/WASM.

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
   [`NativeOriginAssets`](native-bridge.md#serving-a-full-apps-assets). E2E is **Appium** (`tests/Rask.Native.Appium.Tests`):
   it installs and drives the *real* app on an Android emulator / iOS simulator. In the **WebView** context it
   asserts the showcase rendered with its scoped CSS + Bootstrap; in the **native** context it asserts the
   [native header/tab bar](native-devices.md#native-header--footer) projected to real platform bars and that **tapping a native
   tab navigates the WebView** (the round trip through the bridge into the router, read back from
   `document.location`). The Appium suite runs **locally, before push** (it needs a booted Android
   emulator / iOS simulator + an Appium server, so it isn't part of CI): boot a device, start
   `appium`, then `dotnet test tests/Rask.Native.Appium.Tests` (the `Android_*`/`Ios_*` facts skip
   unless `RASK_APPIUM_*` env is set — see the test's `AppiumEnv`). The per-PR CI `native` job still
   compiles both examples for both TFMs (the fast breakage gate). Appium replaced an earlier
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
   [Native device backends](native-devices.md#native-device-backends)) — the second proving the pattern holds for a
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
