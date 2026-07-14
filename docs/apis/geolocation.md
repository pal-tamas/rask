# IGeolocation

> One-shot position (`GetCurrentPositionAsync`) or a live `WatchAsync` stream of fixes.

- **Wraps:** Geolocation API
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription (pushes to a callback)
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** CLLocationManager / LocationManager

Needs a secure context and the location permission. The native backend gives the real OS prompt and sensor accuracy; add `NSLocationWhenInUseUsageDescription` / `ACCESS_FINE_LOCATION`.

## See also

- Source: [`IGeolocation.cs`](../../src/Rask.Core/Browser/IGeolocation.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
