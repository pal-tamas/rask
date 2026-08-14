using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Rask.Signaling;

/// <summary>
///     The rooms the signaling endpoint relays through. In-memory and per-process: signaling is short-lived
///     (peers trade an offer, an answer and their candidates, then talk directly), so there is nothing worth
///     persisting, but it does mean a multi-instance deployment needs sticky routing for the signaling path
///     — two peers assigned to different instances never see each other.
/// </summary>
internal sealed class SignalingHub(RaskSignalingOptions options)
{
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);

    /// <summary>
    ///     Adds a peer to a room, minting its id. Returns the peer and the ids already present, or null when
    ///     the room (or the hub) is full. <b>The id is minted here, never taken from the client</b> — a
    ///     client-chosen id would let a caller impersonate another peer, or overwrite it.
    /// </summary>
    public Peer? Join(string room, WebSocket socket, out IReadOnlyList<string> existing)
    {
        existing = [];

        // GetOrAdd's factory can run more than once under contention, so cap rooms by re-reading the count
        // after the add rather than trusting a pre-check.
        if (!_rooms.TryGetValue(room, out var entry))
        {
            if (_rooms.Count >= options.MaxRooms)
            {
                return null;
            }

            entry = _rooms.GetOrAdd(room, _ => new Room());
        }

        var peer = new Peer(Guid.NewGuid().ToString("N"), socket, room);

        lock (entry.Gate)
        {
            if (entry.Removed || entry.Peers.Count >= options.MaxPeersPerRoom)
            {
                return null;
            }

            existing = [.. entry.Peers.Keys];
            entry.Peers[peer.Id] = peer;
        }

        return peer;
    }

    /// <summary>Removes a peer, dropping the room once it is empty.</summary>
    public void Leave(Peer peer)
    {
        if (!_rooms.TryGetValue(peer.Room, out var entry))
        {
            return;
        }

        lock (entry.Gate)
        {
            entry.Peers.Remove(peer.Id);
            if (entry.Peers.Count > 0)
            {
                return;
            }

            // Mark before removing so a Join racing us can't add into a room that is about to disappear.
            entry.Removed = true;
        }

        _rooms.TryRemove(peer.Room, out _);
    }

    /// <summary>Every other peer in the room. Never includes <paramref name="peer" /> itself.</summary>
    public IReadOnlyList<Peer> Others(Peer peer)
    {
        if (!_rooms.TryGetValue(peer.Room, out var entry))
        {
            return [];
        }

        lock (entry.Gate)
        {
            return [.. entry.Peers.Values.Where(p => p.Id != peer.Id)];
        }
    }

    /// <summary>
    ///     The named peer, but only when it shares <paramref name="from" />'s room. Membership is checked
    ///     here rather than trusted from the message: without it, a peer could address anyone in any room.
    /// </summary>
    public Peer? Target(Peer from, string peerId)
    {
        if (peerId == from.Id || !_rooms.TryGetValue(from.Room, out var entry))
        {
            return null;
        }

        lock (entry.Gate)
        {
            return entry.Peers.GetValueOrDefault(peerId);
        }
    }

    private sealed class Room
    {
        public Dictionary<string, Peer> Peers { get; } = new(StringComparer.Ordinal);

        public object Gate { get; } = new();

        public bool Removed { get; set; }
    }
}

/// <summary>One connected peer. <see cref="SendGate" /> serialises writes — a WebSocket allows only one.</summary>
internal sealed record Peer(string Id, WebSocket Socket, string Room)
{
    public SemaphoreSlim SendGate { get; } = new(1, 1);
}
