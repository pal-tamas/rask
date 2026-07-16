# Browser APIs

Rask ships **typed C# wrappers over the browser's Web APIs** — inject one through a component
constructor and call it, instead of hand-writing `IJSRuntime` identifiers and getting the JSON
shape right yourself. Each is a thin, awaitable layer over the same unified
[`IJSRuntime`](js-interop.md#calling-js-from-c-ijsruntime), so it works the same way whether your
app runs on the **Server** (WebSocket) or **WASM** (`JSImport`/`JSExport`) transport.

This page is the **map of the whole surface**. For an at-a-glance view of *where each API works*
(Web / PWA / Native, and which have a native backend), see the
[**capability matrix**](browser-capabilities.md) — it links to a dedicated reference page per API
under [`docs/apis/`](apis/). For the deeper "why" — user activation, the transport seam, element
refs — see [JS interop → Typed browser APIs](js-interop.md#typed-browser-apis); for the mobile/PWA
angle see the [Mobile & PWA guide](pwa.md). Every wrapper has a runnable demo in the **Browser APIs**
section of the [showcase](https://pal-tamas.github.io/rask/demo/).

## Three homes, one rule

- **`Rask.Core.Browser`** — APIs that work on **every host** (Server + WASM + Native). Registered by all three.
- **`Rask.Client.Browser`** — APIs the **in-process** hosts (WASM + Native) can run but Server can't:
  they need *transient* user activation, preserved only when the interop call runs inside the click's own
  call stack, which the Server's WebSocket round-trip loses. `Rask.Native` can't reference the
  browser-targeted `Rask.Wasm`, so anything both in-process hosts share lives here.
- **`Rask.Wasm.Browser`** — browser-only APIs (the installed-PWA instance / live document / browser-only
  device APIs). Registered only by the WASM host.

> **The rule:** shared-everywhere APIs live in `Rask.Core.Browser`; WASM+Native-shared ones in
> `Rask.Client.Browser`; browser-only ones in `Rask.Wasm.Browser`. A host simply doesn't register a
> service it can't provide.

Sharing shows the split cleanly. The **declarative, headless** `Shareable` (Rask.Core) hands *your* markup
a `data-rask-share` attribute and the shared client fires `navigator.share` *inside the click gesture* — no
round-trip, so activation survives — so it works on **every** host, Server included. The **imperative**
`IShare` (Rask.Client) lets you share from code (a lifecycle hook, after an `await`), which only the
in-process hosts can do, so it lives one tier down.

Inject through the **constructor** (not a settable property — that would become a required factory
parameter) and call from an **event handler or lifecycle hook**, never from `Render()`:

```csharp
public sealed class ThemeToggle(IBrowserStorage storage, IMediaQuery media) : Component
{
    protected override async Task OnRenderedAsync(bool first)
    {
        if (!first) return;
        var saved = await storage.Local.GetAsync("theme");
        var dark = saved is null ? await media.PrefersDarkAsync() : saved == "dark";
        // …apply theme…
    }
}
```

Browser-gated APIs (clipboard, geolocation, notifications, fullscreen, crypto's secure context, …)
can fail — a denial/timeout/unsupported surfaces as a `JSException` from the awaited task, so gate on
the API's `IsSupported`/permission check and wrap calls in `try/catch`.

## Shared APIs — `Rask.Core.Browser`

Work identically on Server and WASM. **Shape** is *one-shot* (a request/response call) or
*subscription* (you hold an `IAsyncDisposable` and the browser **pushes** updates to a C# handler — see
[Subscriptions](#subscriptions-the-push-pattern)).

| Service | Wraps | What it does | Shape |
| --- | --- | --- | --- |
| `IBrowserStorage` | `localStorage` / `sessionStorage` | `.Local` / `.Session` key/value (get/set/remove/clear/key/length) | one-shot |
| `ICookies` | `document.cookie` | Read/write cookies with typed `CookieOptions` | one-shot |
| `IClipboard` | `navigator.clipboard` | `WriteTextAsync` / `ReadTextAsync` | one-shot |
| `IGeolocation` | `navigator.geolocation` | `GetCurrentPositionAsync` (one fix) + `WatchAsync` (live tracking) | one-shot + subscription |
| `IPermissions` | `navigator.permissions` | `QueryAsync(PermissionName)` → `PermissionState` before prompting | one-shot |
| `IVibration` | `navigator.vibrate` | Haptic buzz / pattern (mobile) | one-shot |
| `IPageVisibility` | `document.visibilityState` | Foreground/background state | one-shot |
| `INavigatorInfo` | `window.navigator` | `OnLineAsync` / `LanguageAsync` / `UserAgentAsync` | one-shot |
| `INetworkInfo` | `navigator.connection` | Effective type / downlink / RTT / Data Saver — adapt loading | one-shot |
| `IMediaQuery` | `window.matchMedia` | Evaluate a query; `PrefersDarkAsync` / `PrefersReducedMotionAsync` | one-shot |
| `ISpeechSynthesis` | `window.speechSynthesis` | Speak text aloud; cancel | one-shot |
| `IMediaSession` | `navigator.mediaSession` | Now-playing metadata + hardware media-key handlers (native-feel player) | one-shot + subscription |
| `IDeviceOrientation` | `deviceorientation` | Gyroscope/compass tilt angles (tilt UI, AR, compass) | **subscription** |
| `IDeviceMotion` | `devicemotion` | Accelerometer / rotation rate (shake, step counter, motion games) | **subscription** |
| `IScreenInfo` | `window.screen` | Size, color depth, device pixel ratio (retina) | one-shot |
| `IStorageEstimator` | `navigator.storage.estimate` | Quota / usage, to budget caches | one-shot |
| `IVisualViewport` | `window.visualViewport` | Visible size/offset/zoom after the soft keyboard | one-shot |
| `ICrypto` | `crypto` / `crypto.subtle` | Random UUID / bytes, SHA digest (hex) | one-shot |
| `IPerformance` | `performance` | High-res clock + navigation timing (TTFB / DCL / load) | one-shot |
| `IIndexedDb` | IndexedDB | `OpenStoreAsync(name)` → large async key/value store | one-shot |
| `IFileSystemAccess` | File System Access API | Open/save a file *back to disk* + directory access (editors) | one-shot |
| `IWebAuthn` | Web Authentication API | Passkeys — register / sign in with biometric or security key | one-shot |
| `IWebLocks` | Web Locks API | Serialise work across an origin's tabs/workers — hold a named lock for a callback | callback-scoped |
| `IBroadcastChannel` | `BroadcastChannel` | Cross-tab messaging | **subscription** |
| `IIntersectionObserver` | `IntersectionObserver` | Element enters/leaves the viewport (lazy-load, infinite scroll) | **subscription** |
| `IResizeObserver` | `ResizeObserver` | Element's size changes (container-responsive layout) | **subscription** |
| `IMutationObserver` | `MutationObserver` | Element's children/attributes/text change (react to externally-written DOM) | **subscription** |
| `IGamepad` | Gamepad API | Connected controllers — sticks / triggers / buttons (browser games) | **subscription** |
| `IWebPush` | Push API | Subscribe to Web Push (returns a `PushSubscription`); send from the backend with [`Rask.WebPush`](pwa.md#sending-from-your-backend-raskwebpush) | one-shot |
| `INotifications` | Notifications API | Show a local notification from the page | one-shot |
| `IBadge` | Badging API | Set/clear a count on the installed app icon | one-shot |
| `IWakeLock` | Screen Wake Lock API | Keep the screen awake (sentinel; dispose to release) | one-shot |

The last four are **PWA** APIs but transport-agnostic (`IJSRuntime`-backed, no transient activation), so they
register on Server too — their JS helpers just ship on the Server client only under `AddRaskPwa` (see
[pwa.md](pwa.md)). On Native, several Shared APIs resolve to a **native C# backend** instead of the WebView —
see the [capability matrix](browser-capabilities.md) and the [Native guide](native.md#native-device-backends).

## Sharing — declarative (all hosts) vs imperative (in-process)

**`Shareable`** (`Rask.Core`) is the all-host way to share, and it's **headless** — you render the trigger,
it hands you the `data-rask-share` attribute to spread onto it:

```csharp
Shareable(new ShareData { Title = "Rask", Url = "https://…" },
    share => Button(Type: "button", Class: "btn btn-primary", Data: share)["Share"])
```

The shared client fires `navigator.share` **inside the click gesture** — no round-trip, so the transient
user activation survives even on the Server transport. Because it's headless, the trigger can be any element
with a `Data` prop (a link, an icon button, a `BsButton`), not just a `<button>`. In the native shell it
upgrades to a native backend. Web Share is available on mobile Safari / Android Chrome / Edge (not desktop
Firefox); an unsupported browser no-ops.

**`IShare`** (`Rask.Client.Browser`) is the **imperative** path — share from *code* (a lifecycle hook,
after an `await`). That needs the in-process transport to keep the activation, so it's registered only by
the **WASM and Native** hosts (`Rask.Native` can't reference the browser-only `Rask.Wasm`, so it lives in
`Rask.Client`). On Native a platform head can register a native `UIActivityViewController` /
`Intent.ACTION_SEND` backend that needs no activation — see the [Native guide](native.md#native-device-backends).

| API | Home | Hosts | Use |
| --- | --- | --- | --- |
| `Shareable` | `Rask.Core` | **all** (Server too) | Headless declarative share — attaches `data-rask-share` to your element; fires `navigator.share` in the gesture |
| `IShare` | `Rask.Client.Browser` | WASM + Native | Imperative share from code; native backend on Native |

### Gesture bridge — activation-gated APIs on the Server host

`Shareable`'s trick — run the call **inside the click gesture** so the transient user activation survives —
generalises. **`GestureTrigger`** and its six typed wrappers are headless the same way: they hand your element a
`data-rask-gesture` bundle, and the shared client runs the capability in the gesture. That makes normally-WASM-only,
activation-gated APIs reachable **declaratively on the Server host** (they're still not injectable there).
Capabilities that return a value (the eyedropper's hex, the install outcome) post it back to an
`OnResult` / `OnColor` / `OnOutcome` callback; the two `<video>` triggers target an element via its `ElementRef`.

```csharp
FullscreenTrigger(g => Button(Type: "button", Data: g)["Full screen"])
ScreenOrientationTrigger(Orientation: "landscape",
    g => Button(Type: "button", Data: g)["Lock landscape"])
EyeDropperTrigger(OnColor: hex => { picked = hex; return Task.CompletedTask; },
    g => Button(Type: "button", Data: g)["Pick a colour"])
InstallTrigger(OnOutcome: o => { outcome = o; return Task.CompletedTask; },
    g => Button(Type: "button", Data: g)["Install app"])
MediaCaptureTrigger(For: preview, Video: true,
    Template: g => Button(Type: "button", Data: g)["Start camera"])
PictureInPictureTrigger(For: preview,
    Template: g => Button(Type: "button", Data: g)["Pop out video"])
```

All six ship: `FullscreenTrigger`, `ScreenOrientationTrigger`, `EyeDropperTrigger`, `InstallTrigger`,
`MediaCaptureTrigger`, and `PictureInPictureTrigger`. See the [capability matrix](browser-capabilities.md).

## WASM-only APIs — `Rask.Wasm.Browser`

Registered only by the WASM host. Each needs something neither the Server transport nor a native WebView
provides — the installed-PWA instance / live document, or a browser-only device API.

| Service | Wraps | What it does | Why WASM-only |
| --- | --- | --- | --- |
| `IFullscreen` | Fullscreen API | Present an element/page fullscreen | transient activation |
| `IScreenOrientation` | Screen Orientation API | Read / lock orientation (lock needs fullscreen) | live document |
| `IInstallPrompt` | `beforeinstallprompt` | Custom "Install app" button: capture + replay the deferred prompt | live document + activation |
| `IMediaDevices` | `getUserMedia` / `getDisplayMedia` | Capture camera / mic / screen into a `<video>` (calls, capture) | transient activation + secure context |
| `IEyeDropper` | EyeDropper API | Pick a color from anywhere on screen (design tools) | transient activation |
| `IPictureInPicture` | Picture-in-Picture API | Float a `<video>` into an always-on-top miniplayer | transient activation |
| `IIdleDetector` | Idle Detection API | Notice when the user goes idle / the screen locks (auto-lock, presence) | activation + live document |
| `ISerial` | Web Serial API | Talk to a serial device (Arduino / microcontroller, GPS, USB-to-serial) — open, write, read | transient activation + secure context |
| `IUsb` | WebUSB API | Pair with and drive a USB device — open, claim an interface, bulk/interrupt/control transfers | transient activation + secure context |
| `IHid` | WebHID API | Talk to a HID device (custom gamepads, sim controls, POS) — output/feature reports + pushed input reports | transient activation + secure context |
| `IBluetooth` | Web Bluetooth API | Pair with a BLE device — connect GATT, read/write characteristics, subscribe to notifications | transient activation + secure context |

PWA infrastructure (the typed `WebAppManifest`, the default service worker, `--pwa` templates) is
covered separately in the [Mobile & PWA guide](pwa.md).

## Subscriptions — the push pattern

Most wrappers are one-shot request/response. Several are **subscriptions**, where the browser *pushes*
each change back into C#:

- **`IBroadcastChannel`** — `OpenAsync(name, onMessage)` → connection (`PostAsync`, `IAsyncDisposable`)
- **`IIntersectionObserver`** — `ObserveAsync(elementRef, onChange, options?)` → `IAsyncDisposable`
- **`IResizeObserver`** — `ObserveAsync(elementRef, onChange)` → `IAsyncDisposable`
- **`IMutationObserver`** — `ObserveAsync(elementRef, onChange, options?)` → `IAsyncDisposable`
- **`IMediaSession.SetActionHandlerAsync`** — `SetActionHandlerAsync(action, onAction)` → `IAsyncDisposable`
- **`IDeviceOrientation`** / **`IDeviceMotion`** — `WatchAsync(onReading)` → `IAsyncDisposable`
- **`IGeolocation.WatchAsync`** — `WatchAsync(onPosition, options?)` → `IAsyncDisposable`
- **`IGamepad`** — `WatchAsync(onReading)` → `IAsyncDisposable` (a `requestAnimationFrame` poll pushed on change)
- **`IIdleDetector`** *(WASM)* — `WatchAsync(onChange, thresholdSeconds?)` → `IAsyncDisposable`
- **`ISerial`** *(WASM)* — `RequestPortAsync(options, onData, onClosed?)` → `ISerialPort?` (the read loop pushes inbound bytes to `onData`; `onClosed` fires if the device is unplugged; dispose the port to stop)
- **`IHid`** *(WASM)* — `IHidDevice.WatchInputReportsAsync(onReport, onDisconnect?)` → `IAsyncDisposable` (each input report is pushed to `onReport`; `onDisconnect` fires if the device is unplugged; dispose to stop)
- **`IBluetooth`** *(WASM)* — `IBluetoothCharacteristic.WatchAsync(onValue)` pushes each notified value; `IBluetoothDevice.WatchDisconnectAsync(onDisconnect)` fires on GATT disconnect — both return `IAsyncDisposable`

They share one mechanism: the JS event invokes a static `[JSInvokable]` via
`window.DotNet.invokeMethodAsync` (which Rask implements on **both** transports), routed back to your
handler by an id — so there's a single implementation, no `DotNetObjectReference` marshalling, and it's
rooted for the WASM trimmer. The observers additionally hand the observed element across as an
[`ElementRef`](js-interop.md#element-refs).

**Lifecycle.** Open from a lifecycle hook (e.g. `OnRenderedAsync(firstRender)`) and **dispose** the
returned handle on unmount (implement `IAsyncDisposable` on the component). A handler that updates state
calls `StateHasChanged()` — the same pattern as subscribing to a background feed. That's a subscription
handler, **not** a generated-factory callback, so [RASK026](diagnostics.md) (which forbids
`StateHasChanged` inside `OnChange`/`OnClick`/`Bind`/… callbacks) does not apply.

```csharp
public sealed class LazyImages(IIntersectionObserver io) : Component, IAsyncDisposable
{
    private readonly ElementRef _sentinel = ElementRef.New();
    private IAsyncDisposable? _obs;

    protected override Component? Render() => Div(Ref: _sentinel)[ /* … */ ];

    protected override async Task OnRenderedAsync(bool first)
    {
        if (!first) return;
        _obs = await io.ObserveAsync(_sentinel, e =>
        {
            if (e.IsIntersecting) LoadMore();   // update state…
            StateHasChanged();                  // …and the framework re-renders
            return Task.CompletedTask;
        }, new IntersectionOptions { RootMargin = "200px" });
    }

    public async ValueTask DisposeAsync() { if (_obs is not null) await _obs.DisposeAsync(); }
}
```

## API reference — live demos

Every wrapper below runs live and identically on both transports. Each demo shows its C# source beside
the running result (some are device/permission-dependent and no-op in a headless or desktop browser —
try them on a phone). The WASM-only device APIs (Serial, USB, HID, Bluetooth) and the installation/PWA
APIs live in the [Mobile & PWA guide](pwa.md).

### Storage & persistence

**`IBrowserStorage`** — typed, awaitable `localStorage` / `sessionStorage`.

<!-- demo:browser-storage -->

**`IIndexedDb`** — a persistent, asynchronous key/value store, far larger than localStorage and non-blocking.

<!-- demo:browser-indexeddb -->

**`ICookies`** — read/write non-HttpOnly cookies with typed `CookieOptions`.

<!-- demo:browser-cookies -->

**`IStorageEstimator`** — the origin's storage quota and usage, to budget a cache.

<!-- demo:browser-storage-estimate -->

### Environment & capabilities

**`INavigatorInfo`** — read-only navigator facts: `onLine`, `language`, `userAgent`.

<!-- demo:browser-navigator-info -->

**`INetworkInfo`** — connection quality (effective type, downlink, RTT, Data Saver) to adapt loading.

<!-- demo:browser-network -->

**`IScreenInfo`** — display size, colour depth, and device pixel ratio.

<!-- demo:browser-screen -->

**`IVisualViewport`** — the actually-visible viewport: size, offset, and pinch-zoom scale.

<!-- demo:browser-visual-viewport -->

**`IMediaQuery`** — evaluate CSS media queries and preferences (dark mode, reduced motion) from C#.

<!-- demo:browser-media-query -->

**`IPageVisibility`** — whether the page is foreground/visible.

<!-- demo:browser-page-visibility -->

**`IPerformance`** — a high-resolution monotonic clock and page-load (Navigation Timing) metrics.

<!-- demo:browser-performance -->

**`IPermissions`** — check a feature's permission state before triggering a prompt.

<!-- demo:browser-permissions -->

### Location, sensors & input

**`IGeolocation`** — one-shot device position.

<!-- demo:browser-geolocation -->

**`IGeolocation.WatchAsync`** — track position live; the browser pushes each fix to C#.

<!-- demo:browser-geolocation-watch -->

**`IDeviceOrientation` / `IDeviceMotion`** — gyroscope/compass and accelerometer readings.

<!-- demo:browser-device-sensors -->

**`IGamepad`** — connected game controllers (sticks, triggers, buttons); prefer WASM for twitch input.

<!-- demo:browser-gamepad -->

**`IVibration`** — pulse the device's vibration motor (mobile).

<!-- demo:browser-vibration -->

### Observers

The push pattern above, one element at a time.

**`IIntersectionObserver`** — notified when an element enters or leaves the viewport.

<!-- demo:browser-intersection -->

**`IResizeObserver`** — notified when an element's size changes.

<!-- demo:browser-resize -->

**`IMutationObserver`** — notified when an element's children, attributes, or text change.

<!-- demo:browser-mutation -->

### Media, crypto & files

**`IClipboard`** — copy to and read from the system clipboard.

<!-- demo:browser-clipboard -->

**`ISpeechSynthesis`** — speak text aloud from C#.

<!-- demo:browser-speech -->

**`IMediaSession`** — publish now-playing metadata to the OS and handle hardware media keys.

<!-- demo:browser-media-session -->

**`ICrypto`** — cryptographically strong randomness and SHA hashing (the Web Crypto API).

<!-- demo:browser-crypto -->

**`IFileSystemAccess`** — open a file, edit it, and save it back to the same file (Chromium-family).

<!-- demo:browser-file-system -->

**`IWebAuthn`** — register and sign in with a passkey instead of a password.

<!-- demo:browser-webauthn -->

**`IBroadcastChannel`** — send messages between same-origin tabs (open this guide in a second tab to try it).

<!-- demo:browser-broadcast-channel -->

**`IWebLocks`** — serialise work across an origin's tabs/workers: `RequestAsync(name, work)` waits for the
named lock, runs `work` while holding it, then releases (even if `work` throws); `TryRequestAsync` returns
`false` without waiting when the lock is already held. Open this guide in a second tab and click "Hold" in
both to watch one wait for the other.

<!-- demo:browser-web-locks -->

**`INotifications` + `IBadge`** — raise a local notification and set the app-icon badge from the page. In the
[native shell](native.md) these resolve to real OS backends (UNUserNotificationCenter / NotificationManager and
the native app-icon badge) that a WebView cannot provide; on Server/WASM they use the browser's Notifications
and Badging APIs (a badge only shows on an installed PWA). On iOS the badge is numeric-only.

<!-- demo:browser-notifications -->

**`Shareable`** *(`Rask.Core` — all hosts)* — headless share: hand *your* element the `data-rask-share`
attribute and its click opens the OS share sheet, on every host including Server (the shared client fires
`navigator.share` in the click gesture, so the activation survives), upgrading to a native backend in the
shell. For a code-driven share on the in-process hosts, inject **`IShare`** (`Rask.Client.Browser`) instead.

<!-- demo:browser-share -->

**`GestureTrigger` + six typed triggers** *(`Rask.Core` — all hosts)* — headless gesture bridge: hand *your*
element the `data-rask-gesture` attribute and its click runs an activation-gated API in the gesture, so it works
on Server too, where the imperative service can't be injected. Ships `FullscreenTrigger`,
`ScreenOrientationTrigger`, `EyeDropperTrigger`, `InstallTrigger`, `MediaCaptureTrigger`, and
`PictureInPictureTrigger`. See [Gesture bridge](#gesture-bridge-activation-gated-apis-on-the-server-host).

<!-- demo:browser-gesture-bridge -->

## Notes

- **Secure context.** Clipboard, geolocation, notifications, push, `crypto.subtle`, and others require
  HTTPS or `localhost`.
- **Permission/support gating.** Many APIs expose `IsSupportedAsync()` and/or pair with `IPermissions`;
  check before triggering a prompt, and `try/catch` the call.
- **Trimming (WASM).** Types these APIs deserialize are registered in source-gen JSON contexts, and the
  push APIs' `[JSInvokable]` methods are `[DynamicDependency]`-rooted, so everything stays correct in a
  `PublishTrimmed` app.

See also: [JS interop](js-interop.md) · [Mobile & PWA](pwa.md).
