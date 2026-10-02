# Browser & device API capability matrix

Every typed browser/device API wrapper Rask ships, and where it works. Inject the interface and the
framework resolves the implementation for the host. Each API links to its own reference page; the
narrative overview (with the three-homes rationale and the subscription pattern) is
[browser-apis.md](browser-apis.md). Everything else the browser ships — clipboard, geolocation, `matchMedia`,
storage, screen, share — is MDN's own surface in [`Rask.Web`](web-apis.md).

**Legend** — ✅ injectable service · 🟡 reachable on Server via a declarative **gesture component** (runs the
activation-gated call inside a click), not as an injected service · ⬜ not available · — n/a.

| API | Web / Server | PWA / WASM |
|-----|:---:|:---:|
| [`ICookies`](apis/cookies.md) | ✅ | ✅ |
| [`IPermissions`](apis/permissions.md) | ✅ | ✅ |
| [`ISpeechSynthesis`](apis/speech-synthesis.md) | ✅ | ✅ |
| [`ISpeechRecognition`](apis/speech-recognition.md) | ✅ | ✅ |
| [`IMediaSession`](apis/media-session.md) | ✅ | ✅ |
| [`IDeviceOrientation`](apis/device-orientation.md) | ✅ | ✅ |
| [`IDeviceMotion`](apis/device-motion.md) | ✅ | ✅ |
| [`IStorageEstimator`](apis/storage-estimator.md) | ✅ | ✅ |
| [`ICrypto`](apis/crypto.md) | ✅ | ✅ |
| [`IIndexedDb`](apis/indexeddb.md) | ✅ | ✅ |
| [`IFileSystemAccess`](apis/file-system-access.md) | ✅ | ✅ |
| [`IOriginPrivateFileSystem`](apis/origin-private-file-system.md) | ✅ | ✅ |
| [`IWebAuthn`](apis/webauthn.md) | ✅ | ✅ |
| [`IWebLocks`](apis/web-locks.md) | ✅ | ✅ |
| [`IMediaStreams`](apis/media-streams.md) | ✅ | ✅ |
| [`ISignaling`](apis/signaling.md) | ✅ | ✅ |
| [`IWebRtc`](apis/webrtc.md) | ✅ | ✅ |
| [`IBroadcastChannel`](apis/broadcast-channel.md) | ✅ | ✅ |
| [`IGamepad`](apis/gamepad.md) | ✅ | ✅ |
| [`IWebPush`](apis/web-push.md) | ✅ | ✅ |
| [`INotifications`](apis/notifications.md) | ✅ | ✅ |
| [`IBadge`](apis/badge.md) | ✅ | ✅ |
| [`IWakeLock`](apis/wake-lock.md) | ✅ | ✅ |
| [`IFullscreen`](apis/fullscreen.md) | 🟡 | ✅ |
| [`IPictureInPicture`](apis/picture-in-picture.md) | 🟡 | ✅ |
| [`IInstallPrompt`](apis/install-prompt.md) | 🟡 | ✅ |
| [`IMediaDevices`](apis/media-devices.md) | 🟡 | ✅ |
| [`ISerial`](apis/serial.md) | ⬜ | ✅ |
| [`IUsb`](apis/usb.md) | ⬜ | ✅ |
| [`IHid`](apis/hid.md) | ⬜ | ✅ |
| [`IBluetooth`](apis/bluetooth.md) | ⬜ | ✅ |
| [`IBackgroundSync`](apis/background-sync.md) | ⬜ | ✅ |

## Notes

- **Web / Server** is the ASP.NET host (per-session, over WebSocket). The transport-agnostic
  wrappers register there; the activation-gated ones (🟡) can't be injected but are reachable through
  declarative **gesture components** that run the call inside the click gesture. All six ship:
  [`Trigger.Fullscreen`](apis/fullscreen.md), `Trigger.ScreenOrientation`,
  `Trigger.EyeDropper`, [`Trigger.Install`](apis/install-prompt.md),
  [`Trigger.MediaCapture`](apis/media-devices.md), and [`Trigger.PictureInPicture`](apis/picture-in-picture.md)
  (plus the generic `Trigger.Gesture`). The last two target a `<video>` via its `ElementRef`.
- **PWA / WASM** is the in-browser WebAssembly host, which registers the full set.
- Push subscription (`IWebPush`) and the PWA APIs (`INotifications`, `IBadge`, `IWakeLock`) work on
  Server too, but their JS helpers ship only under `AddRaskPwa` — see [pwa.md](pwa.md).
