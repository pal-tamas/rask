# IMediaStreams

> Attach or stop a live media stream, wherever it came from.

- **Wraps:** MediaStream (attach / stop)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** — (WebView JS)

A `MediaStream` can't cross interop, so the framework holds it in the browser under a
[`MediaStreamId`](#the-id-is-the-currency) and C# passes the id around instead. `IMediaStreams` is what you
do with one: show it in a `<video>`, or stop it.

Neither call needs a user gesture — only *acquiring* a stream does — which is why this works on every host
while [`IMediaDevices`](media-devices.md) is WASM-only.

## The id is the currency

Three things hand you a `MediaStreamId`, and all three produce the same kind:

| Source | Host | How |
|---|---|---|
| [`MediaCaptureTrigger`](media-devices.md) | every host | its `OnStream` callback, from the click gesture |
| [`IMediaDevices`](media-devices.md) | WASM | `IMediaStreamHandle.Id` |
| [`IWebRtc`](webrtc.md) | every host | `RtcHandlers.OnTrack`, for a peer's remote stream |

So a camera acquired on the Server host can be sent to a WebRTC peer, and a peer's incoming video can be
attached to a `<video>`, with the same two calls in both directions.

```csharp
public sealed class Camera(IMediaStreams streams) : Component
{
    private readonly ElementRef _video = ElementRef.New();
    private MediaStreamId? _stream;

    protected override Component? Render() =>
        Div()[
            MediaCaptureTrigger(For: _video, Video: true,
                OnStream: id => { _stream = id; StateHasChanged(); return Task.CompletedTask; },
                Template: g => Button(Type: "button", Data: g)["Start camera"]),
            Button(Type: "button", Disabled: _stream is null, OnClickAsync: StopAsync)["Stop camera"],
            Video(Ref: _video, Muted: true)
        ];

    private async Task StopAsync()
    {
        if (_stream is { } id) { await streams.StopAsync(id); _stream = null; }
    }
}
```

## Stopping is not optional

A live stream holds the camera and microphone open — hardware indicator and all — until every one of its
tracks is stopped. Nothing stops it for you when a component unmounts or the user navigates away within the
app. Stop it yourself.

The one exception: a **remote** stream from `RtcHandlers.OnTrack` is owned by its peer connection, and
disposing that connection stops it. A stream you captured and sent with `AddStreamAsync` stays yours, and
disposing the connection deliberately leaves it running.

## On the Server host this is new

Before this existed, `MediaCaptureTrigger` started the camera and attached it to a `<video>`, and that was
the end of it — the stream was unreachable from C#, so a Server-hosted app could not stop it, re-attach it,
or do anything else with it. `OnStream` plus `IMediaStreams` closes that gap.

## See also

- Source: [`IMediaStreams.cs`](../../src/Rask.Core/Browser/IMediaStreams.cs)
- [`IMediaDevices`](media-devices.md) — acquiring a stream
- [`IWebRtc`](webrtc.md) — sending one to a peer
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
