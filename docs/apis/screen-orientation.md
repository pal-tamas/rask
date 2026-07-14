# IScreenOrientation

> Read/lock the screen orientation.

- **Wraps:** Screen Orientation API
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Lock needs fullscreen + activation → WASM-only; Server gesture bridge planned.

> 🟡 On the Server host this is reachable declaratively via the planned gesture bridge, not as an injected service.

## See also

- Source: [`IScreenOrientation.cs`](../../src/Rask.Wasm/Browser/IScreenOrientation.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
