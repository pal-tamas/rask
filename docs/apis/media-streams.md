# IMediaStreams

> Attach or stop a live media stream, wherever it came from.

- **Wraps:** MediaStream (attach / stop)
- **MDN:** [MediaStream](https://developer.mozilla.org/en-US/docs/Web/API/MediaStream)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

A `MediaStream` can't cross interop, so the framework holds it in the browser under a
[`MediaStreamId`](#the-id-is-the-currency) and C# passes the id around instead. `IMediaStreams` is what you
do with one: show it in a `<video>`, or stop it.

Neither call needs a user gesture — only *acquiring* a stream does — which is why this works on every host.

## The id is the currency

Two things hand you a `MediaStreamId`, and both produce the same kind:

| Source | Host | How |
|---|---|---|
| `Trigger.MediaCapture` | Server + WASM | its `OnStream` callback, from the click gesture |
| [`IWebRtc`](webrtc.md) | every host | `RtcHandlers.OnTrack`, for a peer's remote stream |

So a camera acquired on the Server host can be sent to a WebRTC peer, and a peer's incoming video can be
attached to a `<video>`, with the same two calls in both directions.

An app can also ask for the camera in code with MDN's own call from [`Rask.Web`](../web-apis.md):
`await Navigator.MediaDevices.GetUserMedia(new() { Video = new() { Width = 640, FacingMode = "user" } })`,
then `await _video.SetSrcObject(stream)`. That hands you a kept `MediaStream`, not an id; stop it with
`await stream.GetTracks()` and `Stop()` on each track.

Attaching and stopping work on **every** host — neither needs a permission. *Acquiring* is the part that
does: it prompts for the camera/microphone permission.

```csharp
public sealed class Camera(IMediaStreams streams) : Component
{
    private readonly ElementRef _video = ElementRef.New();
    private MediaStreamId? _stream;

    protected override Component? Render() =>
        Div[
            Trigger.MediaCapture.For(_video).Template(g => Button.Type("button").Data(g)["Start camera"])
                .Video()
                .OnStream(id => { _stream = id; StateHasChanged(); return Task.CompletedTask; }),
            Button.Type("button").Disabled(_stream is null).OnClick(StopAsync)["Stop camera"],
            Video.Ref(_video).Muted()
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

Before this existed, `Trigger.MediaCapture` started the camera and attached it to a `<video>`, and that was
the end of it — the stream was unreachable from C#, so a Server-hosted app could not stop it, re-attach it,
or do anything else with it. `OnStream` plus `IMediaStreams` closes that gap.

## See also

- Source: [`IMediaStreams.cs`](../../src/Rask.Core/Browser/IMediaStreams.cs)
- [Web APIs from MDN](../web-apis.md) — `Navigator.MediaDevices.GetUserMedia`, asking for a stream in code
- [`IWebRtc`](webrtc.md) — sending one to a peer
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
