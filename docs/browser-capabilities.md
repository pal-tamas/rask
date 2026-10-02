# Browser & device API capability matrix

Every typed browser/device API wrapper Rask ships, and where it works. Inject the interface and the
framework resolves the implementation for the host. Each API links to its own reference page; the
narrative overview (with the three-homes rationale and the subscription pattern) is
[browser-apis.md](browser-apis.md). Everything else the browser ships — clipboard, geolocation, `matchMedia`,
storage, screen, share, crypto, permissions, `BroadcastChannel`, media session, Web Locks, the storage estimate,
device orientation and motion, the camera and microphone, speech synthesis, animations, fullscreen,
Picture-in-Picture, files, gamepads, notifications, push subscription, the app badge, WebUSB, WebHID, Web Serial,
Web Bluetooth — is MDN's own surface in [`Rask.Web`](web-apis.md).

**Legend** — ✅ injectable service · 🟡 reachable on Server via a declarative **gesture component** (runs the
activation-gated call inside a click), not as an injected service · ⬜ not available · — n/a.

| API | Web / Server | PWA / WASM |
|-----|:---:|:---:|
| [`ISpeechRecognition`](apis/speech-recognition.md) | ✅ | ✅ |
| [`IIndexedDb`](apis/indexeddb.md) | ✅ | ✅ |
| [`IWebAuthn`](apis/webauthn.md) | ✅ | ✅ |
| [`IMediaStreams`](apis/media-streams.md) | ✅ | ✅ |
| [`ISignaling`](apis/signaling.md) | ✅ | ✅ |
| [`IWebRtc`](apis/webrtc.md) | ✅ | ✅ |
| [`IWakeLock`](apis/wake-lock.md) | ✅ | ✅ |
| [`IInstallPrompt`](apis/install-prompt.md) | 🟡 | ✅ |
| [`IBackgroundSync`](apis/background-sync.md) | ⬜ | ✅ |

## Notes

- **Web / Server** is the ASP.NET host (per-session, over WebSocket). The transport-agnostic
  wrappers register there; the activation-gated ones (🟡) can't be injected but are reachable through
  declarative **gesture components** that run the call inside the click gesture. All six ship:
  `Trigger.Fullscreen`, `Trigger.ScreenOrientation`,
  `Trigger.EyeDropper`, [`Trigger.Install`](apis/install-prompt.md),
  [`Trigger.MediaCapture`](apis/media-streams.md), and `Trigger.PictureInPicture`
  (plus the generic `Trigger.Gesture`). The last two target a `<video>` via its `ElementRef`.
- **PWA / WASM** is the in-browser WebAssembly host, which registers the full set.
- The wake lock (`IWakeLock`) works on Server too, but its JS helper ships only under `AddRaskPwa` — see
  [pwa.md](pwa.md). Push subscription, notifications and the app badge are MDN's `PushManager`, `Notification`
  and `Navigator.SetAppBadge` in [`Rask.Web`](web-apis.md).
