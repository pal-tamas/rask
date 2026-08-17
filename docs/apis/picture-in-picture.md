# IPictureInPicture

> Float a `<video>` into a mini-player.

- **Wraps:** Picture-in-Picture API
- **MDN:** [Picture-in-Picture API](https://developer.mozilla.org/en-US/docs/Web/API/Picture-in-Picture_API)
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Needs transient activation, so the imperative `IPictureInPicture` service is WASM-only. On the **Server** host, use the declarative **`PictureInPictureTrigger`** component — point its `For:` at the `<video>`'s `ElementRef` and its click opens the mini-player inside the gesture.

> 🟡 On the Server host, reachable declaratively via `PictureInPictureTrigger` (a click-gesture component), not as an injected service.

## See also

- Source: [`IPictureInPicture.cs`](../../src/Rask.Wasm/Browser/IPictureInPicture.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
