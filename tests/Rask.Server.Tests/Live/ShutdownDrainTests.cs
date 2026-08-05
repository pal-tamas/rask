using System.Diagnostics;
using System.Net.WebSockets;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
/// On shutdown the host tells its clients to reconnect, while their sockets still work.
/// </summary>
/// <remarks>
/// Without it a client learns its host is gone only when the socket drops, and then walks a backoff
/// ladder — up to five seconds of a frozen page — before trying the replacement that has been serving the
/// whole time. During a <c>rask deploy</c> the proxy is switched to the new container <em>before</em> the
/// old one is stopped, so the reconnect this frame triggers lands on a host that is already up.
/// </remarks>
public sealed class ShutdownDrainTests
{
    private sealed class Shell : Component
    {
        protected override Component? Render() =>
            [Doctype(), new Html()[new Head(), new Body()[new H1()["hi"]]]];
    }

    private static async Task<(RaskTestHost Host, WebSocket Ws)> ConnectAsync()
    {
        var host = RaskTestHost.Create<Shell>();
        var html = await host.Http.GetStringAsync("/start");
        var sessionId = MarkupAssert.SessionId(html);
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // Wait for the server to actually process the hello. Sending it only queues bytes; the attach
        // happens on the server's receive loop, and a session with no socket attached cannot be sent to —
        // so without this a loaded machine can shut the host down first and legitimately drain nobody.
        var session = host.Store.Get(sessionId)!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!session.IsAttached && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(session.IsAttached, "the socket never attached, so there was nothing to drain");
        return (host, ws);
    }

    [Fact]
    public async Task A_connected_client_is_told_to_reconnect_before_its_socket_goes()
    {
        var (host, ws) = await ConnectAsync();
        using var _ = host;
        using var _2 = ws;

        // Receive FIRST. A browser sits in a receive loop; a test that stops the host and only then
        // starts reading is racing the socket teardown that follows the drain, and under load loses.
        var receive = ws.TryReceiveTextAsync(TimeSpan.FromSeconds(10));
        await host.StopAsync();

        var frame = await receive;

        Assert.NotNull(frame);
        Assert.Contains("\"type\":\"drain\"", frame, StringComparison.Ordinal);
    }

    /// <summary>
    /// The drain must not become a way to hang shutdown. Everything after it — disposing sessions, the WAL
    /// checkpoint, a Litestream flush — is what actually loses data if the budget runs out, and
    /// `rask deploy` SIGKILLs 20 s after SIGTERM regardless.
    /// </summary>
    [Fact]
    public async Task Shutdown_is_not_delayed_by_the_drain()
    {
        var (host, ws) = await ConnectAsync();
        using var _ = host;
        using var _2 = ws;

        var started = Stopwatch.GetTimestamp();
        await host.StopAsync();
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(elapsed < TimeSpan.FromSeconds(5),
            $"the drain took {elapsed.TotalMilliseconds:F0} ms of the shutdown budget");
    }

    /// <summary>
    /// The ordering the whole design turns on. Sockets are aborted on a token the store cancels once the
    /// drain has finished — not on ApplicationStopping, whose callback order is not guaranteed. If that
    /// regressed, the socket would be gone before the frame reached it and this would time out.
    /// </summary>
    [Fact]
    public async Task The_socket_survives_long_enough_to_carry_the_frame()
    {
        var (host, ws) = await ConnectAsync();
        using var _ = host;
        using var _2 = ws;

        var receive = ws.TryReceiveTextAsync(TimeSpan.FromSeconds(10));
        await host.StopAsync();

        var frame = await receive;
        Assert.NotNull(frame);
        Assert.Contains("\"drain\"", frame, StringComparison.Ordinal);
    }

    /// <summary>A host with nobody connected must not pay for, or trip over, the drain.</summary>
    [Fact]
    public async Task An_idle_host_shuts_down_without_incident()
    {
        using var host = RaskTestHost.Create<Shell>();

        await host.StopAsync();

        Assert.Equal(0, host.Store.Count);
    }
}
