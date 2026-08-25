# IFullscreen

> Present an element or the page fullscreen.

- **Wraps:** Fullscreen API
- **MDN:** [Fullscreen API](https://developer.mozilla.org/en-US/docs/Web/API/Fullscreen_API)
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅

Needs transient activation, so the imperative `IFullscreen` service is WASM-only. On the **Server** host, use the declarative **`FullscreenTrigger`** component — its click requests fullscreen inside the gesture (the activation survives, unlike a round-tripped service call).

> 🟡 On the Server host, reachable declaratively via `FullscreenTrigger` (a click-gesture component), not as an injected service.

## See also

- Source: [`IFullscreen.cs`](../../src/Rask.Wasm/Browser/IFullscreen.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
