# IEyeDropper

> Pick a colour from anywhere on screen.

- **Wraps:** EyeDropper API
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Needs activation, so the imperative `IEyeDropper` service is WASM-only. On the **Server** host, use the declarative **`EyeDropperTrigger`** component — its click opens the picker inside the gesture and posts the chosen colour back to your `OnColor` callback.

> 🟡 On the Server host, reachable declaratively via `EyeDropperTrigger` (a click-gesture component), not as an injected service.

## See also

- Source: [`IEyeDropper.cs`](../../src/Rask.Wasm/Browser/IEyeDropper.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
