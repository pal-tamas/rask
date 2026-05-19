using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

[Collection("SessionGracePeriod")]
public class HandlerDispatchTests
{
    [Fact]
    public async Task HandlerId_KnownHandler_InvokesAndSendsRender()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var handlerId = ExtractFirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = handlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Contains("count=1", html);
    }

    [Fact]
    public async Task HandlerId_UnknownHandler_NoPayload()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = "h999" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task Message_NoIdAndNoType_Ignored()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var sessionId = ExtractSessionId(await initial.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { foo = "bar" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.Null(text);
    }

    [Fact]
    public async Task MalformedJson_PayloadCausesSessionToBeRemoved()
    {
        var prev = RaskEndpointExtensions.SessionGracePeriod;
        RaskEndpointExtensions.SessionGracePeriod = TimeSpan.FromMilliseconds(50);
        try
        {
            using var host = RaskTestHost.Create<TestApp>();
            var sessionId = ExtractSessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId });
            _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, host.Store.Count);

            var bytes = Encoding.UTF8.GetBytes("{not-json");
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

            // Socket loop tears down on JsonException; the session is then scheduled for removal.
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

    [Fact]
    public async Task Message_MissingType_ButHasOtherFields_Ignored()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var sessionId = ExtractSessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { foo = "bar", x = 1 });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task ConcurrentHandlerInvocations_SerialisedByPerSessionLock()
    {
        using var host = RaskTestHost.Create<TestApp>();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);
        var handlerId = ExtractFirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        for (var i = 0; i < 5; i++)
        {
            await ws.SendJsonAsync(new { id = handlerId });
        }

        var counts = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(text);
            var match = Regex.Match(text!, "count=(\\d+)");
            Assert.True(match.Success);
            counts.Add(int.Parse(match.Groups[1].Value));
        }

        // Lock guarantees sequential, monotonically increasing count values 1..5.
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, counts);
    }

    [Fact]
    public async Task HandlerThatThrows_TripsImplicitRootBoundary_AndDispatcherKeepsRunning()
    {
        using var host = RaskTestHost.Create<ThrowingApp>();
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = ExtractSessionId(initialHtml);

        var handlerIds = Regex.Matches(initialHtml, "data-rask-on-click=\"(h\\d+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Equal(2, handlerIds.Count);
        var throwingId = handlerIds[0];

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        // Now that UseRask<TApp> wraps the App in an implicit RootErrorBoundary, a handler
        // throw trips the boundary and the next render replaces the App's tree with the
        // built-in DefaultErrorPage. The dispatcher must remain open afterwards.
        await ws.SendJsonAsync(new { id = throwingId });
        var afterThrow = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(afterThrow);
        Assert.Contains("rask-error-boundary", afterThrow);
        Assert.Contains("Something went wrong", afterThrow);
        Assert.DoesNotContain("count=", afterThrow);

        // Unknown id post-trip still gets handled gracefully (no payload, socket alive).
        await ws.SendJsonAsync(new { id = "h999" });
        var unknown = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));
        Assert.Null(unknown);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    private static string ExtractSessionId(string html) =>
        Regex.Match(html, "data-rask-root=\"([^\"]+)\"").Groups[1].Value;

    private static string ExtractFirstHandlerId(string html)
    {
        var match = Regex.Match(html, "data-rask-on-click=\"(h\\d+)\"");
        Assert.True(match.Success, $"no handler attribute found in html: {html}");
        return match.Groups[1].Value;
    }
}
