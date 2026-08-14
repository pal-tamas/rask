# Rask.Signaling

The WebRTC **signaling relay** that Rask's `ISignaling` client connects to — the channel two peers trade an
offer, an answer and their ICE candidates over before they can reach each other directly.

It depends on nothing from `Rask.Server`, only ASP.NET routing and WebSockets, so **any** ASP.NET host can
map it — including a static-file host serving a published WASM bundle.

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

## What it guarantees

It is a relay between untrusted peers, so:

- **Peer ids are minted by the server**, never taken from the client.
- **A message reaches only a peer in the sender's own room**, checked at delivery.
- **Nothing is ever echoed back to its sender.**
- **Payload size, message rate, room size and room count are capped.**
- **Authentication is required by default** — a relay anyone can join is a way to reach other people's
  browsers, so opening it should be a decision, not an accident.
- **A refused join says the same thing whether the room was full or you weren't allowed in**, so a caller
  can't probe which rooms exist.

The payload is an **opaque string**. The relay never parses an SDP or an ICE candidate — only the two
browsers need to understand it.

## Limits

Rooms are held **in memory, per process**. Signaling is short-lived, so there is nothing worth persisting,
but a multi-instance deployment needs **sticky routing** on the signaling path — two peers assigned to
different instances never see each other.

## See also

- `docs/apis/signaling.md` — the client half (`ISignaling`, in `Rask.Core`)
- `docs/apis/webrtc.md` — what the signaling is for
