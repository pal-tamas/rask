using System.Net.WebSockets;

namespace Rask.Signaling.Tests;

// The hub is the membership authority: which peers exist, which room each is in, and who may be addressed.
// Every security property of the relay reduces to one of these.
public class SignalingHubTests
{
    private static SignalingHub Hub(int maxPeers = 8, int maxRooms = 1000) =>
        new(new RaskSignalingOptions { MaxPeersPerRoom = maxPeers, MaxRooms = maxRooms });

    private static WebSocket Socket() => new FakeSocket();

    [Fact]
    public void Join_MintsThePeerId_AndNeverTakesItFromTheCaller()
    {
        // A client-chosen id would let a caller impersonate another peer, or overwrite it. There is
        // deliberately no way to supply one.
        var hub = Hub();

        var first = hub.Join("room", Socket(), out _)!;
        var second = hub.Join("room", Socket(), out _)!;

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEmpty(first.Id);
    }

    [Fact]
    public void Join_ReportsThePeersAlreadyPresent()
    {
        var hub = Hub();
        var first = hub.Join("room", Socket(), out var noneYet)!;

        hub.Join("room", Socket(), out var existing);

        Assert.Empty(noneYet);
        Assert.Equal([first.Id], existing);
    }

    [Fact]
    public void Join_RefusesOnceTheRoomIsFull()
    {
        var hub = Hub(maxPeers: 2);
        hub.Join("room", Socket(), out _);
        hub.Join("room", Socket(), out _);

        Assert.Null(hub.Join("room", Socket(), out _));
    }

    [Fact]
    public void Join_RefusesOnceThereAreTooManyRooms()
    {
        var hub = Hub(maxRooms: 1);
        hub.Join("first", Socket(), out _);

        Assert.Null(hub.Join("second", Socket(), out _));
    }

    [Fact]
    public void Others_NeverIncludesTheCallerItself()
    {
        // This is what stops the relay echoing a peer's own message back to it.
        var hub = Hub();
        var peer = hub.Join("room", Socket(), out _)!;
        var other = hub.Join("room", Socket(), out _)!;

        var others = hub.Others(peer);

        Assert.Equal([other.Id], others.Select(p => p.Id));
    }

    [Fact]
    public void Target_RefusesAPeerInAnotherRoom()
    {
        // The message names a peer; without this check, naming one is enough to reach it anywhere.
        var hub = Hub();
        var here = hub.Join("room-a", Socket(), out _)!;
        var elsewhere = hub.Join("room-b", Socket(), out _)!;

        Assert.Null(hub.Target(here, elsewhere.Id));
    }

    [Fact]
    public void Target_RefusesTheCallerItself()
    {
        var hub = Hub();
        var peer = hub.Join("room", Socket(), out _)!;

        Assert.Null(hub.Target(peer, peer.Id));
    }

    [Fact]
    public void Target_RefusesAnUnknownPeer()
    {
        var hub = Hub();
        var peer = hub.Join("room", Socket(), out _)!;

        Assert.Null(hub.Target(peer, "not-a-peer"));
    }

    [Fact]
    public void Target_FindsAPeerInTheSameRoom()
    {
        var hub = Hub();
        var from = hub.Join("room", Socket(), out _)!;
        var to = hub.Join("room", Socket(), out _)!;

        Assert.Equal(to.Id, hub.Target(from, to.Id)?.Id);
    }

    [Fact]
    public void Leave_RemovesThePeer_AndTheRoomGoesWithTheLastOne()
    {
        var hub = Hub(maxRooms: 1);
        var first = hub.Join("room", Socket(), out _)!;
        var second = hub.Join("room", Socket(), out _)!;

        hub.Leave(first);
        Assert.Empty(hub.Others(second));

        hub.Leave(second);

        // The room is gone, so a different one now fits under the cap of 1 — which is how we observe that
        // an emptied room doesn't leak.
        Assert.NotNull(hub.Join("another", Socket(), out _));
    }

    [Fact]
    public void Leave_IsSafeTwice()
    {
        var hub = Hub();
        var peer = hub.Join("room", Socket(), out _)!;

        hub.Leave(peer);
        hub.Leave(peer);
    }

    [Fact]
    public void Rooms_AreCaseSensitiveAndDoNotBleedIntoEachOther()
    {
        var hub = Hub();
        var lower = hub.Join("room", Socket(), out _)!;
        var upper = hub.Join("ROOM", Socket(), out _)!;

        Assert.Empty(hub.Others(lower));
        Assert.Null(hub.Target(lower, upper.Id));
    }

    private sealed class FakeSocket : WebSocket
    {
        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => WebSocketState.Open;

        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));

        public override Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
