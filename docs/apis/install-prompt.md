# IInstallPrompt

> Capture and replay the PWA install prompt.

- **Wraps:** `beforeinstallprompt`
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Needs activation + live document → WASM-only; Server gesture bridge planned (PWA-gated).

> 🟡 On the Server host this is reachable declaratively via the planned gesture bridge, not as an injected service.

## See also

- Source: [`IInstallPrompt.cs`](../../src/Rask.Wasm/Browser/IInstallPrompt.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
