using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core.Diagnostics;
using Rask.Server.Diagnostics;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
///     Why a tab reconnected or reloaded is otherwise invisible on the server: each connection's opening, attach and
///     close, and each session's making and removal, are logged on <c>Rask.Live</c> — by fingerprint, never by id.
/// </summary>
[Collection("DiagnosticsSink")]
public sealed class SessionLifecycleLogTests
{
    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_first_connection_logs_its_opening_and_its_attach_by_fingerprint(LiveTransportKind kind)
    {
        var log = new CapturedLog();
        using var host = HostWith(log);
        var sessionId = await OpenPageAsync(host);

        await using var connection = await LiveTestConnection.OpenAsync(host, kind, sessionId);
        var attached = await LineAsync(log, "attached to session");

        Assert.Contains(log.Entries, static e => e.Message.EndsWith("connection 1 opened.", StringComparison.Ordinal));
        Assert.Contains($"#{SessionFingerprint.Of(sessionId)}: its first connection", attached.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Debug, attached.Level);
        Assert.DoesNotContain(log.Entries, e => e.Category == SessionLifecycleLog.Category && e.Message.Contains(sessionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_connection_the_client_closes_logs_who_closed_it_and_the_grace_the_session_waits()
    {
        var log = new CapturedLog();
        using var host = HostWith(log);
        var sessionId = await OpenPageAsync(host);
        using var ws = await AttachAsync(host, sessionId);

        await ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "tab closed", TestContext.Current.CancellationToken);
        var closed = await LineAsync(log, "closed by the");

        Assert.Contains("closed by the client (close status 1001)", closed.Message, StringComparison.Ordinal);
        Assert.Contains("the session is kept 30 s for a reconnect", closed.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Debug, closed.Level);
    }

    [Fact]
    public async Task A_reconnect_logs_its_wait_and_the_whole_page_it_was_sent_with_its_bytes()
    {
        var log = new CapturedLog();
        using var host = HostWith(log);
        var sessionId = await OpenPageAsync(host);
        using (var first = await AttachAsync(host, sessionId))
        {
            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
            await LineAsync(log, "closed by the");
        }

        using var second = await AttachAsync(host, sessionId);
        var attached = await LineAsync(log, "a reconnect, since its last connection went");

        Assert.Contains("connection 2 of the session", attached.Message, StringComparison.Ordinal);
        Assert.Matches(@"catch-up: whole page, [1-9]\d* B\.$", attached.Message);
    }

    [Fact]
    public async Task A_session_nothing_connects_to_logs_its_removal_with_why_and_the_limit()
    {
        var log = new CapturedLog();
        using var host = HostWith(log, o => o.UnconnectedSessionGracePeriod = TimeSpan.FromMilliseconds(200));

        var sessionId = await OpenPageAsync(host);
        var removed = await LineAsync(log, "removed:");

        Assert.Contains($"session #{SessionFingerprint.Of(sessionId)} removed: no connection attached within the "
                        + "unconnected grace period", removed.Message, StringComparison.Ordinal);
        Assert.Contains("0 connection(s)", removed.Message, StringComparison.Ordinal);
        Assert.Contains("limit 0.2 s", removed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tab_that_comes_back_to_a_removed_session_is_told_when_and_why_it_went()
    {
        var log = new CapturedLog();
        using var host = HostWith(log, o => o.UnconnectedSessionGracePeriod = TimeSpan.FromMilliseconds(100));
        var sessionId = await OpenPageAsync(host);
        await LineAsync(log, "removed:");
        var refusals = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = refusals.Enqueue;

        try
        {
            using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
            await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
            await ws.ReceiveUntilAsync(static f => f.Contains("\"status\":\"unknown\"", StringComparison.Ordinal), "the refusal");
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        var refused = Assert.Single(refusals, static e => e.Message.Contains("refused", StringComparison.Ordinal));
        Assert.Contains("it was removed", refused.Message, StringComparison.Ordinal);
        Assert.Contains("no connection attached within the unconnected grace period", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connection_the_server_closes_for_a_safety_cap_is_logged_at_information()
    {
        var log = new CapturedLog();
        using var host = HostWith(log, o => o.MaxInboundFramesPerSecond = 2);
        var sessionId = await OpenPageAsync(host);
        using var ws = await AttachAsync(host, sessionId);

        for (var i = 0; i < 5 && ws.State == WebSocketState.Open; i++)
        {
            await ws.SendJsonAsync(new { type = "noop" }, TestContext.Current.CancellationToken);
        }

        var closed = await LineAsync(log, "closed by the server");

        Assert.Contains("(frame rate)", closed.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Information, closed.Level);
    }

    [Fact]
    public async Task A_page_refused_at_capacity_is_logged_at_information()
    {
        var log = new CapturedLog();
        using var host = HostWith(log);
        host.Store.MaxSessions = 1;
        await OpenPageAsync(host);

        using var refused = await host.Http.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        var line = Assert.Single(log.Entries, static e => e.Message.Contains("refused a new session", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("limit of 1", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_debug_off_the_lifecycle_writes_nothing()
    {
        var log = new CapturedLog();
        using var host = HostWith(log, minimum: LogLevel.Information);
        var sessionId = await OpenPageAsync(host);

        using (var ws = await AttachAsync(host, sessionId))
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", TestContext.Current.CancellationToken);
        }

        await WaitFor.True(() => host.Store.ConnectedCount == 0, "the connection to detach");
        Assert.DoesNotContain(log.Entries, static e => e.Category == SessionLifecycleLog.Category && e.Level < LogLevel.Information);
    }

    private static RaskTestHost HostWith(
        CapturedLog log, Action<RaskServerOptions>? configureServer = null, LogLevel minimum = LogLevel.Debug) =>
        RaskTestHost.Create<NoOpApp>(
            configureServices: services => services.AddLogging(logging =>
            {
                logging.AddProvider(log);
                logging.SetMinimumLevel(minimum);
                logging.AddFilter(SessionLifecycleLog.Category, minimum);
            }),
            configureServer: configureServer);

    private static async Task<string> OpenPageAsync(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        return MarkupAssert.SessionId(html);
    }

    private static async Task<WebSocket> AttachAsync(RaskTestHost host, string sessionId)
    {
        var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, TestContext.Current.CancellationToken);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId }, TestContext.Current.CancellationToken);
        await ws.AttachedAsync(host, sessionId);
        return ws;
    }

    private static async Task<CapturedLog.Entry> LineAsync(CapturedLog log, string containing)
    {
        await WaitFor.True(
            () => log.Entries.Any(e => e.Message.Contains(containing, StringComparison.Ordinal)),
            $"a line saying \"{containing}\"");
        return log.Entries.First(e => e.Message.Contains(containing, StringComparison.Ordinal));
    }
}
