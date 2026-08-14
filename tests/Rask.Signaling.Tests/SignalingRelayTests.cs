using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Signaling.Tests;

// End-to-end over a real WebSocket: the relay's guarantees only mean anything at the wire.
public class SignalingRelayTests : IDisposable
{
    // Owned by the test instance (xUnit builds one per test), so the timer is rooted for the test's whole
    // life. A CancellationTokenSource created inline and immediately dropped can be collected before its
    // timer fires, and then the token never cancels — a socket wait that should time out hangs the run
    // instead. That is not hypothetical: it is what this suite did when a deliberately broken relay stopped
    // answering.
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(10));

    public void Dispose() => _cts.Dispose();

    [Fact]
    public async Task TwoPeersInARoom_CanReachEachOther()
    {
        using var host = Host();

        var (first, firstId, _) = await JoinAsync(host, "room");
        var (second, _, peers) = await JoinAsync(host, "room");

        // The joiner learns who was already there, which is how an app decides who offers.
        Assert.Equal([firstId], peers);

        // The first peer is told about the arrival.
        var announced = await ReadAsync(first);
        Assert.Equal("peer-joined", announced.GetProperty("type").GetString());

        await SendAsync(second, new { type = "signal", to = firstId, payload = "an-offer" });

        var delivered = await ReadAsync(first);
        Assert.Equal("signal", delivered.GetProperty("type").GetString());
        Assert.Equal("an-offer", delivered.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task APeerCannotAddressSomeoneInAnotherRoom()
    {
        using var host = Host();

        var (_, hereId, _) = await JoinAsync(host, "room-a");
        var (elsewhere, _, _) = await JoinAsync(host, "room-b");

        await SendAsync(elsewhere, new { type = "signal", to = hereId, payload = "leaked" });

        var reply = await ReadAsync(elsewhere);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("no such peer", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task APeerCannotAddressItself()
    {
        // Otherwise the relay is an echo service any client can aim at itself.
        using var host = Host();
        var (socket, id, _) = await JoinAsync(host, "room");

        await SendAsync(socket, new { type = "signal", to = id, payload = "echo" });

        var reply = await ReadAsync(socket);
        Assert.Equal("error", reply.GetProperty("type").GetString());
    }

    [Fact]
    public async Task SignallingBeforeJoining_IsRefused()
    {
        using var host = Host();
        var socket = await ConnectAsync(host);

        await SendAsync(socket, new { type = "signal", to = "someone", payload = "x" });

        var reply = await ReadAsync(socket);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("join first", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AnOversizedPayload_IsRefused_AndTheSocketSurvives()
    {
        using var host = Host(o => o.MaxPayloadBytes = 256);
        var (socket, _, _) = await JoinAsync(host, "room");

        await SendAsync(socket, new { type = "signal", to = "whoever", payload = new string('x', 512) });

        var reply = await ReadAsync(socket);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("payload too large", reply.GetProperty("message").GetString());
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task AFullRoom_RefusesTheNextPeer()
    {
        using var host = Host(o => o.MaxPeersPerRoom = 2);
        await JoinAsync(host, "room");
        await JoinAsync(host, "room");

        var third = await ConnectAsync(host);
        await SendAsync(third, new { type = "join", room = "room" });

        var reply = await ReadAsync(third);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("cannot join", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AnUnauthorizedRoom_IsRefused_WithTheSameWordsAsAFullOne()
    {
        // The wording must not distinguish "no such room" from "not allowed in": either would let an
        // unauthorized caller probe which rooms exist.
        using var host = Host(o => o.AuthorizeRoom = c => ValueTask.FromResult(c.Room == "allowed"));
        var socket = await ConnectAsync(host);

        await SendAsync(socket, new { type = "join", room = "secret" });

        var reply = await ReadAsync(socket);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("cannot join", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AuthorizeRoom_SeesTheRoomTheCallerAskedFor()
    {
        string? seen = null;
        using var host = Host(o => o.AuthorizeRoom = c =>
        {
            seen = c.Room;
            return ValueTask.FromResult(true);
        });

        await JoinAsync(host, "the-room");

        Assert.Equal("the-room", seen);
    }

    [Fact]
    public async Task JoiningTwice_IsRefused()
    {
        using var host = Host();
        var (socket, _, _) = await JoinAsync(host, "room");

        await SendAsync(socket, new { type = "join", room = "another" });

        var reply = await ReadAsync(socket);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal("already joined", reply.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AMalformedMessage_IsRefused_AndTheSocketSurvives()
    {
        using var host = Host();
        var (socket, _, _) = await JoinAsync(host, "room");

        await socket.SendAsync(
            Encoding.UTF8.GetBytes("not json"), WebSocketMessageType.Text, true, _cts.Token);

        var reply = await ReadAsync(socket);
        Assert.Equal("error", reply.GetProperty("type").GetString());
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task ALeavingPeer_IsAnnouncedToTheRest()
    {
        using var host = Host();
        var (first, _, _) = await JoinAsync(host, "room");
        var (second, secondId, _) = await JoinAsync(host, "room");
        await ReadAsync(first); // peer-joined

        // CloseOutputAsync, not CloseAsync: we still want to read `first`'s announcement, and CloseAsync
        // would block this side waiting on a handshake we don't care about here.
        await second.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, _cts.Token);

        var announced = await ReadAsync(first);
        Assert.Equal("peer-left", announced.GetProperty("type").GetString());
        Assert.Equal(secondId, announced.GetProperty("peerId").GetString());
    }

    [Fact]
    public async Task AHostWithoutUseWebSockets_SaysWhichCallIsMissing()
    {
        // Without the middleware there is no upgrade feature, and IsWebSocketRequest is false for every
        // request — so the relay would refuse perfectly good clients with a bare 400. That is exactly the
        // shape of bug that costs an afternoon, so the response names the missing call.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddRaskSignaling(o => o.RequireAuthorization = false);

        var app = builder.Build();
        app.UseRouting();
        // Deliberately no app.UseWebSockets().
        app.MapRaskSignaling();
        await app.StartAsync();

        try
        {
            using var client = app.GetTestServer().CreateClient();
            var response = await client.GetAsync(new Uri("/rask/signaling", UriKind.Relative), _cts.Token);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Contains(
                "UseWebSockets", await response.Content.ReadAsStringAsync(_cts.Token), StringComparison.Ordinal);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void MapRaskSignaling_WithoutAddRaskSignaling_SaysSo()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        var app = builder.Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.MapRaskSignaling());
        Assert.Contains("AddRaskSignaling", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no-leading-slash")]
    public void AddRaskSignaling_RejectsAPathThatIsNotRooted(string path)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddRaskSignaling(o => o.Path = path));
    }

    [Fact]
    public void AddRaskSignaling_RejectsAPayloadCapAboveTheMessageCap()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddRaskSignaling(o =>
        {
            o.MaxMessageBytes = 2048;
            o.MaxPayloadBytes = 4096;
        }));
    }

    private static SignalingTestHost Host(Action<RaskSignalingOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddRaskSignaling(o =>
        {
            // The relay defaults to requiring authentication; these tests are about the relay's own rules,
            // so they opt out rather than standing up an auth scheme. AnUnauthorizedRoom… covers the hook.
            o.RequireAuthorization = false;
            configure?.Invoke(o);
        });

        var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        app.MapRaskSignaling();
        app.StartAsync().GetAwaiter().GetResult();

        return new SignalingTestHost(app, app.GetTestServer());
    }

    private async Task<WebSocket> ConnectAsync(SignalingTestHost host)
    {
        var client = host.Server.CreateWebSocketClient();
        var uri = new Uri(new Uri(host.Server.BaseAddress, "/rask/signaling").ToString().Replace("http://", "ws://"));
        return await client.ConnectAsync(uri, _cts.Token);
    }

    private async Task<(WebSocket Socket, string PeerId, string[] Peers)> JoinAsync(
        SignalingTestHost host, string room)
    {
        var socket = await ConnectAsync(host);
        await SendAsync(socket, new { type = "join", room });

        var joined = await ReadAsync(socket);
        Assert.Equal("joined", joined.GetProperty("type").GetString());
        return (socket,
            joined.GetProperty("peerId").GetString()!,
            [.. joined.GetProperty("peers").EnumerateArray().Select(p => p.GetString()!)]);
    }

    private Task SendAsync(WebSocket socket, object message) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true,
            _cts.Token);

    private async Task<JsonElement> ReadAsync(WebSocket socket)
    {
        var buffer = new byte[8 * 1024];
        var result = await socket.ReceiveAsync(buffer, _cts.Token);
        return JsonDocument.Parse(buffer.AsMemory(0, result.Count)).RootElement.Clone();
    }

    private sealed class SignalingTestHost(WebApplication app, TestServer server) : IDisposable
    {
        public TestServer Server { get; } = server;

        public void Dispose() => app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
