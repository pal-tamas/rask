namespace Rask.Signaling;

/// <summary>
///     One connected peer: its server-minted id, its room, who it counts against (<see cref="Client" />), and the
///     queue every frame for it goes through (<see cref="Outbox" />).
/// </summary>
internal sealed record Peer(string Id, PeerOutbox Outbox, string Room, string Client);
