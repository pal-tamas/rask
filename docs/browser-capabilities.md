# Browser & device API capability matrix

Every typed browser/device API wrapper Rask ships, and where it works. Inject the interface and the
framework resolves the implementation for the host. Each API links to its own reference page; the
narrative overview (with the three-homes rationale and the subscription pattern) is
[browser-apis.md](browser-apis.md).

**Legend** — ✅ injectable service · 🟡 reachable on Server via a declarative **gesture component** (runs the
activation-gated call inside a click), not as an injected service · ⬜ not available · — n/a.

The third column is for the front ends that are **not** Rask components — a TypeScript SPA, or a meta
framework. There you do not inject anything: you either import the module named, which is the very code
Rask's own C# wrapper calls through, or you call the platform, because `lib.dom.d.ts` already types it
better than a wrapper would. See [TypeScript front ends → Browser APIs](spa.md#browser-apis).

| API | Web / Server | PWA / WASM | TypeScript front end |
|-----|:---:|:---:|:---|
| [`IBrowserStorage`](apis/storage.md) | ✅ | ✅ | the platform |
| [`ICookies`](apis/cookies.md) | ✅ | ✅ | `browser/cookies` |
| [`IClipboard`](apis/clipboard.md) | ✅ | ✅ | the platform |
| [`IGeolocation`](apis/geolocation.md) | ✅ | ✅ | `browser/geolocation` |
| [`IPermissions`](apis/permissions.md) | ✅ | ✅ | `browser/permissions` |
| [`IVibration`](apis/vibration.md) | ✅ | ✅ | the platform |
| [`IPageVisibility`](apis/page-visibility.md) | ✅ | ✅ | the platform |
| [`INavigatorInfo`](apis/navigator-info.md) | ✅ | ✅ | the platform |
| [`INetworkInfo`](apis/network-info.md) | ✅ | ✅ | `browser/networkInformation` |
| [`IBattery`](apis/battery.md) | ✅ | ✅ | `browser/battery` |
| [`IMediaQuery`](apis/media-query.md) | ✅ | ✅ | `browser/mediaQuery` |
| [`ISpeechSynthesis`](apis/speech-synthesis.md) | ✅ | ✅ | `browser/speechSynthesis` |
| [`ISpeechRecognition`](apis/speech-recognition.md) | ✅ | ✅ | `browser/speechRecognition` |
| [`IMediaSession`](apis/media-session.md) | ✅ | ✅ | `browser/mediaSession` |
| [`IDeviceOrientation`](apis/device-orientation.md) | ✅ | ✅ | `browser/deviceOrientation` |
| [`IDeviceMotion`](apis/device-motion.md) | ✅ | ✅ | `browser/deviceMotion` |
| [`IScreenInfo`](apis/screen-info.md) | ✅ | ✅ | `browser/screen` |
| [`IStorageEstimator`](apis/storage-estimator.md) | ✅ | ✅ | `browser/storageManager` |
| [`IVisualViewport`](apis/visual-viewport.md) | ✅ | ✅ | `browser/visualViewport` |
| [`ICrypto`](apis/crypto.md) | ✅ | ✅ | `browser/crypto` |
| [`IPerformance`](apis/performance.md) | ✅ | ✅ | `browser/performance` |
| [`IIndexedDb`](apis/indexeddb.md) | ✅ | ✅ | `browser/indexedDb` |
| [`IFileSystemAccess`](apis/file-system-access.md) | ✅ | ✅ | `browser/fileSystem` |
| [`IOriginPrivateFileSystem`](apis/origin-private-file-system.md) | ✅ | ✅ | `browser/originPrivateFileSystem` |
| [`IWebAuthn`](apis/webauthn.md) | ✅ | ✅ | `browser/webAuthn` |
| [`IWebLocks`](apis/web-locks.md) | ✅ | ✅ | `browser/webLocks` |
| [`IMediaStreams`](apis/media-streams.md) | ✅ | ✅ | `browser/mediaDevices` |
| [`ISignaling`](apis/signaling.md) | ✅ | ✅ | `browser/signaling` |
| [`IWebRtc`](apis/webrtc.md) | ✅ | ✅ | the platform |
| [`IBroadcastChannel`](apis/broadcast-channel.md) | ✅ | ✅ | `browser/broadcastChannel` |
| [`IIntersectionObserver`](apis/intersection-observer.md) | ✅ | ✅ | `browser/intersectionObserver` |
| [`IResizeObserver`](apis/resize-observer.md) | ✅ | ✅ | `browser/resizeObserver` |
| [`IMutationObserver`](apis/mutation-observer.md) | ✅ | ✅ | `browser/mutationObserver` |
| [`IGamepad`](apis/gamepad.md) | ✅ | ✅ | `browser/gamepad` |
| [`IWebPush`](apis/web-push.md) | ✅ | ✅ | `browser/webPush` |
| [`INotifications`](apis/notifications.md) | ✅ | ✅ | `browser/notifications` |
| [`IBadge`](apis/badge.md) | ✅ | ✅ | `browser/badge` |
| [`IWakeLock`](apis/wake-lock.md) | ✅ | ✅ | `browser/wakeLock` |
| [`IShare`](apis/share.md) | 🟡 | ✅ | the platform |
| [`IFullscreen`](apis/fullscreen.md) | 🟡 | ✅ | `browser/fullscreen` |
| [`IScreenOrientation`](apis/screen-orientation.md) | 🟡 | ✅ | `browser/screenOrientation` |
| [`IEyeDropper`](apis/eye-dropper.md) | 🟡 | ✅ | `browser/eyeDropper` |
| [`IPictureInPicture`](apis/picture-in-picture.md) | 🟡 | ✅ | `browser/pictureInPicture` |
| [`IInstallPrompt`](apis/install-prompt.md) | 🟡 | ✅ | `browser/installPrompt` |
| [`IMediaDevices`](apis/media-devices.md) | 🟡 | ✅ | `browser/mediaDevices` |
| [`IIdleDetector`](apis/idle-detector.md) | ⬜ | ✅ | the platform |
| [`ISerial`](apis/serial.md) | ⬜ | ✅ | the platform |
| [`IUsb`](apis/usb.md) | ⬜ | ✅ | the platform |
| [`IHid`](apis/hid.md) | ⬜ | ✅ | the platform |
| [`IBluetooth`](apis/bluetooth.md) | ⬜ | ✅ | the platform |
| [`IBackgroundSync`](apis/background-sync.md) | ⬜ | ✅ | the platform |

## Notes

- **Web / Server** is the ASP.NET host (per-session, over WebSocket). The 38 transport-agnostic
  wrappers register there; the activation-gated ones (🟡) can't be injected but are reachable through
  declarative **gesture components** that run the call inside the click gesture. All six ship:
  [`FullscreenTrigger`](apis/fullscreen.md), [`ScreenOrientationTrigger`](apis/screen-orientation.md),
  [`EyeDropperTrigger`](apis/eye-dropper.md), [`InstallTrigger`](apis/install-prompt.md),
  [`MediaCaptureTrigger`](apis/media-devices.md), and [`PictureInPictureTrigger`](apis/picture-in-picture.md)
  (plus the generic `GestureTrigger`). The last two target a `<video>` via its `ElementRef`.
- **PWA / WASM** is the in-browser WebAssembly host, which registers the full set.
- **TypeScript front end** is an SPA or a meta framework. On the SPA lane the modules arrive in
  `src/rask/browser/`; on the meta lane they arrive in whichever source directory that framework
  uses and are imported as `@rask/browser/…` through a tsconfig path — see
  [meta.md](meta.md#browser-apis). Read the 🟡 and ⬜ rows there carefully: those
  restrictions are properties of the SERVER TRANSPORT, not of the API. Fullscreen, the eye dropper,
  screen orientation, picture-in-picture, the install prompt, media capture and the device APIs are
  limited on Server because they need *transient user activation*, which a WebSocket round trip loses.
  A TypeScript front end calls them inside the click's own stack, so **every row above is available to
  it** — the column only says whether Rask ships a module or you call the platform.
- "The platform" is not a lesser answer. `navigator.clipboard.writeText`, `localStorage`,
  `element.animate()`, `RTCPeerConnection` and `navigator.serial` are native, already typed, and a
  wrapper would only stand between you and them. Rask ships a module where the platform leaves real
  work to you — a callback API that should be a promise, a live object that has to be snapshotted, a
  base64url ceremony — or where the other half is Rask's own server (`signaling`, `webPush`).
- Push subscription (`IWebPush`) and the PWA APIs (`INotifications`, `IBadge`, `IWakeLock`) work on
  Server too, but their JS helpers ship only under `AddRaskPwa` — see [pwa.md](pwa.md).
