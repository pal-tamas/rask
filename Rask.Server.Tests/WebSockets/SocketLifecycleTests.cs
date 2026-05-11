using System.Net;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class SocketLifecycleTests
{
    [Fact]
    public async Task NonWebSocketGet_To_RaskWs_Returns400()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/rask/ws");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SocketDisconnect_SchedulesRemoval_SessionRemovedAfterShortenedGracePeriod()
    {
        var prevGrace = RaskEndpointExtensions.SessionGracePeriod;
        RaskEndpointExtensions.SessionGracePeriod = TimeSpan.FromMilliseconds(50);
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            var initial = await host.Http.GetAsync("/start");
            var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());
            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (host.Store.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(0, host.Store.Count);
        }
        finally
        {
            RaskEndpointExtensions.SessionGracePeriod = prevGrace;
        }
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;
}
