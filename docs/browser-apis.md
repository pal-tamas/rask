# Browser APIs

Rask reaches the browser's Web APIs two ways. Most are MDN's own surface, generated into C# by
[`Rask.Web`](web-apis.md): `await Navigator.Clipboard.WriteText("hi")`, `await LocalStorage.GetItem("theme")`. The
rest are **typed C# wrappers** — inject one through a component constructor and call it, instead of hand-writing
`IJSRuntime` identifiers and getting the JSON shape right yourself. Each is a thin, awaitable layer over the same unified
[`IJSRuntime`](js-interop-runtime.md#calling-js-from-c-ijsruntime), so it works the same way whether your
app runs on the **Server** (WebSocket) or **WASM** (`JSImport`/`JSExport`) transport.

This page is the **map of the whole surface**: where each wrapper lives, how sharing and the gesture triggers keep
a click's activation on the Server host, and how subscriptions push updates back into C#. For an at-a-glance view
of *where each wrapper works* (Server / WASM), see the
[**capability table**](browser-apis-reference.md#where-each-wrapper-works) — it links to a dedicated reference page
per API under [`docs/apis/`](apis/). For the deeper "why" — user activation, the transport seam, element
refs — see [JS interop → Typed browser APIs](js-interop-runtime.md#typed-browser-apis); for the mobile/PWA
angle see the [Mobile & PWA guide](pwa.md). Every wrapper has a runnable demo in the
[showcase](https://rask.sh/docs/), under **Browser APIs** — except the WASM-only tier, which gets its own page under
**PWA** because it needs something the Server transport can't give it. (The activation-gated ones appear in both: as gesture components
under Browser APIs, and as injectable services or Rask.Web calls under PWA.)

## Two homes, one rule

- **`Rask.Core.Browser`** — APIs that work on **every host** (Server + WASM). Registered by both.
- **`Rask.Wasm.Browser`** — APIs only the WASM host can run: they need the installed-PWA instance, a live
  document, a device chooser, or *transient* user activation — preserved only when the interop call runs
  inside the click's own call stack, which the Server's WebSocket round-trip loses.

> **The rule:** shared-everywhere APIs live in `Rask.Core.Browser`, WASM-only ones in
> `Rask.Wasm.Browser`. A host simply doesn't register a service it can't provide.

Call from an **event handler or lifecycle hook**, never from `Render()`. A typed wrapper is injected
through the **constructor** (not a settable property — that would become a required chain parameter):

```csharp
public sealed partial class ThemeToggle : Component
{
    protected override async Task OnFirstRender()
    {
        var saved = await LocalStorage.GetItem("theme");     // or: await Document.Cookie
        var dark = saved is null
            ? await Window.MatchMedia("(prefers-color-scheme: dark)").Matches
            : saved == "dark";
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
[Subscriptions](#subscriptions--the-push-pattern)). Storage, clipboard, geolocation, `matchMedia`, the screen, cookies, crypto,
permissions, `BroadcastChannel`, media session, Web Locks, the storage estimate, speech synthesis and recognition, the
screen wake lock, the install prompt, live media streams, animations, files,
gamepads, notifications, the app badge and the rest of what the browser ships are MDN's own surface in
[`Rask.Web`](web-apis.md), not wrappers. Device tilt and motion are `Window` events there:
`await Window.OnDeviceOrientation(e => _angle = e.Alpha, every: 100.Milliseconds)`, where `every:` throttles them in the
browser before they cross.

The wrappers here are `IIndexedDb`, `IWebAuthn`, `ISignaling`, `IWebRtc` and `IViewTransitions`; the
[capability table](browser-apis-reference.md#where-each-wrapper-works) says what each wraps, its shape and its hosts.

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
`Trigger.MediaCapture`, and `Trigger.PictureInPicture`. See the [capability table](browser-apis-reference.md#where-each-wrapper-works).

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

The one wrapper here is `IBackgroundSync` — ask the browser to wake the app when connectivity returns, or on a
schedule, to drain an offline queue. It needs a service-worker registration, which only the WASM host has.

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

## Notes

- **Secure context.** Clipboard, geolocation, notifications, push, `crypto.subtle`, and others require
  HTTPS or `localhost`.
- **Permission/support gating.** Many APIs expose `IsSupported()`; ask
  `Navigator.Permissions.Query(…)` from [`Rask.Web`](web-apis.md) before triggering a prompt, and `try/catch` the call.
- **Trimming (WASM).** Types these APIs deserialize are registered in source-gen JSON contexts, and the
  push APIs' `[JSInvokable]` methods are `[DynamicDependency]`-rooted, so everything stays correct in a
  `PublishTrimmed` app.

## Related pages

- [Web APIs from MDN](web-apis.md) — every web API the browser ships, generated from MDN into C# by `Rask.Web`.
- [Reference & live demos](browser-apis-reference.md) — where each wrapper works, and every API with a runnable demo.
- [IJSRuntime, typed APIs & refs](js-interop-runtime.md) — the transport seam beneath the wrappers, and element refs.
- [JS interop](js-interop.md) · [Mobile & PWA](pwa.md).
