# ISignaling

> The relay two peers trade an offer, an answer and their ICE candidates over.

- **Wraps:** WebSocket (Rask's signaling relay)
- **MDN:** [WebSockets API](https://developer.mozilla.org/en-US/docs/Web/API/WebSockets_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** subscription
- **Availability:** Web/Server ✅ · PWA/WASM ✅

[`IWebRtc`](webrtc.md) deliberately doesn't pick a signaling channel — an app that already has one should
use it. `ISignaling` is the channel for apps that don't, and it pairs with the **`Rask.Signaling`** package —
which depends on nothing from `Rask.Server`, so any ASP.NET host can map it, including a static-file host
serving a published WASM bundle.

The payload is an **opaque string end to end**. Serialize an `RtcDescription` or an `RtcIceCandidate` into
it however you like; neither the relay nor this wrapper looks inside. That's deliberate — parsing
attacker-controlled SDP server-side would be a lot of surface for no benefit, since only the two browsers
need to understand it.

## Server: hosting the relay

```csharp
builder.Services.AddRaskSignaling(o =>
{
    // The framework can't know who belongs in which room. This is where you say.
    o.AuthorizeRoom = ctx => ctx.Services
        .GetRequiredService<IConversations>()
        .IsMemberAsync(ctx.User, ctx.Room);
});

app.MapRaskSignaling();
```

The host must have WebSocket support in the pipeline: `Rask.Server`'s `UseRask()` calls
`app.UseWebSockets()` for you, but a static-file host serving a published WASM bundle does not — call it
yourself before mapping. The relay says so explicitly rather than refusing clients with a bare 400.

Authentication is **required by default**. A signaling relay anyone can join is a way to reach other
people's browsers, so a public default would make that an accident rather than a decision. `AuthorizeRoom`
is the per-room hook on top; its default lets any authenticated caller into any room, which is only right
when every user of the app may talk to every other.

## Client: joining a room

```csharp
_signal = await signaling.JoinAsync("room-42", new SignalingHandlers
{
    // The peers already here are the ones WE offer to. A peer arriving later offers to us instead.
    OnJoined = async (self, peers) => { foreach (var p in peers) await OfferToAsync(p); },
    OnSignal = (from, payload) => ApplyAsync(from, payload),
    OnPeerLeft = id => DropAsync(id),
});

await _signal.SendAsync(peerId, JsonSerializer.Serialize(offer));
```

That asymmetry in `OnJoined` matters: if both sides offer at once you get an SDP *glare* collision that
neither browser resolves for you. "The one who arrives offers to those already there" is the simplest rule
that avoids it.

## What the relay guarantees

- **Peer ids are minted by the server**, never taken from the client — otherwise a caller could impersonate
  another peer, or overwrite it.
- **A message reaches only a peer in the sender's own room**, checked at delivery rather than trusted from
  the message.
- **Nothing is ever echoed to its sender**, so the relay can't be aimed at itself.
- **Payload size, message rate, room size and room count are all capped** (`RaskSignalingOptions`).
- **A refused join says the same thing whether the room was full or you weren't allowed in** — anything
  else would let a caller probe which rooms exist.

## Limits worth knowing

Rooms are held **in memory, per process**. Signaling is short-lived, so there's nothing worth persisting —
but a multi-instance deployment needs sticky routing for the signaling path, or two peers assigned to
different instances never see each other.

The socket is **separate from the live render socket**, on both hosts. That one has its own frame contract,
rate limits and shutdown-drain semantics, and signaling traffic has no business sharing them. It lives in
the browser on both hosts too, so a Server-hosted app doesn't put its own server in the middle of a relay
it is already running.

## See also

- Source: [`ISignaling.cs`](../../src/Rask.Core/Browser/ISignaling.cs) ·
  [`RaskSignalingExtensions.cs`](../../src/Rask.Signaling/RaskSignalingExtensions.cs)
- [`IWebRtc`](webrtc.md) — what the signaling is for
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
