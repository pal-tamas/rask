# IBattery

> Charge level and charging state.

- **Wraps:** Battery Status API (`navigator.getBattery`)
- **MDN:** [Battery Status API](https://developer.mozilla.org/en-US/docs/Web/API/Battery_Status_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot (`GetStatusAsync`) + subscription (`WatchAsync` pushes to a callback)
- **Availability:** Web/Server ⚠️ (Chromium-only) · PWA/WASM ⚠️ (Chromium-only)

`GetStatusAsync()` reads the current `BatteryStatus` (level `0.0`–`1.0`, charging flag, and — where the
backend reports them — charge/discharge time in seconds) once, or `null` where the platform doesn't
expose it. `WatchAsync(onChange)` subscribes to level/charging changes and returns an `IAsyncDisposable`;
dispose it on unmount. Browser support is Chromium-family only (Firefox and Safari removed or never
shipped the API), so gate on a `null` status rather than assuming a reading.

## See also

- Source: [`IBattery.cs`](../../src/Rask.Core/Browser/IBattery.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
