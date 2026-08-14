# IScreenOrientation

> Read/lock the screen orientation.

- **Wraps:** Screen Orientation API
- **MDN:** [Screen Orientation API](https://developer.mozilla.org/en-US/docs/Web/API/Screen_Orientation_API)
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Lock needs fullscreen + transient activation, so the imperative `IScreenOrientation` service is WASM-only. On the **Server** host, use the declarative **`ScreenOrientationTrigger`** component (`ScreenOrientationTrigger(Orientation: "landscape", …)`) — its click locks the orientation inside the gesture. The browser's `screen.orientation.lock` only resolves while the page is fullscreen, so pair it with a `FullscreenTrigger` (or app-controlled fullscreen); off-fullscreen or on desktop the lock is a silent no-op.

> 🟡 On the Server host, reachable declaratively via `ScreenOrientationTrigger` (a click-gesture component), not as an injected service.

## See also

- Source: [`IScreenOrientation.cs`](../../src/Rask.Wasm/Browser/IScreenOrientation.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
