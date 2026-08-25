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
> scoped-CSS FOUC gating — with the Server and WASM clients (see [Roadmap](#roadmap)), and
> [file input, downloads and sign-out](#files-downloads-and-sign-out) work here as they do on the web
> heads; only the scoped-JS invoke gate remains host-specific.

## On this page

- [Modes & the JS bridge](native-bridge.md) — Local vs Server, INativeWebView, platform heads, asset serving.
- [Device capabilities & chrome](native-devices.md) — safe-area insets, device backends, native header/footer.

Also in this doc: [How it fits](#how-it-fits), [Pure-native screens](#pure-native-screens-no-webview),
[A WebView pointed at your own app](#a-webview-pointed-at-your-own-app),
[The hardware Back button](#the-hardware-back-button),
[Get started](#get-started), [Files, downloads and sign-out](#files-downloads-and-sign-out),
[Honest framing](#honest-framing), [Roadmap](#roadmap).

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

> **Status — both backends ship, and both have now been run on a device.** The component family, the
> render → view-tree → diff → patch pipeline, the surface/event contract and the mixed-surface switching
> all ship, covered by unit tests against a test-double backend. The **iOS** (`RaskWkWebView`, UIKit) and
> **Android** (`RaskAndroidWebView`, framework widgets) `INativeSurface` backends were both exercised on
> real platforms by the #775 spike: iOS on an iPhone 17 Pro simulator and Android on an API 36 emulator,
> each painting a real platform view tree inside the native chrome with no WebView showing, with route →
> surface selection and tab tracking both working. Register no `INativeSurface` and your app keeps
> rendering through the WebView exactly as before.

`NativeScreen` is the pure-native counterpart of `NativeWebView`, and sits in the same slot — a sibling of
the native bars. Everything inside it is a real platform view: no WebView, no HTML, no JavaScript.

```csharp
protected override Component? Render() =>
[
    NativeHeaderBar.Title("Profile"),
    NativeScreen[
        NativeStack.Spacing(12).Padding(16)[
            NativeLabel.FontWeight(NativeFontWeight.Semibold)[$"Signed in as {_user.Name}"],
            NativeTextField.Value(_note).Placeholder("Add a note").OnInput(v => _note = v),
            NativeSwitch.On(_notify).OnChanged(v => _notify = v),
            NativeButton.OnClickAsync(SaveAsync)["Save"]]],
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

### A WebView pointed at your own app

A `NativeWebView` has a second mode. Instead of hosting markup, give it a `Url` and it loads that address —
a Rask server, or a WASM app you host — so the UI ships when you deploy rather than when the store approves:

```csharp
protected override Component? Render() =>
[
    NativeHeaderBar.Title("Home"),
    NativeWebView.Url("https://app.example.com/"),
    NativeTabBar.Tabs([...]),
];
```

The bars around it are still native, still declared in the same `Render()`, and still projected onto a real
`UINavigationBar` / Android top bar. The page reaches the device backends through the
[capability bridge](native-bridge.md#native-device-apis-from-a-server-app-the-capability-bridge). What
changes is only where the UI comes from.

`.Url(…)` takes a `string` or a `Uri`, and both must be an absolute `http`/`https` address — a relative one,
or a `javascript:` / `data:` / `file:` one, is rejected where you write it rather than becoming a blank
WebView on a device.

The two modes are exclusive. One `NativeWebView` that sets a `Url` *and* takes children is
[RASK049](diagnostics.md#rask049) — it shows one document, so the children could only be discarded — and a
component that uses both modes is [RASK050](diagnostics.md#rask050), because in URL mode the session holds
no HTML baseline for a markup arm to paint against. A pure-native `NativeScreen` is
unaffected by both and composes beside either.

> **The page you name gets your device APIs.** The head keeps the WebView on that origin — an off-origin
> link opens in the system browser instead — but that confines where the grant travels, not what the site
> you pointed at can do with it. Point it at an origin you control.

> **Cleartext is blocked by default.** A `--host native` app ships with no ATS exception on iOS and no
> `usesCleartextTraffic` on Android, so a `Url` of `http://localhost:5000` loads **nothing, silently**. Use
> `https://`, or add the narrow exception the remote templates use while developing (see
> [the CLI docs](cli.md)).

### The hardware Back button

A pure-native app has no page, so there is no `window.history` for Back to pop — the session keeps its
own history instead, and the head routes the button through it:

```csharp
private void RegisterBackHandler()
{
    if (!OperatingSystem.IsAndroidVersionAtLeast(33) || OnBackInvokedDispatcher is not { } dispatcher)
    {
        return;
    }

    _backCallback = new BackInvokedCallback(this);
    dispatcher.RegisterOnBackInvokedCallback(0, _backCallback);
}

private void HandleBack()
{
    if (_app is { CanGoBack: true } app)
    {
        _ = app.GoBackAsync();
        return;
    }

    Finish();   // nothing to pop — Back closes the app, as it should at the root of a task
}
```

`rask new --template native` writes this for you. Two things about it are easy to get wrong:

- **Use `OnBackInvokedDispatcher`, not the `OnBackPressed` override.** For an app targeting API 35+
  predictive back is enabled by default and `OnBackPressed` is **never called** — the override compiles,
  looks correct, and Back silently closes the app mid-navigation. Keep the override only as the API 32
  and earlier path.
- **Registering a callback means you own closing the app too.** The system stops doing it for you, so
  `HandleBack` must call `Finish()` when `CanGoBack` is false, or Back is dead on the first page.

`CanGoBack` is false for an app that has a WebView: the page owns that history, and reading it needs a
round trip the back handler cannot wait for. A hybrid app therefore keeps the platform's default Back.

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

```csharp
NativeList[todos.Select(t => NativeStack.Key(t.Id).OnClick(() => Toggle(t))[NativeLabel[t.Title]])]
```

`Key` comes **first** in the chain ([RASK046](diagnostics.md#rask046)) — it decides which instance is being
built, so anything written before it lands on one that is about to be discarded. And because the chain ends
at the children indexer it hands back a `Component`, so the enumerable needs no `(Component)` cast; a chain
ending at a setter would, since that yields a `Build<T>`.

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
rask new MyApp --template native             # --host native (default) | --host server
cd MyApp

dotnet workload install ios android          # the iOS/Android SDK workloads (one-time)
dotnet build "-t:Build;Run" -f net10.0-android   # Android emulator
dotnet build "-t:Build;Run" -f net10.0-ios       # iOS simulator (macOS + Xcode)
```

The **`--host`** parameter picks which of Rask's app models supplies the UI (see
[Two modes](native-bridge.md#two-modes-local-and-server)). `--host native` (default) scaffolds the
in-process app below — one project, everything on the device.

`--host server` and `--host wasm-hosted` scaffold **both halves as one solution**: the app you host, and a
`.Mobile` project carrying the thin-shell heads that point at it, with the
[native capability bridge](native-bridge.md#native-device-apis-from-a-server-app-the-capability-bridge)
wired in. A shell with nothing behind it is not a runnable app, so the template does not leave you to
supply the other half:

```
MyApp/
  MyApp.slnx
  MyApp.Server/      # the Rask app the shell points at (wasm-hosted also gets .Client and .Shared)
  MyApp.Mobile/      # iOS + Android heads — Platforms/{iOS/ServerAppDelegate,Android/ServerActivity}.cs
```

The heads point at the app half's own dev URL out of the box — `http://localhost:5000` on the iOS
simulator, `http://10.0.2.2:5000` on the Android emulator, which is that VM's alias for your machine — so a
fresh solution connects with nothing to edit. The scaffold allows cleartext for exactly that
(`usesCleartextTraffic` on Android, `NSAllowsLocalNetworking` on iOS); swap in your deployed `https://` URL
and drop both. There is no `App.cs` in `.Mobile` — the app half renders the components.

`rask new MyApp --template native --host native` scaffolds a project that multi-targets `net10.0-ios;net10.0-android`:

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
> `dotnet build samples/Rask.Example.Native/Rask.Example.Native.csproj "-t:Build;Run" -f net10.0-android -p:RaskNativeHeads=true`
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

## Files, downloads and sign-out

`Rask.Core` is the shared component surface, so anything a component can inject or call from it has to work
on Server, WASM **and** native — otherwise a component written once breaks on one head only, at runtime,
with nothing at compile time to warn you. `RaskHostContracts` names that set, and each host's
test project asserts its own bootstrap resolves every one of them.

Three of them used to be missing here, and the failures were quiet: a file input handed the handler an
**empty list**, `Navigator.Download` threw, and injecting `IAuthSignIn` failed DI.

### File input

Nothing to wire. A file input works exactly as it does on the other hosts:

```csharp
Input.Value<string>(null)
    .Type(InputType.File)
    .OnFiles(files => { foreach (var f in files) Save(f.OpenReadStream()); })
```

The picked `File` stays in the WebView, registered under a short ref by the shared
`Rask.Core/Resources/rask-files.js` module (spliced into both in-process clients). `NativeFileBackend`
reads it back a chunk at a time over `IJSRuntime`, so `OpenReadStream` is a real stream — a large upload is
never buffered through a render payload. Files picked in a form's `submit` arrive the same way.

### Downloads → the OS share sheet

`Navigator.Download(name, bytes, contentType)` works from any handler. What differs is the *delivery*: a
browser downloads, and a device shares. `<a download>` does nothing useful in a `WKWebView`, and a file
written into the app sandbox is invisible to the user on iOS unless the app opts into file sharing — so the
host stages the file under the app cache directory and hands it to **`INativeFileExport`**:

```csharp
public interface INativeFileExport
{
    ValueTask ExportAsync(NativeFileExport file);   // FileName, ContentType, Path
}
```

A platform head registers one (`UIActivityViewController` with the file URL on iOS,
`Intent.ACTION_SEND` with a `FileProvider` URI on Android) through `INativePlatform.Register`, the same
native-first seam the [device backends](native-devices.md#native-device-backends) use. Register your own on
`host.Services` to send downloads somewhere else. With none registered the file is still staged and its
location reported through `RaskDiagnostics`, so a shared component never crashes on a head that has no
share sheet.

The bytes never ride the frame: the payload carries a token, the client hands it straight back, and the host
pulls the bytes — the same token-pull contract the WASM host uses. Download names are reduced to a single
safe path segment before they touch the filesystem; on this host the name becomes a real path, and it can be
attacker-influenced (a record title, a filename echoed back from an API).

> `INativeFileExport` is deliberately separate from `IShare`. `ShareData` is Web-Share-shaped — title, text,
> URL, no file — and widening it would change what the same type means on the WASM host.

### Sign-out

`NativeAuthSignIn` clears the local `ITokenStore`, posts to its `LogoutPath` when the app has registered an
`HttpClient` to reach a server, refreshes `IUserProvider`, then navigates. Both server-facing dependencies
are optional, because a Native + Local app need not have a backend at all — an offline app signs out by
dropping its token. The token is cleared **before** the network call, so a failed request still leaves the
device signed out rather than holding a live token. `returnUrl` goes through the same `LocalUrl` sanitizer
the web hosts use; a native app reaches these through deep links.

`SignInAsync(principal)` throws, as it does on WASM: a device cannot mint its own principal. POST credentials
to a server endpoint and persist the issued token through `ITokenStore`.

All four registrations use `TryAdd`, so an app or platform module that registers its own wins.

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
   client reached parity for them and the former Server↔WASM duplication collapsed. **File input and
   download now work here too** — see [Files, downloads and sign-out](#files-downloads-and-sign-out); the
   ref registry moved into a shared `rask-files.js`, and the transports stay per-host by design (a WASM
   JSExport pull, a Server `fetch` upload, an `IJSRuntime` chunk read on native). **Still per-host
   (deferred):** the scoped-JS `Rask.*` invoke gate (genuinely diverged — WASM tracks scoped `rsk-`
   scripts with a 30s backstop, Server skips them with a 5s timeout; reconciling changes error-boundary
   timing and needs its own pass).
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
