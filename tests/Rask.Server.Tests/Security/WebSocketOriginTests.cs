using System.Net.WebSockets;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Security;

/// <summary>
///     Cross-Site WebSocket Hijacking guard on the live endpoint. The upgrade carries the
///     user's auth cookie and CORS does not apply to WS handshakes, so a mismatched Origin
///     must be rejected. The endpoint reuses the redeem flow's host-only same-origin check
///     (IsSameOrigin, unit-tested via AuthRedeemEndpointTests) — these tests assert the WS
///     handshake is actually wired to it. Clients that send no Origin (non-browser tooling)
///     are allowed; the existing WS suite connects that way.
/// </summary>
public class WebSocketOriginTests
{
    [Fact]
    public async Task CrossOriginHandshake_IsRejected()
    {
        using var host = RaskTestHost.Create<TestApp>();
        host.WebSockets.ConfigureRequest = req => req.Headers["Origin"] = "http://evil.example";

        // TestServer surfaces the non-101 handshake as a thrown exception; the socket never opens.
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None));
        Assert.Contains("403", ex.Message);
    }

    [Fact]
    public async Task SameOriginHandshake_Succeeds()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var origin = new Uri(host.Server.BaseAddress, "/").GetLeftPart(UriPartial.Authority);
        host.WebSockets.ConfigureRequest = req => req.Headers["Origin"] = origin;

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        Assert.Equal(WebSocketState.Open, ws.State);
    }
}
