using System.Net.WebSockets;

namespace Rask.Signaling;

/// <summary>One connected peer. <see cref="SendGate" /> serialises writes — a WebSocket allows only one.</summary>
internal sealed record Peer(string Id, WebSocket Socket, string Room)
{
    public SemaphoreSlim SendGate { get; } = new(1, 1);
}
