# Browser APIs — reference & live demos

Every typed browser wrapper with a runnable demo showing its C# source beside the live result.

‹ Back to [Browser APIs](browser-apis.md)

## API reference — live demos

Every wrapper below runs live and identically on both transports. Each demo shows its C# source beside
the running result (some are device/permission-dependent and no-op in a headless or desktop browser —
try them on a phone). The WASM-only device APIs (Serial, USB, HID, Bluetooth) and the installation/PWA
APIs live in the [Mobile & PWA guide](pwa.md).

### Storage & persistence

**`IBrowserStorage`** — typed, awaitable `localStorage` / `sessionStorage`.

<!-- demo:browser-storage -->

**`IIndexedDb`** — a persistent, asynchronous key/value store, far larger than localStorage and non-blocking. Holds
text (`SetAsync`/`GetAsync`) or raw bytes (`SetBytesAsync`/`GetBytesAsync`, stored as a real `Uint8Array`).

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


<!-- demo:browser-battery -->

**`IScreenInfo`** — display size, colour depth, and device pixel ratio.

<!-- demo:browser-screen -->

**`IVisualViewport`** — the actually-visible viewport: size, offset, and pinch-zoom scale.

<!-- demo:browser-visual-viewport -->

**`IMediaQuery`** — evaluate CSS media queries and preferences (dark mode, reduced motion) from C#.

<!-- demo:browser-media-query -->

**`IPageVisibility`** — whether the page is foreground/visible.

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
`IMediaQuery` and skip what should be skipped.

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


<!-- demo:browser-speech-recognition -->

**`IMediaSession`** — publish now-playing metadata to the OS and handle hardware media keys.

<!-- demo:browser-media-session -->

**`ICrypto`** — cryptographically strong randomness and SHA hashing (the Web Crypto API).

<!-- demo:browser-crypto -->

**`IFileSystemAccess`** — open a file, edit it, and save it back to the same file (Chromium-family).

<!-- demo:browser-file-system -->

**`IOriginPrivateFileSystem`** — a private, persistent file tree the app owns: no picker, addressed by path, written in byte ranges. The right home for a local database file.

<!-- demo:browser-opfs -->

**`IWebAuthn`** — register and sign in with a passkey instead of a password.

<!-- demo:browser-webauthn -->

**`IBroadcastChannel`** — send messages between same-origin tabs (open this guide in a second tab to try it).

<!-- demo:browser-broadcast-channel -->

**`IWebLocks`** — serialise work across an origin's tabs/workers: `RequestAsync(name, work)` waits for the
named lock, runs `work` while holding it, then releases (even if `work` throws); `TryRequestAsync` returns
`false` without waiting when the lock is already held. Open this guide in a second tab and click "Hold" in
both to watch one wait for the other.

<!-- demo:browser-web-locks -->

**`IWebRtc`** — connect two browsers directly for peer-to-peer data. You supply the signaling (a WebSocket,
an HTTP endpoint, even `IBroadcastChannel` between two tabs); the wrapper handles the offer/answer exchange,
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

**`INotifications` + `IBadge`** — raise a local notification and set the app-icon badge from the page. In the
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
`PictureInPictureTrigger`. See [Gesture bridge](browser-apis-sharing.md#gesture-bridge--activation-gated-apis-on-the-server-host).

<!-- demo:browser-gesture-bridge -->
