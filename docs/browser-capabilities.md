# Browser & device API capability matrix

Every typed browser/device API wrapper Rask ships, and where it works. Inject the interface and the
framework resolves the implementation for the host. Each API links to its own reference page; the
narrative overview (with the three-homes rationale and the subscription pattern) is
[browser-apis.md](browser-apis.md). Everything else the browser ships — clipboard, geolocation, `matchMedia`,
storage, screen, share, crypto, permissions, `BroadcastChannel`, media session, Web Locks, the storage estimate,
device orientation and motion, the camera and microphone, speech synthesis and recognition, the screen wake lock, the
install prompt, animations, fullscreen,
Picture-in-Picture, files, gamepads, notifications, push subscription, the app badge, WebUSB, WebHID, Web Serial,
Web Bluetooth — is MDN's own surface in [`Rask.Web`](web-apis.md).

**Legend** — ✅ injectable service · ⬜ not available · — n/a.

| API | Web / Server | PWA / WASM |
|-----|:---:|:---:|
| [`IIndexedDb`](apis/indexeddb.md) | ✅ | ✅ |
| [`IWebAuthn`](apis/webauthn.md) | ✅ | ✅ |
| [`ISignaling`](apis/signaling.md) | ✅ | ✅ |
| [`IWebRtc`](apis/webrtc.md) | ✅ | ✅ |
| [`IBackgroundSync`](apis/background-sync.md) | ⬜ | ✅ |

## Notes

- **Web / Server** is the ASP.NET host (per-session, over WebSocket). The transport-agnostic
  wrappers register there; activation-gated browser calls can't run from a handler there, but are reachable through
  declarative **gesture components** that run the call inside the click gesture. All six ship:
  `Trigger.Fullscreen`, `Trigger.ScreenOrientation`,
  `Trigger.EyeDropper`, `Trigger.Install`, `Trigger.MediaCapture`, and `Trigger.PictureInPicture`
  (plus the generic `Trigger.Gesture`). The last two target a `<video>` via its `ElementRef`.
  `Trigger.MediaCapture`'s `OnStream` hands the stream over as an `IJSObjectReference`; wrap it with
  `MediaStream.From(stream)` from [`Rask.Web`](web-apis.md#on-an-element-ref) to stop it or show it.
- **PWA / WASM** is the in-browser WebAssembly host, which registers the full set.
- The wake lock is `await Navigator.WakeLock.Request(WakeLockType.Screen)` and the custom install button is
  `Window.OnBeforeInstallPrompt(…)` in [`Rask.Web`](web-apis.md#where-a-browser-falls-short). The wake lock works on
  both hosts; the install prompt's `Prompt()` needs a click, so it is WASM-only, and `Trigger.Install` covers the
  Server host. Push subscription, notifications and the app badge are MDN's `PushManager`, `Notification`
  and `Navigator.SetAppBadge` in [`Rask.Web`](web-apis.md).
