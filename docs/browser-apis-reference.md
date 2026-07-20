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

**`IBattery`** — charge level and charging state: `GetStatusAsync()` reads once, `WatchAsync(onChange)` subscribes to level/charging changes. Chromium-only in the browser (`GetStatusAsync` returns `null` elsewhere); in the [native shell](native.md) it resolves to a real OS backend (iOS `UIDevice` / Android `BatteryManager`).

<!-- demo:browser-battery -->

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

**`ISpeechRecognition`** — dictation: `StartAsync(onResult, options)` streams each recognised phrase (final and, with `InterimResults`, interim) to the callback; dispose the handle to stop. Prompts for the microphone; Chromium-only in the browser, with a native `SFSpeechRecognizer`/`SpeechRecognizer` backend in the [native shell](native.md). The counterpart to `ISpeechSynthesis`.

<!-- demo:browser-speech-recognition -->

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
`PictureInPictureTrigger`. See [Gesture bridge](browser-apis-sharing.md#gesture-bridge--activation-gated-apis-on-the-server-host).

<!-- demo:browser-gesture-bridge -->
