# IWakeLock

> Keep the screen awake; release by disposing the sentinel.

- **Wraps:** Screen Wake Lock API
- **MDN:** [Screen Wake Lock API](https://developer.mozilla.org/en-US/docs/Web/API/Screen_Wake_Lock_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription (pushes to a callback)
- **Availability:** Web/Server ✅ · PWA/WASM ✅

The lock is released automatically when the page is hidden, so re-acquire it on `visibilitychange`.

## See also

- Source: [`IWakeLock.cs`](../../src/Rask.Core/Browser/IWakeLock.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
