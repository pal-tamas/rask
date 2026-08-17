# Browser APIs — the sharing model

Where each wrapper lives, how declarative and imperative sharing differ, and how subscriptions push updates back into C#.

‹ Back to [Browser APIs](browser-apis.md)

## Shared APIs — `Rask.Core.Browser`

Work identically on Server and WASM. **Shape** is *one-shot* (a request/response call) or
*subscription* (you hold an `IAsyncDisposable` and the browser **pushes** updates to a C# handler — see
[Subscriptions](#subscriptions--the-push-pattern)).

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
| `IBattery` | Battery Status API | Charge level + charging state (native OS backend on the shell) | one-shot + **subscription** |
| `IMediaQuery` | `window.matchMedia` | Evaluate a query; `PrefersDarkAsync` / `PrefersReducedMotionAsync` | one-shot |
| `ISpeechSynthesis` | `window.speechSynthesis` | Speak text aloud; cancel | one-shot |
| `ISpeechRecognition` | `webkitSpeechRecognition` | Dictation — spoken audio → text (native OS backend on the shell) | **subscription** |
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
| `IMediaStreams` | `MediaStream` | Attach a live stream to a `<video>`, or stop it (releasing the camera) | one-shot |
| `ISignaling` | WebSocket | Join a room on Rask's signaling relay and pass payloads to one peer | **subscription** |
| `IWebRtc` | WebRTC | Peer-to-peer data channels between two browsers (you supply the signaling) | **subscription** |
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
see the [capability matrix](browser-capabilities.md) and the [Native guide](native-devices.md#native-device-backends).

## Sharing — declarative (all hosts) vs imperative (in-process)

**`Shareable`** (`Rask.Core`) is the all-host way to share, and it's **headless** — you render the trigger,
it hands you the `data-rask-share` attribute to spread onto it:

```csharp
Shareable(new ShareData { Title = "Rask", Url = "https://…" },
    share => Button.Type("button").Class("btn btn-primary").Data(share)["Share"])
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
`Intent.ACTION_SEND` backend that needs no activation — see the [Native guide](native-devices.md#native-device-backends).

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
FullscreenTrigger(g => Button.Type("button").Data(g)["Full screen"])
ScreenOrientationTrigger(Orientation: "landscape",
    g => Button.Type("button").Data(g)["Lock landscape"])
EyeDropperTrigger(OnColor: hex => { picked = hex; return Task.CompletedTask; },
    g => Button.Type("button").Data(g)["Pick a colour"])
InstallTrigger(OnOutcome: o => { outcome = o; return Task.CompletedTask; },
    g => Button.Type("button").Data(g)["Install app"])
MediaCaptureTrigger.For(preview).Video(true)
    // Keeps the stream reachable from C# — the only way a Server-hosted app can stop it later.
    .OnStream(id => { camera = id; StateHasChanged(); return Task.CompletedTask; })
    .Template(g => Button.Type("button").Data(g)["Start camera"])
PictureInPictureTrigger.For(preview).Template(g => Button.Type("button").Data(g)["Pop out video"])
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
- **`ISignaling`** — `JoinAsync(room, handlers, path?)` → connection (`SendAsync`, `IAsyncDisposable`);
  pairs with `AddRaskSignaling()` / `MapRaskSignaling()` on the server
- **`IWebRtc`** — `CreateAsync(config, handlers)` → connection (`IAsyncDisposable`); its channels'
  `ListenAsync(onMessages)` delivers **batches**, not single messages — on Server each push is a WebSocket
  frame, so the framework coalesces them
- **`IIntersectionObserver`** — `ObserveAsync(elementRef, onChange, options?)` → `IAsyncDisposable`
- **`IResizeObserver`** — `ObserveAsync(elementRef, onChange)` → `IAsyncDisposable`
- **`IMutationObserver`** — `ObserveAsync(elementRef, onChange, options?)` → `IAsyncDisposable`
- **`IMediaSession.SetActionHandlerAsync`** — `SetActionHandlerAsync(action, onAction)` → `IAsyncDisposable`
- **`IDeviceOrientation`** / **`IDeviceMotion`** — `WatchAsync(onReading)` → `IAsyncDisposable`
- **`IBattery`** — `WatchAsync(onChange)` → `IAsyncDisposable` (plus a one-shot `GetStatusAsync`)
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
[`ElementRef`](js-interop-runtime.md#element-refs).

**Lifecycle.** Open from a lifecycle hook (e.g. `OnRenderedAsync(firstRender)`) and **dispose** the
returned handle on unmount (implement `IAsyncDisposable` on the component). A handler that updates state
calls `StateHasChanged()` — the same pattern as subscribing to a background feed. That's a subscription
handler, **not** a chain-set callback, so [RASK026](diagnostics.md) (which forbids
`StateHasChanged` inside `OnChange`/`OnClick`/`Bind`/… callbacks) does not apply.

```csharp
public sealed partial class LazyImages(IIntersectionObserver io) : Component, IAsyncDisposable
{
    private readonly ElementRef _sentinel = ElementRef.New();
    private IAsyncDisposable? _obs;

    protected override Component? Render() => Div.Ref(_sentinel)[ /* … */ ];

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
