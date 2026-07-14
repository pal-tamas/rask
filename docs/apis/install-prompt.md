# IInstallPrompt

> Capture and replay the PWA install prompt.

- **Wraps:** `beforeinstallprompt`
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Needs transient activation + a boot-captured event, so the imperative `IInstallPrompt` service is WASM-only. On the **Server** host, use the declarative **`InstallTrigger`** component — its click shows the browser's install prompt inside the gesture and posts the outcome (`"accepted"` / `"dismissed"` / `"unavailable"`) to `OnOutcome`. The app must be installable (a web manifest + service worker over HTTPS — on Server that means `AddRaskPwa`), otherwise the outcome is `"unavailable"`.

> 🟡 On the Server host, reachable declaratively via `InstallTrigger` (a click-gesture component), not as an injected service.

## See also

- Source: [`IInstallPrompt.cs`](../../src/Rask.Wasm/Browser/IInstallPrompt.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
