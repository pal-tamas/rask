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

    [Fact]
    public async Task Reconnect_BeforeGracePeriodDeadline_AttachesToExistingSession()
    {
        var prev = RaskEndpointExtensions.SessionGracePeriod;
        RaskEndpointExtensions.SessionGracePeriod = TimeSpan.FromSeconds(2);
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            var sessionId = ExtractSessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

            var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws1.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws1.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

            // Reconnect well inside the grace window.
            using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
            var rerender = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(rerender);
            Assert.Contains("count=", rerender);
            Assert.Equal(1, host.Store.Count);
        }
        finally
        {
            RaskEndpointExtensions.SessionGracePeriod = prev;
        }
    }

    [Fact]
    public async Task Reconnect_AfterGracePeriodExpires_HelloIsRejected()
    {
        var prev = RaskEndpointExtensions.SessionGracePeriod;
        RaskEndpointExtensions.SessionGracePeriod = TimeSpan.FromMilliseconds(50);
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            var sessionId = ExtractSessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

            var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws1.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws1.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (host.Store.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(0, host.Store.Count);

            using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
            var reply = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(reply);
            Assert.Contains("\"status\":\"unknown\"", reply);
        }
        finally
        {
            RaskEndpointExtensions.SessionGracePeriod = prev;
        }
    }

    [Fact]
    public async Task Reconnect_WhileExistingSocketAttached_NewSocketBecomesAuthoritative()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var handlerId = Regex.Match(initialHtml, "data-rask-on-click=\"(h\\d+)\"").Groups[1].Value;

        using var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws1.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Open ws2 with the same session id while ws1 is still attached.
        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // ws2 is authoritative now; a handler invocation should render to ws2.
        await ws2.SendJsonAsync(new { id = handlerId });
        var ws2Reply = await ws2.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(ws2Reply);
        Assert.Contains("count=1", ws2Reply);
    }

    [Fact]
    public async Task Close_WithCustomReason_RemovesSessionAfterGrace()
    {
        var prev = RaskEndpointExtensions.SessionGracePeriod;
        RaskEndpointExtensions.SessionGracePeriod = TimeSpan.FromMilliseconds(50);
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            var sessionId = ExtractSessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

            var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "policy-violation-bye",
                CancellationToken.None);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (host.Store.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            Assert.Equal(0, host.Store.Count);
        }
        finally
        {
            RaskEndpointExtensions.SessionGracePeriod = prev;
        }
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;
}
