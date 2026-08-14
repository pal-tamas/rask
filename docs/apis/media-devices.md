# IMediaDevices

> Capture camera/mic/screen into a `<video>`.

- **Wraps:** Media Capture (getUserMedia)
- **Home:** `Rask.Wasm.Browser` (WASM only)
- **Shape:** one-shot
- **Availability:** Web/Server 🟡 · PWA/WASM ✅ · Native ⬜
- **Native backend:** — (WebView JS)

Capture needs transient activation + a secure (HTTPS) context, so the imperative `IMediaDevices` service is WASM-only. On the **Server** host, use the declarative **`MediaCaptureTrigger`** component — point its `For:` at a `<video>`'s `ElementRef`, set `Audio` / `Video` / `FacingMode`, and its click starts the stream and attaches it to that element inside the gesture, posting `"granted"` / `"denied"` to `OnResult`.

The trigger's **`OnStream`** callback hands back the started stream's [`MediaStreamId`](media-streams.md), which is what keeps the stream reachable from C# afterwards — stop it with [`IMediaStreams`](media-streams.md), re-attach it, or send it to a peer with [`IWebRtc`](webrtc.md). On WASM, `IMediaStreamHandle.Id` is the same id.

> 🟡 On the Server host, reachable declaratively via `MediaCaptureTrigger` (a click-gesture component), not as an injected service.

## See also

- Source: [`IMediaDevices.cs`](../../src/Rask.Wasm/Browser/IMediaDevices.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
