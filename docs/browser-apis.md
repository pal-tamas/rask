# Browser APIs

Rask ships **typed C# wrappers over the browser's Web APIs** — inject one through a component
constructor and call it, instead of hand-writing `IJSRuntime` identifiers and getting the JSON
shape right yourself. Each is a thin, awaitable layer over the same unified
[`IJSRuntime`](js-interop.md#calling-js-from-c-ijsruntime), so it works the same way whether your
app runs on the **Server** (WebSocket) or **WASM** (`JSImport`/`JSExport`) transport.

This page is the **map of the whole surface**. For the deeper "why" — user activation, the
transport seam, element refs — see [JS interop → Typed browser APIs](js-interop.md#typed-browser-apis);
for the mobile/PWA angle see the [Mobile & PWA guide](pwa.md). Every wrapper has a runnable demo in
the **Browser APIs** section of the [showcase](https://pal-tamas.github.io/rask/).

## Two homes, one rule

- **`Rask.Core.Browser`** — APIs that work on **both transports**. Registered by both hosts.
- **`Rask.Wasm.Browser`** — APIs that **can't** work over the Server round-trip (they need *transient*
  user activation, or the installed-PWA instance / live document). Registered only by the WASM host.

> **The rule:** shared APIs live in `Rask.Core.Browser`; APIs that can't run on Server live in
> `Rask.Wasm.Browser`. If you inject a WASM-only service on Server, it simply isn't registered.

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
| `IBroadcastChannel` | `BroadcastChannel` | Cross-tab messaging | **subscription** |
| `IIntersectionObserver` | `IntersectionObserver` | Element enters/leaves the viewport (lazy-load, infinite scroll) | **subscription** |
| `IResizeObserver` | `ResizeObserver` | Element's size changes (container-responsive layout) | **subscription** |
| `IMutationObserver` | `MutationObserver` | Element's children/attributes/text change (react to externally-written DOM) | **subscription** |
| `IGamepad` | Gamepad API | Connected controllers — sticks / triggers / buttons (browser games) | **subscription** |

## WASM-only APIs — `Rask.Wasm.Browser`

Registered only by the WASM host. Each needs something the Server/WebSocket transport can't provide —
*transient* user activation (preserved only when the interop call runs inside the click's call stack,
which happens in-process on WASM), or the installed-PWA instance / live document.

| Service | Wraps | What it does | Why WASM-only |
| --- | --- | --- | --- |
| `IShare` | `navigator.share` | Hand a link/text to the OS share sheet | transient activation |
| `IFullscreen` | Fullscreen API | Present an element/page fullscreen | transient activation |
| `IWebPush` | Push API | Subscribe to Web Push (returns a `PushSubscription`); send from the backend with [`Rask.WebPush`](pwa.md#sending-from-your-backend-raskwebpush) | service worker + installed PWA |
| `INotifications` | Notifications API | Show a local notification from the page | permission needs a live gesture |
| `IBadge` | Badging API | Set/clear a count on the installed app icon | installed-PWA instance |
| `IWakeLock` | Screen Wake Lock API | Keep the screen awake (sentinel; dispose to release) | tied to the live document |
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

## Notes

- **Secure context.** Clipboard, geolocation, notifications, push, `crypto.subtle`, and others require
  HTTPS or `localhost`.
- **Permission/support gating.** Many APIs expose `IsSupportedAsync()` and/or pair with `IPermissions`;
  check before triggering a prompt, and `try/catch` the call.
- **Trimming (WASM).** Types these APIs deserialize are registered in source-gen JSON contexts, and the
  push APIs' `[JSInvokable]` methods are `[DynamicDependency]`-rooted, so everything stays correct in a
  `PublishTrimmed` app.

See also: [JS interop](js-interop.md) · [Mobile & PWA](pwa.md).
