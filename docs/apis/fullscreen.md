# IFullscreen

> Present an element or the page fullscreen.

- **Wraps:** Fullscreen API
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Needs transient activation → WASM-only imperatively; a Server `FullscreenTrigger` (declarative gesture bridge) is planned.

> 🟡 On the Server host this is reachable declaratively via the planned gesture bridge, not as an injected service.

## See also

- Source: [`IFullscreen.cs`](../../src/Rask.Wasm/Browser/IFullscreen.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
