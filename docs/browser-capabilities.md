# Browser & device API capability matrix

Every typed browser/device API wrapper Rask ships, and where it works. Inject the interface; the
framework resolves the best implementation for the host — a native iOS/Android backend where one
exists, the WebView's JS otherwise. Each API links to its own reference page; the narrative overview
(with the three-homes rationale and the subscription pattern) is [browser-apis.md](browser-apis.md).

**Legend** — ✅ implemented · 🟡 planned via the Server gesture bridge · ⬜ not available · — n/a. A
**★** in the Native column marks an API with a native C# backend (the rest run through the WebView's JS).

| API | Web / Server | PWA / WASM | Native | Native backend |
|-----|:---:|:---:|:---:|-----|
| [`IBrowserStorage`](apis/storage.md) | ✅ | ✅ | ✅ | — |
| [`ICookies`](apis/cookies.md) | ✅ | ✅ | ✅ | — |
| [`IClipboard`](apis/clipboard.md) | ✅ | ✅ | ✅&nbsp;★ | UIPasteboard / ClipboardManager |
| [`IGeolocation`](apis/geolocation.md) | ✅ | ✅ | ✅&nbsp;★ | CLLocationManager / LocationManager |
| [`IPermissions`](apis/permissions.md) | ✅ | ✅ | ✅ | — |
| [`IVibration`](apis/vibration.md) | ✅ | ✅ | ✅&nbsp;★ | AudioToolbox / Vibrator |
| [`IPageVisibility`](apis/page-visibility.md) | ✅ | ✅ | ✅ | — |
| [`INavigatorInfo`](apis/navigator-info.md) | ✅ | ✅ | ✅ | — |
| [`INetworkInfo`](apis/network-info.md) | ✅ | ✅ | ✅&nbsp;★ | NWPathMonitor / ConnectivityManager |
| [`IMediaQuery`](apis/media-query.md) | ✅ | ✅ | ✅ | — |
| [`ISpeechSynthesis`](apis/speech-synthesis.md) | ✅ | ✅ | ✅&nbsp;★ | AVSpeechSynthesizer / TextToSpeech |
| [`IMediaSession`](apis/media-session.md) | ✅ | ✅ | ✅ | — |
| [`IDeviceOrientation`](apis/device-orientation.md) | ✅ | ✅ | ✅&nbsp;★ | CoreMotion / SensorManager |
| [`IDeviceMotion`](apis/device-motion.md) | ✅ | ✅ | ✅&nbsp;★ | CoreMotion / SensorManager |
| [`IScreenInfo`](apis/screen-info.md) | ✅ | ✅ | ✅&nbsp;★ | UIScreen / DisplayMetrics |
| [`IStorageEstimator`](apis/storage-estimator.md) | ✅ | ✅ | ✅ | — |
| [`IVisualViewport`](apis/visual-viewport.md) | ✅ | ✅ | ✅ | — |
| [`ICrypto`](apis/crypto.md) | ✅ | ✅ | ✅ | — |
| [`IPerformance`](apis/performance.md) | ✅ | ✅ | ✅ | — |
| [`IIndexedDb`](apis/indexeddb.md) | ✅ | ✅ | ✅ | — |
| [`IFileSystemAccess`](apis/file-system-access.md) | ✅ | ✅ | ✅ | — |
| [`IWebAuthn`](apis/webauthn.md) | ✅ | ✅ | ✅ | — |
| [`IBroadcastChannel`](apis/broadcast-channel.md) | ✅ | ✅ | ✅ | — |
| [`IIntersectionObserver`](apis/intersection-observer.md) | ✅ | ✅ | ✅ | — |
| [`IResizeObserver`](apis/resize-observer.md) | ✅ | ✅ | ✅ | — |
| [`IMutationObserver`](apis/mutation-observer.md) | ✅ | ✅ | ✅ | — |
| [`IGamepad`](apis/gamepad.md) | ✅ | ✅ | ✅ | — |
| [`IWebPush`](apis/web-push.md) | ✅ | ✅ | ✅ | — |
| [`INotifications`](apis/notifications.md) | ✅ | ✅ | ✅ | — |
| [`IBadge`](apis/badge.md) | ✅ | ✅ | ✅ | — |
| [`IWakeLock`](apis/wake-lock.md) | ✅ | ✅ | ✅&nbsp;★ | IdleTimerDisabled / FLAG_KEEP_SCREEN_ON |
| [`IShare`](apis/share.md) | 🟡 | ✅ | ✅&nbsp;★ | UIActivityViewController / ACTION_SEND |
| [`IFullscreen`](apis/fullscreen.md) | 🟡 | ✅ | ⬜ | — |
| [`IScreenOrientation`](apis/screen-orientation.md) | 🟡 | ✅ | ⬜ | — |
| [`IEyeDropper`](apis/eye-dropper.md) | 🟡 | ✅ | ⬜ | — |
| [`IPictureInPicture`](apis/picture-in-picture.md) | 🟡 | ✅ | ⬜ | — |
| [`IInstallPrompt`](apis/install-prompt.md) | 🟡 | ✅ | ⬜ | — |
| [`IMediaDevices`](apis/media-devices.md) | 🟡 | ✅ | ⬜ | — |
| [`IIdleDetector`](apis/idle-detector.md) | ⬜ | ✅ | ⬜ | — |
| [`ISerial`](apis/serial.md) | ⬜ | ✅ | ⬜ | — |
| [`IUsb`](apis/usb.md) | ⬜ | ✅ | ⬜ | — |
| [`IHid`](apis/hid.md) | ⬜ | ✅ | ⬜ | — |
| [`IBluetooth`](apis/bluetooth.md) | ⬜ | ✅ | ⬜ | — |

## Notes

- **Web / Server** is the ASP.NET host (per-session, over WebSocket). The 31 transport-agnostic
  wrappers register there; the activation-gated ones (🟡) can't be injected but are reachable through
  declarative gesture components (planned — see the roadmap in [browser-apis.md](browser-apis.md)).
- **PWA / WASM** is the in-browser WebAssembly host, which registers the full set.
- **Native** is the `Rask.Native` host. Every ★ API has a first-class native backend wired by
  `ApplePlatform` / `AndroidPlatform` (see [native.md](native.md)); the rest run through the WebView.
- Push subscription (`IWebPush`) and the PWA APIs (`INotifications`, `IBadge`, `IWakeLock`) work on
  Server too, but their JS helpers ship only under `AddRaskPwa` — see [pwa.md](pwa.md).
