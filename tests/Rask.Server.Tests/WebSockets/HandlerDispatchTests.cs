using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class HandlerDispatchTests
{
    // Asserts against the `html` payload field — force the legacy full-HTML wire
    // shape (framework default is now LiveDiffMode.Auto).

    [Fact]
    public async Task HandlerId_KnownHandler_InvokesAndSendsRender()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

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
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var sessionId = MarkupAssert.SessionId(await initial.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = "h999" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.Null(text);
        Assert.Equal(WebSocketState.Open, ws.State);
    }

    [Fact]
    public async Task HandlerId_FrameTypeThatCannotFeedTheHandler_IsIgnored()
    {
        // Action ids are positional per render, so a frame the client sent against an earlier tree
        // resolves to whatever now occupies that slot. h0 here is the parameterless "bump" click; an
        // `input` frame landing on it used to RUN it, with nothing to say the wrong thing had fired.
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { id = handlerId, type = "input", value = "x" });

        // No render: the counter was never bumped. Answered exactly like the stale id it is.
        Assert.Null(await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400)));
        Assert.Equal(WebSocketState.Open, ws.State);

        // ...and the socket still dispatches the frame that DOES fit.
        await ws.SendJsonAsync(new { id = handlerId, type = "click" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.Contains("count=1", doc.RootElement.GetProperty("html").GetString()!);
    }

    [Fact]
    public async Task Message_NoIdAndNoType_Ignored()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var sessionId = MarkupAssert.SessionId(await initial.Content.ReadAsStringAsync());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        await ws.SendJsonAsync(new { foo = "bar" });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(400));

        Assert.Null(text);
    }

    [Fact]
    public async Task MalformedJson_IsDropped_SessionSurvivesAndKeepsDispatching()
    {
        // A single malformed frame must NOT tear down the live session. Previously the
        // unguarded JsonDocument.Parse threw JsonException out of the receive loop, detaching
        // the socket and scheduling the session for removal — one bad (buggy or adversarial)
        // frame dropped the whole session. Now it is dropped and the loop keeps serving.
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        _ = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, host.Store.Count);

        var bytes = Encoding.UTF8.GetBytes("{not-json");
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);

        // No teardown: the socket stays open, the session is not removed, and a subsequent
        // valid handler frame still dispatches and renders.
        await ws.SendJsonAsync(new { id = handlerId });
        var text = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(text);
        using var doc = JsonDocument.Parse(text!);
        Assert.Contains("count=1", doc.RootElement.GetProperty("html").GetString()!);
        Assert.Equal(WebSocketState.Open, ws.State);
        Assert.Equal(1, host.Store.Count);
    }

    [Fact]
    public async Task Message_MissingType_ButHasOtherFields_Ignored()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start")).Content.ReadAsStringAsync());

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
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = MarkupAssert.FirstHandlerId(initialHtml);

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
        using var host = RaskTestHost.Create<ThrowingApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start");
        var initialHtml = await initial.Content.ReadAsStringAsync();
        var sessionId = MarkupAssert.SessionId(initialHtml);

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
}
