# Browser & device API capability matrix

Every typed browser/device API wrapper Rask ships, and where it works. Inject the interface and the
framework resolves the implementation for the host. Each API links to its own reference page; the
narrative overview (with the three-homes rationale and the subscription pattern) is
[browser-apis.md](browser-apis.md).

**Legend** — ✅ injectable service · 🟡 reachable on Server via a declarative **gesture component** (runs the
activation-gated call inside a click), not as an injected service · ⬜ not available · — n/a.

| API | Web / Server | PWA / WASM |
|-----|:---:|:---:|
| [`IBrowserStorage`](apis/storage.md) | ✅ | ✅ |
| [`ICookies`](apis/cookies.md) | ✅ | ✅ |
| [`IClipboard`](apis/clipboard.md) | ✅ | ✅ |
| [`IGeolocation`](apis/geolocation.md) | ✅ | ✅ |
| [`IPermissions`](apis/permissions.md) | ✅ | ✅ |
| [`IVibration`](apis/vibration.md) | ✅ | ✅ |
| [`IPageVisibility`](apis/page-visibility.md) | ✅ | ✅ |
| [`INavigatorInfo`](apis/navigator-info.md) | ✅ | ✅ |
| [`INetworkInfo`](apis/network-info.md) | ✅ | ✅ |
| [`IBattery`](apis/battery.md) | ✅ | ✅ |
| [`IMediaQuery`](apis/media-query.md) | ✅ | ✅ |
| [`ISpeechSynthesis`](apis/speech-synthesis.md) | ✅ | ✅ |
| [`ISpeechRecognition`](apis/speech-recognition.md) | ✅ | ✅ |
| [`IMediaSession`](apis/media-session.md) | ✅ | ✅ |
| [`IDeviceOrientation`](apis/device-orientation.md) | ✅ | ✅ |
| [`IDeviceMotion`](apis/device-motion.md) | ✅ | ✅ |
| [`IScreenInfo`](apis/screen-info.md) | ✅ | ✅ |
| [`IStorageEstimator`](apis/storage-estimator.md) | ✅ | ✅ |
| [`IVisualViewport`](apis/visual-viewport.md) | ✅ | ✅ |
| [`ICrypto`](apis/crypto.md) | ✅ | ✅ |
| [`IPerformance`](apis/performance.md) | ✅ | ✅ |
| [`IIndexedDb`](apis/indexeddb.md) | ✅ | ✅ |
| [`IFileSystemAccess`](apis/file-system-access.md) | ✅ | ✅ |
| [`IOriginPrivateFileSystem`](apis/origin-private-file-system.md) | ✅ | ✅ |
| [`IWebAuthn`](apis/webauthn.md) | ✅ | ✅ |
| [`IWebLocks`](apis/web-locks.md) | ✅ | ✅ |
| [`IMediaStreams`](apis/media-streams.md) | ✅ | ✅ |
| [`ISignaling`](apis/signaling.md) | ✅ | ✅ |
| [`IWebRtc`](apis/webrtc.md) | ✅ | ✅ |
| [`IBroadcastChannel`](apis/broadcast-channel.md) | ✅ | ✅ |
| [`IIntersectionObserver`](apis/intersection-observer.md) | ✅ | ✅ |
| [`IResizeObserver`](apis/resize-observer.md) | ✅ | ✅ |
| [`IMutationObserver`](apis/mutation-observer.md) | ✅ | ✅ |
| [`IGamepad`](apis/gamepad.md) | ✅ | ✅ |
| [`IWebPush`](apis/web-push.md) | ✅ | ✅ |
| [`INotifications`](apis/notifications.md) | ✅ | ✅ |
| [`IBadge`](apis/badge.md) | ✅ | ✅ |
| [`IWakeLock`](apis/wake-lock.md) | ✅ | ✅ |
| [`IShare`](apis/share.md) | 🟡 | ✅ |
| [`IFullscreen`](apis/fullscreen.md) | 🟡 | ✅ |
| [`IScreenOrientation`](apis/screen-orientation.md) | 🟡 | ✅ |
| [`IEyeDropper`](apis/eye-dropper.md) | 🟡 | ✅ |
| [`IPictureInPicture`](apis/picture-in-picture.md) | 🟡 | ✅ |
| [`IInstallPrompt`](apis/install-prompt.md) | 🟡 | ✅ |
| [`IMediaDevices`](apis/media-devices.md) | 🟡 | ✅ |
| [`IIdleDetector`](apis/idle-detector.md) | ⬜ | ✅ |
| [`ISerial`](apis/serial.md) | ⬜ | ✅ |
| [`IUsb`](apis/usb.md) | ⬜ | ✅ |
| [`IHid`](apis/hid.md) | ⬜ | ✅ |
| [`IBluetooth`](apis/bluetooth.md) | ⬜ | ✅ |
| [`IBackgroundSync`](apis/background-sync.md) | ⬜ | ✅ |

## Notes

- **Web / Server** is the ASP.NET host (per-session, over WebSocket). The 38 transport-agnostic
  wrappers register there; the activation-gated ones (🟡) can't be injected but are reachable through
  declarative **gesture components** that run the call inside the click gesture. All six ship:
  [`FullscreenTrigger`](apis/fullscreen.md), [`ScreenOrientationTrigger`](apis/screen-orientation.md),
  [`EyeDropperTrigger`](apis/eye-dropper.md), [`InstallTrigger`](apis/install-prompt.md),
  [`MediaCaptureTrigger`](apis/media-devices.md), and [`PictureInPictureTrigger`](apis/picture-in-picture.md)
  (plus the generic `GestureTrigger`). The last two target a `<video>` via its `ElementRef`.
- **PWA / WASM** is the in-browser WebAssembly host, which registers the full set.
- Push subscription (`IWebPush`) and the PWA APIs (`INotifications`, `IBadge`, `IWakeLock`) work on
  Server too, but their JS helpers ship only under `AddRaskPwa` — see [pwa.md](pwa.md).
