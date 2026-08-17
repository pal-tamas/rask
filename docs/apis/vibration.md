# IVibration

> Buzz the device following a vibrate/pause pattern.

- **Wraps:** Vibration API
- **MDN:** [Vibration API](https://developer.mozilla.org/en-US/docs/Web/API/Vibration_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** AudioToolbox / Vibrator

`navigator.vibrate` is Android-Chromium only and absent in iOS WKWebView; the native backend works on both (iOS maps to a single system vibration).

## See also

- Source: [`IVibration.cs`](../../src/Rask.Core/Browser/IVibration.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
