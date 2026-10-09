using System.Net;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

public class SocketLifecycleTests
{
    // Asserts against the `html` payload field — force the legacy full-HTML wire shape. The
    // LiveDiffMode collection disables parallelization for this class, so this global-static
    // assignment is single-threaded and can't race another class that sets a different DiffMode.

    [Fact]
    public async Task A_plain_GET_to_the_socket_endpoint_answers_400()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);

        var response = await host.Http.GetAsync("/rask/ws", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_socket_disconnect_schedules_removal_and_the_session_goes_after_the_shortened_grace_period()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull,
            configureServer: o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(50));
        var initial = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await WaitFor.True(
            () => host.Store.Count == 0, LiveFrames.HangCeiling, "the session is removed once its grace period ends");

        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task A_reconnect_before_the_grace_period_ends_attaches_to_the_existing_session()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull,
            configureServer: o => o.SessionGracePeriod = TimeSpan.FromSeconds(2));
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws1.AttachedAsync(host, sessionId);
        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        // Reconnect well inside the grace window.
        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        var rerender = await ws2.ReceiveTextAsync();

        Assert.Contains("count=", rerender);
        Assert.Equal(1, host.Store.Count);
    }

    [Fact]
    public async Task A_hello_after_the_grace_period_expires_is_rejected()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull,
            configureServer: o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(50));
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws1.AttachedAsync(host, sessionId);
        await ws1.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await WaitFor.True(
            () => host.Store.Count == 0, LiveFrames.HangCeiling, "the session is removed once its grace period ends");

        Assert.Equal(0, host.Store.Count);

        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        var reply = await ws2.ReceiveTextAsync();

        Assert.Contains("\"status\":\"unknown\"", reply);
    }

    [Fact]
    public async Task A_reconnect_while_the_old_socket_is_attached_makes_the_new_socket_authoritative()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initial = await host.Http.GetAsync("/start", TestContext.Current.CancellationToken);
        var initialHtml = await initial.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = Regex.Match(initialHtml, "data-rask-on-click=\"(h\\d+)\"").Groups[1].Value;

        using var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws1.AttachedAsync(host, sessionId);

        // Open ws2 with the same session id while ws1 is still attached. A reconnect always emits a
        // frame, so reading it proves ws2 is the attached one.
        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws2.ReceiveTextAsync();

        // ws2 is authoritative now; a handler invocation should render to ws2.
        await ws2.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var ws2Reply = await ws2.ReceiveTextAsync();

        Assert.Contains("count=1", ws2Reply);
    }

    // #1076: the tab reconnects before the server notices its old socket died, so the OLD loop's cleanup
    // runs after the new socket attached. That cleanup must leave the new socket alone: still attached,
    // still counted, and the session not scheduled for removal under an open tab.
    [Fact]
    public async Task The_old_sockets_cleanup_after_a_reconnect_leaves_the_new_socket_live()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull,
            configureServer: o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(50));
        var initialHtml = await host.Http.GetStringAsync("/start", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = Regex.Match(initialHtml, "data-rask-on-click=\"(h\\d+)\"").Groups[1].Value;

        using var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await WaitFor.True(() => host.Store.ConnectedCount == 1, TimeSpan.FromSeconds(5));

        // A reconnect always emits a frame, so receiving one proves ws2 is attached.
        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);

        await ws2.ReceiveTextAsync();

        // Only now does the first socket's loop finish.
        await ws1.CloseAndAwaitServerCleanupAsync();

        // Outlast the 50 ms grace a wrongly armed removal would use.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Equal(1, host.Store.Count);
        Assert.Equal(1, host.Store.ConnectedCount);

        // And ws2 still receives renders.
        await ws2.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);
        var reply = await ws2.ReceiveTextAsync();

        Assert.Contains("count=1", reply);

        // Its own close is the one that counts.
        await ws2.CloseAndAwaitServerCleanupAsync();

        Assert.Equal(0, host.Store.ConnectedCount);
    }

    // A socket another hello replaced must not keep driving the session: it was admitted for whoever the
    // session belonged to then, and a sign-in on the new socket may have changed that (#1075).
    [Fact]
    public async Task A_replaced_socket_cannot_dispatch_handlers()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull);
        var initialHtml = await host.Http.GetStringAsync("/start", TestContext.Current.CancellationToken);
        var sessionId = MarkupAssert.SessionId(initialHtml);
        var handlerId = Regex.Match(initialHtml, "data-rask-on-click=\"(h\\d+)\"").Groups[1].Value;

        using var ws1 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws1.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await WaitFor.True(() => host.Store.ConnectedCount == 1, TimeSpan.FromSeconds(5));

        using var ws2 = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws2.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws2.ReceiveTextAsync();

        // The replaced socket clicks: had it dispatched, the render would arrive on ws2, the attached one.
        await ws1.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);

        Assert.Null(await ws2.TryReceiveTextAsync(TimeSpan.FromMilliseconds(500)));

        // The attached socket's click is the first one that counts.
        await ws2.SendJsonAsync(new { id = handlerId }, ct: TestContext.Current.CancellationToken);

        var reply = await ws2.ReceiveTextAsync();
        Assert.Contains("count=1", reply);
        Assert.Equal(WebSocketState.Open, ws1.State);
    }

    [Fact]
    public async Task A_close_with_a_custom_reason_removes_the_session_after_the_grace_period()
    {
        using var host = RaskTestHost.Create<TestApp>(diffMode: LiveDiffMode.DisabledFull,
            configureServer: o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(50));
        var sessionId = MarkupAssert.SessionId(await (await host.Http.GetAsync("/start", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, ct: TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);

        await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "policy-violation-bye",
            CancellationToken.None);

        await WaitFor.True(
            () => host.Store.Count == 0, LiveFrames.HangCeiling, "the session is removed once its grace period ends");

        Assert.Equal(0, host.Store.Count);
    }
}
