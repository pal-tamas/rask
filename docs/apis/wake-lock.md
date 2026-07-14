# IWakeLock

> Keep the screen awake; release by disposing the sentinel.

- **Wraps:** Screen Wake Lock API
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription (pushes to a callback)
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** IdleTimerDisabled / FLAG_KEEP_SCREEN_ON

The native backend keeps the screen on without the Wake Lock API the WebView restricts.

## See also

- Source: [`IWakeLock.cs`](../../src/Rask.Core/Browser/IWakeLock.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
