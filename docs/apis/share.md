# IShare

> Share text/URL from code to the OS share sheet.

- **Wraps:** Web Share API
- **Home:** `Rask.Client.Browser` (WASM + Native)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ✅
- **Native backend:** UIActivityViewController / ACTION_SEND

Imperative sharing needs transient activation, so it's WASM+Native only. On Server, use the headless `Shareable` component, whose click fires the share inside the gesture.

> 🟡 On the Server host this is reachable declaratively via the planned gesture bridge, not as an injected service.

## See also

- Source: [`IShare.cs`](../../src/Rask.Client/Browser/IShare.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
