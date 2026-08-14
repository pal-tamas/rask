# IBattery

> Charge level and charging state.

- **Wraps:** Battery Status API (`navigator.getBattery`)
- **MDN:** [Battery Status API](https://developer.mozilla.org/en-US/docs/Web/API/Battery_Status_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot (`GetStatusAsync`) + subscription (`WatchAsync` pushes to a callback)
- **Availability:** Web/Server ⚠️ (Chromium-only) · PWA/WASM ⚠️ (Chromium-only) · Native ✅ ★
- **Native backend:** iOS `UIDevice` battery monitoring / Android `BatteryManager`

`GetStatusAsync()` reads the current `BatteryStatus` (level `0.0`–`1.0`, charging flag, and — where the
backend reports them — charge/discharge time in seconds) once, or `null` where the platform doesn't
expose it. `WatchAsync(onChange)` subscribes to level/charging changes and returns an `IAsyncDisposable`;
dispose it on unmount. Browser support is Chromium-family only (Firefox and Safari removed or never
shipped it), so gate on `IsSupportedAsync`. In the [native shell](../native.md) it upgrades to a real OS
backend the WebView can't provide; iOS/Android don't surface charge/discharge time, so those fields are
`null` there.

## See also

- Source: [`IBattery.cs`](../../src/Rask.Core/Browser/IBattery.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
