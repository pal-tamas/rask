# Browser APIs — reference & live demos

Every typed browser wrapper, and the MDN calls from [Rask.Web](web-apis.md) that replaced the rest, with a runnable
demo showing its C# source beside the live result.

‹ Back to [Browser APIs](browser-apis.md)

## API reference — live demos

Every demo below runs live and identically on both transports. Each demo shows its C# source beside
the running result (some are device/permission-dependent and no-op in a headless or desktop browser —
try them on a phone). The WASM-only device APIs (Serial, USB, HID, Bluetooth) and the installation/PWA
APIs live in the [Mobile & PWA guide](pwa.md).

### Storage & persistence

**localStorage / sessionStorage** — `await LocalStorage.SetItem(key, value)` and `await LocalStorage.GetItem(key)`, from [Rask.Web](web-apis.md).

<!-- demo:browser-storage -->

**`IIndexedDb`** — a persistent, asynchronous key/value store, far larger than localStorage and non-blocking. Holds
text (`SetAsync`/`GetAsync`) or raw bytes (`SetBytesAsync`/`GetBytesAsync`, stored as a real `Uint8Array`).

<!-- demo:browser-indexeddb -->

**`ICookies`** — read/write non-HttpOnly cookies with typed `CookieOptions`.

<!-- demo:browser-cookies -->

**`IStorageEstimator`** — the origin's storage quota and usage, to budget a cache.

<!-- demo:browser-storage-estimate -->

### Environment & capabilities

**Navigator facts** — `await Navigator.OnLine`, `await Navigator.Language`, `await Navigator.UserAgent`, from [Rask.Web](web-apis.md).

<!-- demo:browser-navigator-info -->

**Network quality** — `await Navigator.Connection.EffectiveType` (and `Downlink`, `Rtt`, `SaveData`), from [Rask.Web](web-apis.md).

<!-- demo:browser-network -->

**Battery** — `await using var battery = await Navigator.GetBattery();` then `await battery.Level`, from [Rask.Web](web-apis.md).

<!-- demo:browser-battery -->

**Screen** — `await Screen.Width`, `await Screen.ColorDepth` and `await Window.DevicePixelRatio`, from [Rask.Web](web-apis.md).

<!-- demo:browser-screen -->

**Visual viewport** — `await Window.VisualViewport.Width` (and `Height`, `OffsetTop`, `Scale`), from [Rask.Web](web-apis.md).

<!-- demo:browser-visual-viewport -->

**Media queries** — `await Window.MatchMedia("(prefers-color-scheme: dark)").Matches`, from [Rask.Web](web-apis.md).

<!-- demo:browser-media-query -->

**Page visibility** — `await Document.VisibilityState`, from [Rask.Web](web-apis.md).

<!-- demo:browser-page-visibility -->

**`IViewTransitions`** — animate between the old and new DOM instead of the new one just appearing.

The one wrapper here you could not have written yourself. A same-document transition has to *wrap* the
DOM mutation, and the mutation is the framework's morph — there is no point in your code that sits
around it. Enabling routes the live runtime's own commit (diff apply and full-document apply, on both
hosts) through `document.startViewTransition`.

**Off by default**, and off is exactly the previous behaviour: the commit stays synchronous. Style it
with the standard `::view-transition-*` pseudo-elements; give an element a stable
`view-transition-name` and the browser morphs it between routes rather than cross-fading it, which is
what makes a shared header travel. `prefers-reduced-motion` is honoured for you — the animation is the
browser's own default, so there is no stylesheet of yours for the preference to switch off.

`IsActiveAsync()` is deliberately separate from what you set: a toggle can be on while nothing animates
because the browser lacks the API or the reader asked for less motion.

```csharp
await _viewTransitions.SetEnabledAsync(true);
```

**`IWebAnimations`** — run and control an animation on an element from C#, no stylesheet and no
animation library.

Keyframes use the API's *object* form — a property name to the values it moves through — which is what
`Element.animate()` takes natively:

```csharp
var id = await _anim.StartAsync(_card, new Dictionary<string, string[]>
{
    ["opacity"] = ["0", "1"],
    ["transform"] = ["translateY(8px)", "none"],
}, new AnimationOptions(DurationMs: 200, Easing: "ease-out", Fill: "forwards"));

await _anim.WaitAsync(id);   // true if it finished, false if it was cancelled — never throws
```

`StartAsync` returns a handle (`AnimationId`) because an `Animation` object cannot cross interop — the
same shape `MediaStreamId` uses. On a browser without the API the handle is simply invalid rather than
an error, so you can animate without feature-testing first. `Iterations: -1` means forever (JSON has no
`Infinity` literal). `Cancel`/`Finish`/`Pause`/`Play` are all harmless on a handle that has already
finished.

Unlike `IViewTransitions`, **reduced motion is yours to decide here** — these are your animations, and
only you know whether a given one is a loading affordance or decoration. Read the preference with
`await Window.MatchMedia("(prefers-reduced-motion: reduce)").Matches` and skip what should be skipped.

**Performance** — `await Performance.Now()`, a high-resolution monotonic clock, from [Rask.Web](web-apis.md).

<!-- demo:browser-performance -->

**Permissions** — `await Navigator.Permissions.Query(new() { Name = "geolocation" })`, from [Rask.Web](web-apis.md); read
`State` before triggering a prompt.

<!-- demo:browser-permissions -->

### Location, sensors & input

**Geolocation** — `await Navigator.Geolocation.GetCurrentPosition(p => _where = p.Coords)`, from [Rask.Web](web-apis.md).

<!-- demo:browser-geolocation -->

**Live position** — `await Navigator.Geolocation.WatchPosition(p => …)`, from [Rask.Web](web-apis.md); each fix runs the handler.

<!-- demo:browser-geolocation-watch -->

**`IDeviceOrientation` / `IDeviceMotion`** — gyroscope/compass and accelerometer readings.

<!-- demo:browser-device-sensors -->

**Gamepad** — `await Navigator.GetGamepads()`, then each pad's `Buttons` and `Axes`, and
`Window.OnGamepadConnected(e => …)`, from [Rask.Web](web-apis.md); prefer WASM for twitch input.

<!-- demo:browser-gamepad -->

**Vibration** — `await Navigator.Vibrate(200)`, from [Rask.Web](web-apis.md) (mobile).

<!-- demo:browser-vibration -->

### Observers

MDN's own `IntersectionObserver`, `ResizeObserver` and `MutationObserver`, from [Rask.Web](web-apis.md#events-and-callbacks):
`await IntersectionObserver.Create(entries => …)`, then `await observer.Observe(_ref)`. The handler gets the entries as
data and re-renders its component.

**IntersectionObserver** — notified when an element enters or leaves the viewport.

<!-- demo:browser-intersection -->

**ResizeObserver** — notified when an element's size changes.

<!-- demo:browser-resize -->

**MutationObserver** — notified when an element's children or attributes change.

<!-- demo:browser-mutation -->

### Media, crypto & files

**Clipboard** — `await Navigator.Clipboard.WriteText("hi")` and `await Navigator.Clipboard.ReadText()`, from [Rask.Web](web-apis.md).

<!-- demo:browser-clipboard -->

**`ISpeechSynthesis`** — speak text aloud from C#.

<!-- demo:browser-speech -->

**`ISpeechRecognition`** — dictation: spoken audio turned into text, pushed to C# as it is heard.

<!-- demo:browser-speech-recognition -->

**Media session** — `await Navigator.MediaSession.SetMetadata(await MediaMetadata.Create(new() { Title = "…" }))` and
`SetActionHandler(action, details => …)`, from [Rask.Web](web-apis.md); now-playing metadata and hardware media keys.

<!-- demo:browser-media-session -->

**Crypto** — `await Crypto.RandomUUID()`, `await Crypto.GetRandomValues(new byte[16])` and
`await Crypto.Subtle.Digest("SHA-256", bytes)`, from [Rask.Web](web-apis.md).

<!-- demo:browser-crypto -->

**File System Access** — `await Window.ShowOpenFilePicker(…)` *(WASM)*, then `await (await files[0].GetFile()).Text()`;
`ShowSaveFilePicker` and `CreateWritable()` save it back, from [Rask.Web](web-apis.md) (Chromium-family).

<!-- demo:browser-file-system -->

**Origin private file system** — `await Navigator.Storage.GetDirectory()`, then
`GetFileHandle(name, new() { Create = true })`, from [Rask.Web](web-apis.md); a private, persistent file tree the
app owns, with no picker. The right home for a local database file.

<!-- demo:browser-opfs -->

**`IWebAuthn`** — register and sign in with a passkey instead of a password.

<!-- demo:browser-webauthn -->

**BroadcastChannel** — `await BroadcastChannel.Create("cart")`, then `PostMessage(msg)` and `OnMessage(e => …)`, from
[Rask.Web](web-apis.md); messages between same-origin tabs (open this guide in a second tab to try it).

<!-- demo:browser-broadcast-channel -->

**`IWebLocks`** — serialise work across an origin's tabs/workers: `RequestAsync(name, work)` waits for the
named lock, runs `work` while holding it, then releases (even if `work` throws); `TryRequestAsync` returns
`false` without waiting when the lock is already held. Open this guide in a second tab and click "Hold" in
both to watch one wait for the other.

<!-- demo:browser-web-locks -->

**`IWebRtc`** — connect two browsers directly for peer-to-peer data. You supply the signaling (a WebSocket,
an HTTP endpoint, even a `BroadcastChannel` between two tabs); the wrapper handles the offer/answer exchange,
ICE, and data channels. Incoming messages and candidates arrive in **batches** — on the Server host each push
costs a WebSocket frame, so one push per message would end the session under load. The demo runs both peers
in one page, so signaling is a method call and everything else is real.

<!-- demo:browser-webrtc -->

**`ISignaling`** — the relay two peers trade an offer, an answer and their ICE candidates over, for apps
that don't already have a channel of their own. Host it with `AddRaskSignaling()` + `MapRaskSignaling()`.
Peer ids are minted by the server, a message only reaches a peer in the sender's own room, and nothing is
ever echoed back to its sender. The demo joins the same room twice from one page, so you can watch the whole
exchange.

<!-- demo:browser-signaling -->

**Notifications and the app badge** — `await Notification.RequestPermission()` *(WASM, in the click)*, then
`await using var n = await Notification.Create("Title", new() { Body = "…" })`; `await Navigator.SetAppBadge(3)` and
`ClearAppBadge()`. All from [Rask.Web](web-apis.md). A badge only shows on an installed PWA; on iOS it is numeric-only.

<!-- demo:browser-notifications -->

**`Shareable`** *(`Rask.Core` — all hosts)* — headless share: hand *your* element the `data-rask-share`
attribute and its click opens the OS share sheet, on every host including Server (the shared client fires
`navigator.share` in the click gesture, so the activation survives). For a code-driven share in a
WASM app, call `await Navigator.Share(…)` from [Rask.Web](web-apis.md#what-only-webassembly-runs) instead.

<!-- demo:browser-share -->

**`Trigger.Gesture` + six typed triggers** *(`Rask.Core` — all hosts)* — headless gesture bridge: hand *your*
element the `data-rask-gesture` attribute and its click runs an activation-gated API in the gesture, so it works
on Server too, where the imperative service can't be injected. Ships `Trigger.Fullscreen`,
`Trigger.ScreenOrientation`, `Trigger.EyeDropper`, `Trigger.Install`, `Trigger.MediaCapture`, and
`Trigger.PictureInPicture`. See [Gesture bridge](browser-apis-sharing.md#gesture-bridge--activation-gated-apis-on-the-server-host).

<!-- demo:browser-gesture-bridge -->
