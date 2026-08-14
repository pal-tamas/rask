# IDeviceMotion

> Accelerometer + gyroscope readings pushed to a callback.

- **Wraps:** Device Motion events
- **MDN:** [Device orientation events](https://developer.mozilla.org/en-US/docs/Web/API/Device_orientation_events)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription (pushes to a callback)
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** CoreMotion / SensorManager

Native backend uses linear acceleration (gravity excluded) + gyroscope; the WebView equivalent is permission-gated and often blocked.

## See also

- Source: [`IDeviceMotion.cs`](../../src/Rask.Core/Browser/IDeviceMotion.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
