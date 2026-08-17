# IWebRtc

> Connect two browsers directly for peer-to-peer data.

- **Wraps:** WebRTC (RTCPeerConnection, RTCDataChannel)
- **MDN:** [WebRTC API](https://developer.mozilla.org/en-US/docs/Web/API/WebRTC_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** — (WebView JS)

WebRTC opens a direct connection between two browsers, so data travels peer-to-peer instead of through your
server. The live `RTCPeerConnection` stays in the browser under the framework's `__raskRtc` helper; C# holds
an `IPeerConnection` addressed by id, and the browser pushes candidates, state changes and messages back
through a static `[JSInvokable]` — one wiring, both transports.

## You supply the signaling

WebRTC cannot start a connection on its own. Before two peers can talk, they have to trade an **offer**, an
**answer**, and their **ICE candidates** through some channel they already share. Rask does not pick that
channel for you — `RtcDescription` and `RtcIceCandidate` are plain serializable records, so they ride
whatever you already have: a WebSocket, an HTTP endpoint, or `IBroadcastChannel` between two tabs of the
same origin.

```csharp
public sealed class Call(IWebRtc rtc) : Component, IAsyncDisposable
{
    private IPeerConnection? _conn;
    private IRtcDataChannel? _chat;

    protected override async Task OnRenderedAsync(bool first)
    {
        if (!first) return;

        _conn = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnIceCandidates = async cands => { foreach (var c in cands) await Signal(c); },
            OnConnectionStateChanged = s => { _state = s; StateHasChanged(); return Task.CompletedTask; },
            OnDataChannel = ch => ch.ListenAsync(OnMessagesAsync).AsTask(),
        });

        _chat = await _conn.CreateDataChannelAsync("chat");
        await _chat.ListenAsync(OnMessagesAsync);

        var offer = await _conn.CreateOfferAsync();
        await _conn.SetLocalDescriptionAsync(offer);
        await Signal(offer);
    }

    private Task OnMessagesAsync(IReadOnlyList<RtcMessage> batch) { /* … */ }

    public async ValueTask DisposeAsync()
    {
        if (_conn is not null) await _conn.DisposeAsync();   // closes its channels too
    }
}
```

## Messages arrive in batches

`ListenAsync` hands you an `IReadOnlyList<RtcMessage>`, not one message at a time, and `OnIceCandidates`
does the same. That is not a convenience — it is what keeps the Server host alive. Each push from the
browser costs one inbound WebSocket frame, and the host closes a socket that exceeds its inbound frame rate
(`RaskServerLimits.MaxInboundFramesPerSecond`, 1000 by default). A busy data channel delivered one message
per push would end the session in under a second; an ICE gathering burst would spike it. The framework
buffers on a short timer instead, which bounds the push rate no matter how fast the peer sends. WASM gets
the same shape, so the two hosts stay identical.

Under sustained overload the client-side buffer is capped. Past the cap the oldest messages are dropped and
counted, and the count is reported through `RaskDiagnostics` — an unbounded buffer would only trade a closed
socket for an out-of-memory tab. ICE candidates are never dropped.

## Call ListenAsync, or you receive nothing

A channel buffers from the moment it exists, and starts delivering only when you call `ListenAsync`. This is
what makes a remote-opened channel safe: by the time `OnDataChannel` runs, the peer may already have sent
something, and those messages ride the first batch rather than being lost.

## Buffer candidates until the remote description is applied

`AddIceCandidateAsync` throws if the connection has no remote description yet, and gathering routinely
outruns the answer. Hold incoming candidates in a list until you have applied the offer or answer, then
drain it. This is the single most common way a first WebRTC integration fails, and it is not something the
wrapper can hide — only your signaling knows when the exchange completed.

## Sending camera, microphone or screen

Acquire a stream, then hand its [`MediaStreamId`](media-streams.md) to the connection:

```csharp
// Server host: the click gesture acquires it, OnStream hands back the id.
MediaCaptureTrigger.For(_preview).Video(true).Audio(true)
    .OnStream(async id => await _conn!.AddStreamAsync(id))
    .Template(g => Button.Type("button").Data(g)["Start camera"])

// The peer's media comes back the same way, and attaches like any other stream.
new RtcHandlers { OnTrack = id => streams.AttachToAsync(id, _remoteVideo) }
```

Screen sharing needs nothing extra — `getDisplayMedia` yields the same kind of id as the camera.

`OnTrack` fires **once per stream, not per track**: a peer sending camera and microphone sends two tracks in
one stream, and the stream is what you attach.

Ownership splits in the way you'd want. A stream **you** added stays yours — disposing the connection stops
sending it but leaves it running, so stop it yourself with `IMediaStreams.StopAsync`. A **remote** stream
from `OnTrack` belongs to the connection, and disposing the connection stops it.

Adding or removing a stream **renegotiates**: exchange a fresh offer/answer afterwards, or the peer never
sees the change.

## ICE servers, and what they leak

`RtcConfiguration.IceServers` is empty by default, which means host candidates only — enough for two peers
on the same machine or LAN, not across the internet. Rask ships no STUN or TURN server; supply your own.
URLs must use `stun:`, `turn:` or `turns:`; anything else is rejected.

Be aware that ICE tells the other peer your local network addresses. That is inherent to WebRTC, not to this
wrapper. Where it matters, set `IceTransportPolicy = "relay"` and provide a TURN server, so all traffic goes
through the relay and the peer learns nothing about your network.

## See also

- Source: [`IWebRtc.cs`](../../src/Rask.Core/Browser/IWebRtc.cs)
- [`IBroadcastChannel`](broadcast-channel.md) — a signaling channel between two tabs of the same origin
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
