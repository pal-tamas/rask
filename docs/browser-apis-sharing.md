# Browser APIs — the sharing model

Where each wrapper lives, how declarative and imperative sharing differ, and how subscriptions push updates back into C#.

‹ Back to [Browser APIs](browser-apis.md)

## Shared APIs — `Rask.Core.Browser`

Work identically on Server and WASM. **Shape** is *one-shot* (a request/response call) or
*subscription* (you hold an `IAsyncDisposable` and the browser **pushes** updates to a C# handler — see
[Subscriptions](#subscriptions--the-push-pattern)). Storage, clipboard, geolocation, `matchMedia`, the screen, crypto,
permissions, `BroadcastChannel`, media session and the rest of what the browser ships are MDN's own surface in
[`Rask.Web`](web-apis.md), not wrappers.

| Service | Wraps | What it does | Shape |
| --- | --- | --- | --- |
| `ICookies` | `document.cookie` | Read/write cookies with typed `CookieOptions` | one-shot |
| `ISpeechSynthesis` | `window.speechSynthesis` | Speak text aloud; cancel | one-shot |
| `ISpeechRecognition` | `webkitSpeechRecognition` | Dictation — spoken audio → text | **subscription** |
| `IDeviceOrientation` | `deviceorientation` | Gyroscope/compass tilt angles (tilt UI, AR, compass) | **subscription** |
| `IDeviceMotion` | `devicemotion` | Accelerometer / rotation rate (shake, step counter, motion games) | **subscription** |
| `IStorageEstimator` | `navigator.storage.estimate` | Quota / usage, to budget caches | one-shot |
| `IIndexedDb` | IndexedDB | `OpenStoreAsync(name)` → large async key/value store | one-shot |
| `IFileSystemAccess` | File System Access API | Open/save a file *back to disk* + directory access (editors) | one-shot |
| `IWebAuthn` | Web Authentication API | Passkeys — register / sign in with biometric or security key | one-shot |
| `IWebLocks` | Web Locks API | Serialise work across an origin's tabs/workers — hold a named lock for a callback | callback-scoped |
| `IMediaStreams` | `MediaStream` | Attach a live stream to a `<video>`, or stop it (releasing the camera) | one-shot |
| `ISignaling` | WebSocket | Join a room on Rask's signaling relay and pass payloads to one peer | **subscription** |
| `IWebRtc` | WebRTC | Peer-to-peer data channels between two browsers (you supply the signaling) | **subscription** |
| `IGamepad` | Gamepad API | Connected controllers — sticks / triggers / buttons (browser games) | **subscription** |
| `IWebPush` | Push API | Subscribe to Web Push (returns a `PushSubscription`); send from the backend with [`Rask.WebPush`](pwa.md#sending-from-your-backend-raskwebpush) | one-shot |
| `INotifications` | Notifications API | Show a local notification from the page | one-shot |
| `IBadge` | Badging API | Set/clear a count on the installed app icon | one-shot |
| `IWakeLock` | Screen Wake Lock API | Keep the screen awake (sentinel; dispose to release) | one-shot |

The last four are **PWA** APIs but transport-agnostic (`IJSRuntime`-backed, no transient activation), so they
register on Server too — their JS helpers just ship on the Server client only under `AddRaskPwa` (see
[pwa.md](pwa.md)).

## Sharing — declarative (all hosts) vs imperative (in-process)

**`Shareable`** (`Rask.Core`) is the all-host way to share, and it's **headless** — you render the trigger,
it hands you the `data-rask-share` attribute to spread onto it:

```csharp
Shareable.Data(new ShareData { Title = "Rask", Url = "https://…" })
    .Template(share => Button.Type("button").Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500").Data(share)["Share"])
```

The shared client fires `navigator.share` **inside the click gesture** — no round-trip, so the transient
user activation survives even on the Server transport. Because it's headless, the trigger can be any element
with a `Data` prop (a link, an icon button, a `Ui.Button`), not just a `<button>`. Web Share is available on
mobile Safari / Android Chrome / Edge (not desktop Firefox); an unsupported browser no-ops.

**`Navigator.Share(…)`** ([`Rask.Web`](web-apis.md#what-only-webassembly-runs)) is the **imperative** path — share
from *code* (a lifecycle hook, after an `await`). That needs the in-process transport to keep the activation, so it
compiles only in a **WASM** app.

| API | Home | Hosts | Use |
| --- | --- | --- | --- |
| `Shareable` | `Rask.Core` | **all** (Server too) | Headless declarative share — attaches `data-rask-share` to your element; fires `navigator.share` in the gesture |
| `Navigator.Share(…)` | `Rask.Web` | WASM | Imperative share from code |

### Gesture bridge — activation-gated APIs on the Server host

`Shareable`'s trick — run the call **inside the click gesture** so the transient user activation survives —
generalises. **`Trigger.Gesture`** and its six typed wrappers are headless the same way: they hand your element a
`data-rask-gesture` bundle, and the shared client runs the capability in the gesture. That makes normally-WASM-only,
activation-gated APIs reachable **declaratively on the Server host** (they're still not injectable there).
Capabilities that return a value (the eyedropper's hex, the install outcome) post it back to an
`OnResult` / `OnColor` / `OnOutcome` callback; the two `<video>` triggers target an element via its `ElementRef`.

```csharp
Trigger.Fullscreen.Template(g => Button.Type("button").Data(g)["Full screen"])
Trigger.ScreenOrientation.Orientation("landscape")
    .Template(g => Button.Type("button").Data(g)["Lock landscape"])
Trigger.EyeDropper.Template(g => Button.Type("button").Data(g)["Pick a colour"])
    .OnColor(hex => picked = hex)
Trigger.Install.Template(g => Button.Type("button").Data(g)["Install app"])
    .OnOutcome(o => outcome = o)
Trigger.MediaCapture.For(preview).Template(g => Button.Type("button").Data(g)["Start camera"])
    .Video()
    // Keeps the stream reachable from C# — the only way a Server-hosted app can stop it later.
    .OnStream(id => camera = id)
Trigger.PictureInPicture.For(preview).Template(g => Button.Type("button").Data(g)["Pop out video"])
```

The required steps come first — `Template` on every trigger, plus `Orientation` or the target `For` where
the capability needs one; the optional ones follow.

All six ship: `Trigger.Fullscreen`, `Trigger.ScreenOrientation`, `Trigger.EyeDropper`, `Trigger.Install`,
`Trigger.MediaCapture`, and `Trigger.PictureInPicture`. See the [capability matrix](browser-capabilities.md).

## WASM-only APIs — `Rask.Wasm.Browser`

Registered only by the WASM host. Each needs something the Server transport cannot provide — the
installed-PWA instance / live document, or a browser-only device API. WebUSB and WebHID are `Navigator.Usb` and
`Navigator.Hid` in [`Rask.Web`](web-apis.md#what-only-webassembly-runs).

| Service | Wraps | What it does | Why WASM-only |
| --- | --- | --- | --- |
| `IFullscreen` | Fullscreen API | Present an element/page fullscreen | transient activation |
| `IInstallPrompt` | `beforeinstallprompt` | Custom "Install app" button: capture + replay the deferred prompt | live document + activation |
| `IMediaDevices` | `getUserMedia` / `getDisplayMedia` | Capture camera / mic / screen into a `<video>` (calls, capture) | transient activation + secure context |
| `IPictureInPicture` | Picture-in-Picture API | Float a `<video>` into an always-on-top miniplayer | transient activation |
| `ISerial` | Web Serial API | Talk to a serial device (Arduino / microcontroller, GPS, USB-to-serial) — open, write, read | transient activation + secure context |
| `IBluetooth` | Web Bluetooth API | Pair with a BLE device — connect GATT, read/write characteristics, subscribe to notifications | transient activation + secure context |
| `IBackgroundSync` | Background Sync + Periodic Background Sync | Ask the browser to wake the app when connectivity returns, or on a schedule, to drain an offline queue | service-worker registration |

PWA infrastructure (the typed `WebAppManifest`, the default service worker, `--pwa` templates) is
covered separately in the [Mobile & PWA guide](pwa.md).

## Subscriptions — the push pattern

Most wrappers are one-shot request/response. Several are **subscriptions**, where the browser *pushes*
each change back into C#:

- **`ISignaling`** — `JoinAsync(room, handlers, path?)` → connection (`SendAsync`, `IAsyncDisposable`);
  pairs with `AddRaskSignaling()` / `MapRaskSignaling()` on the server
- **`IWebRtc`** — `CreateAsync(config, handlers)` → connection (`IAsyncDisposable`); its channels'
  `ListenAsync(onMessages)` delivers **batches**, not single messages — on Server each push is a WebSocket
  frame, so the framework coalesces them
- **`IDeviceOrientation`** / **`IDeviceMotion`** — `WatchAsync(onReading)` → `IAsyncDisposable`
- **`IGamepad`** — `WatchAsync(onReading)` → `IAsyncDisposable` (a `requestAnimationFrame` poll pushed on change)
- **`ISerial`** *(WASM)* — `RequestPortAsync(options, onData, onClosed?)` → `ISerialPort?` (the read loop pushes inbound bytes to `onData`; `onClosed` fires if the device is unplugged; dispose the port to stop)
- **`IBluetooth`** *(WASM)* — `IBluetoothCharacteristic.WatchAsync(onValue)` pushes each notified value; `IBluetoothDevice.WatchDisconnectAsync(onDisconnect)` fires on GATT disconnect — both return `IAsyncDisposable`

They share one mechanism: the JS event invokes a static `[JSInvokable]` via
`window.DotNet.invokeMethodAsync` (which Rask implements on **both** transports), routed back to your
handler by an id — so there's a single implementation, no `DotNetObjectReference` marshalling, and it's
rooted for the WASM trimmer. A `Rask.Web` object's events are `On{Event}` subscriptions instead — see
[Web APIs from MDN](web-apis.md#events-and-callbacks).

**Lifecycle.** Open from a lifecycle hook (e.g. `OnFirstRender()`) and **dispose** the
returned handle on unmount (implement `IAsyncDisposable` on the component). A handler that updates state
calls `StateHasChanged()` — the same pattern as subscribing to a background feed. That's a subscription
handler, **not** a chain-set callback, so [RASK026](diagnostics.md) (which forbids
`StateHasChanged` inside `OnChange`/`OnClick`/`Bind`/… callbacks) does not apply.

```csharp
using Rask.Web;

public sealed partial class LazyImages : Component
{
    private readonly ElementRef _sentinel = ElementRef.New();
    private Rask.Web.Types.IntersectionObserver? _io;

    protected override Component? Render() => Div.Ref(_sentinel)[ /* … */ ];

    // The observers are MDN's own, from Rask.Web (docs/web-apis.md): the handler runs in this component and
    // re-renders it, so it needs no StateHasChanged.
    protected override async Task OnFirstRender()
    {
        _io ??= await IntersectionObserver.Create(entries =>
        {
            if (entries.Any(e => e.IsIntersecting)) LoadMore();
        }, new() { RootMargin = "200px" });
        await _io.Observe(_sentinel);
    }

    protected override async Task OnUnmount()
    {
        if (_io is not null) await _io.DisposeAsync();
    }
}
```
