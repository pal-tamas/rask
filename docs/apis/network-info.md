# INetworkInfo

> Effective connection class, downlink, RTT, and Data-Saver.

- **Wraps:** Network Information API
- **MDN:** [Network Information API](https://developer.mozilla.org/en-US/docs/Web/API/Network_Information_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** NWPathMonitor / ConnectivityManager

`navigator.connection` is Chromium-only; the native backend maps the OS reachability/transport instead.

## See also

- Source: [`INetworkInfo.cs`](../../src/Rask.Core/Browser/INetworkInfo.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
