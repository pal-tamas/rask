# Browser APIs — the sharing model

Where each wrapper lives, how declarative and imperative sharing differ, and how subscriptions push updates back into C#.

‹ Back to [Browser APIs](browser-apis.md)

## Shared APIs — `Rask.Core.Browser`

Work identically on Server and WASM. **Shape** is *one-shot* (a request/response call) or
*subscription* (you hold an `IAsyncDisposable` and the browser **pushes** updates to a C# handler — see
[Subscriptions](#subscriptions--the-push-pattern)). Storage, clipboard, geolocation, `matchMedia`, the screen, cookies, crypto,
permissions, `BroadcastChannel`, media session, Web Locks, the storage estimate, speech synthesis and recognition, the
screen wake lock, the install prompt, live media streams, animations, files,
gamepads, notifications, the app badge and the rest of what the browser ships are MDN's own surface in
[`Rask.Web`](web-apis.md), not wrappers. Device tilt and motion are `Window` events there:
`await Window.OnDeviceOrientation(e => _angle = e.Alpha, every: 100.Milliseconds)`, where `every:` throttles them in the
browser before they cross.

| Service | Wraps | What it does | Shape |
| --- | --- | --- | --- |
| `IIndexedDb` | IndexedDB | `OpenStore(name)` → large async key/value store | one-shot |
| `IWebAuthn` | Web Authentication API | Passkeys — register / sign in with biometric or security key | one-shot |
| `ISignaling` | WebSocket | Join a room on Rask's signaling relay and pass payloads to one peer | **subscription** |
| `IWebRtc` | WebRTC | Peer-to-peer data channels between two browsers (you supply the signaling) | **subscription** |

The screen wake lock is `await Navigator.WakeLock.Request(WakeLockType.Screen)` and dictation is
`await SpeechRecognition.Create()`, both from [`Rask.Web`](web-apis.md#where-a-browser-falls-short) on every host.
Web Push subscription is MDN's own `PushManager`, from [`Rask.Web`](web-apis.md#keeping-an-object).

## Sharing — declarative (all hosts) vs imperative (in-process)

**`Shareable`** (`Rask.Core`) is the all-host way to share, and it's **headless** — you render the trigger,
it hands you the `data-rask-share` attribute to spread onto it:

```csharp
Shareable.Data(new ShareData { Title = "Rask", Url = "https://…" })
    .Template(share => Button.Type(ButtonType.Button).Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-violet-600 text-white hover:bg-violet-500").Data(share)["Share"])
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
Trigger.Fullscreen.Template(g => Button.Type(ButtonType.Button).Data(g)["Full screen"])
Trigger.ScreenOrientation.Orientation("landscape")
    .Template(g => Button.Type(ButtonType.Button).Data(g)["Lock landscape"])
Trigger.EyeDropper.Template(g => Button.Type(ButtonType.Button).Data(g)["Pick a colour"])
    .OnColor(hex => picked = hex)
Trigger.Install.Template(g => Button.Type(ButtonType.Button).Data(g)["Install app"])
    .OnOutcome(o => outcome = o)
Trigger.MediaCapture.For(preview).Template(g => Button.Type(ButtonType.Button).Data(g)["Start camera"])
    .Video()
    // Keeps the stream reachable from C# — the only way a Server-hosted app can stop it later.
    .OnStream(stream => camera = MediaStream.From(stream))
Trigger.PictureInPicture.For(preview).Template(g => Button.Type(ButtonType.Button).Data(g)["Pop out video"])
```

The required steps come first — `Template` on every trigger, plus `Orientation` or the target `For` where
the capability needs one; the optional ones follow.

All six ship: `Trigger.Fullscreen`, `Trigger.ScreenOrientation`, `Trigger.EyeDropper`, `Trigger.Install`,
`Trigger.MediaCapture`, and `Trigger.PictureInPicture`. See the [capability matrix](browser-capabilities.md).

## WASM-only APIs — `Rask.Wasm.Browser`

Registered only by the WASM host. Each needs something the Server transport cannot provide — the
installed-PWA instance / live document, or a browser-only device API. WebUSB, WebHID, Web Serial and Web Bluetooth are
`Navigator.Usb`, `Navigator.Hid`, `Navigator.Serial` and `Navigator.Bluetooth` in
[`Rask.Web`](web-apis.md#what-only-webassembly-runs). Fullscreen and Picture-in-Picture are
`await _stage.RequestFullscreen()` and `await _video.RequestPictureInPicture()` on an element ref there. The camera
and microphone are `await Navigator.MediaDevices.GetUserMedia(new() { Video = new() })` (every host) and screen
capture is `GetDisplayMedia()` (WASM); show the stream with `await _video.SetSrcObject(stream)`.
A custom "Install app" button is `Window.OnBeforeInstallPrompt(e => _prompt = e)`, then `await _prompt.Prompt()` in the
click (WASM); `Trigger.Install` is the declarative one that also works on Server. See
[Where a browser falls short](web-apis.md#where-a-browser-falls-short).

| Service | Wraps | What it does | Why WASM-only |
| --- | --- | --- | --- |
| `IBackgroundSync` | Background Sync + Periodic Background Sync | Ask the browser to wake the app when connectivity returns, or on a schedule, to drain an offline queue | service-worker registration |

PWA infrastructure (the typed `WebAppManifest`, the default service worker, what the templates scaffold) is
covered separately in the [Mobile & PWA guide](pwa.md).

## Subscriptions — the push pattern

Most wrappers are one-shot request/response. Several are **subscriptions**, where the browser *pushes*
each change back into C#:

- **`ISignaling`** — `Join(room, handlers, path?)` → connection (`Send`, `IAsyncDisposable`);
  pairs with `AddRaskSignaling()` / `MapRaskSignaling()` on the server
- **`IWebRtc`** — `Create(config, handlers)` → connection (`IAsyncDisposable`); its channels'
  `Listen(onMessages)` delivers **batches**, not single messages — on Server each push is a WebSocket
  frame, so the framework coalesces them

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
public sealed partial class LazyImages : Component
{
    private readonly ElementRef _sentinel = ElementRef.New();
    private Types.IntersectionObserver? _io;

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
