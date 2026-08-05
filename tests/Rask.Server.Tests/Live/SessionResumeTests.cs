using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
/// The behaviour the whole thing exists for: a client whose session is gone gets its page back instead of
/// "Your session timed out. Reload to continue."
/// </summary>
/// <remarks>
/// <para>
/// Each test drops the session from the store between the two connections. That is precisely what a
/// restart or a <c>rask deploy</c> container swap looks like from the client's side — the process that
/// held the tree is gone, and the one answering the reconnect never knew it.
/// </para>
/// <para>
/// <b>Not yet wired to the client.</b> The server opens a record and rebuilds the page around it, and the
/// browser sends back whatever record it holds — but nothing pushes a record to the browser yet, so it
/// never holds one in practice. Delivering it as its own WebSocket frame was tried and reverted: it lands
/// in the middle of the render stream, and the frame contract is that a hello with nothing pending emits
/// no frame at all. The record has to ride inside the render payload next to <c>history</c>/<c>auth</c>,
/// which is the outstanding work. These tests seal the record directly so everything downstream of
/// holding one is verified against the real protector.
/// </para>
/// </remarks>
public sealed class SessionResumeTests
{
    private const string StateKey = "counter";

    /// <summary>A page that declares one value and renders it, so a rebuild is visible in the markup.</summary>
    private sealed class CounterApp(IPersistentState state, RouteState route) : Component
    {
        protected override Component? Render()
        {
            state.TryGet<int>(StateKey, out var count);
            return
            [
                Doctype(),
                new Html()[
                    new Head(),
                    new Body()[
                        new Div()[count.ToString()],
                        new Div()[route.Path]
                    ]
                ]
            ];
        }
    }

    /// <summary>
    /// Starts a session, declares some state, and seals the record for it.
    /// </summary>
    /// <remarks>
    /// The record is built straight from the host's own protector rather than read off the socket, because
    /// nothing pushes it to the client yet — see the "not wired to the client" note on the class. That is a
    /// delivery gap, not a semantic one: this is byte-for-byte the record the server will hand out, so
    /// everything downstream of holding one is exercised for real.
    /// </remarks>
    private static async Task<(RaskTestHost Host, string SessionId, string Token)> StartAndCapture(
        int seed, string path = "/start")
    {
        var host = RaskTestHost.Create<CounterApp>();
        var initial = await host.Http.GetAsync(path);
        var sessionId = SessionIdFrom(await initial.Content.ReadAsStringAsync());

        var session = host.Store.Get(sessionId)!;
        session.Services.GetRequiredService<IPersistentState>().Persist(StateKey, seed);

        return (host, sessionId, SealFor(host, session));
    }

    private static string SealFor(RaskTestHost host, LiveSession session)
    {
        var protector = host.Services.GetRequiredService<SessionResumeSupport>().Protector;
        Assert.NotNull(protector);

        var route = session.Services.GetRequiredService<RouteState>();
        var state = session.Services.GetRequiredService<PersistentState>();
        var user = session.Services.GetRequiredService<Rask.Server.Authentication.SessionUserProvider>().Current;

        return protector!.Protect(QueryString.Build(route.Path, route.Query), user, state.Entries);
    }

    [Fact]
    public async Task A_client_whose_session_is_gone_gets_its_page_rebuilt()
    {
        var (host, sessionId, token) = await StartAndCapture(seed: 41);
        using var _ = host;

        // The process that held the tree is gone.
        await host.Store.RemoveAsync(sessionId);
        Assert.Null(host.Store.Get(sessionId));

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId, resume = token });

        var frame = await ReadFrameWithHtmlAsync(ws);

        // The declared state came back, and it is rendered — not merely stored.
        Assert.Contains(">41<", frame, StringComparison.Ordinal);
        // A brand-new session now backs the page.
        Assert.Equal(1, host.Store.Count);
        Assert.Null(host.Store.Get(sessionId));
    }

    /// <summary>Even with nothing declared, the URL survives — which is what makes a deploy a re-render rather than a reload.</summary>
    [Fact]
    public async Task The_route_survives_so_the_user_lands_where_they_were()
    {
        var (host, sessionId, token) = await StartAndCapture(seed: 7, path: "/orders/2026");
        using var _ = host;
        await host.Store.RemoveAsync(sessionId);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId, resume = token });

        var frame = await ReadFrameWithHtmlAsync(ws);

        Assert.Contains("/orders/2026", frame, StringComparison.Ordinal);
    }

    /// <summary>The rebuilt session must be reachable by the id the client now holds, or the NEXT drop loses the page.</summary>
    [Fact]
    public async Task The_rebuilt_session_can_itself_be_resumed()
    {
        var (host, sessionId, token) = await StartAndCapture(seed: 5);
        using var _ = host;
        await host.Store.RemoveAsync(sessionId);

        string secondToken;
        string rebuiltId;
        using (var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None))
        {
            await ws.SendJsonAsync(new { type = "hello", session = sessionId, resume = token });
            // The rebuilt session's id reaches the client the same way it reaches a browser: stamped on
            // the html of the frame it gets back.
            rebuiltId = SessionIdFrom(await ReadFrameWithHtmlAsync(ws));
            secondToken = SealFor(host, host.Store.Get(rebuiltId)!);
        }

        Assert.NotEqual(sessionId, rebuiltId);

        // Drop the rebuilt one too, and go round again with the record it issued.
        await host.Store.RemoveAsync(rebuiltId);

        using var second = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await second.SendJsonAsync(new { type = "hello", session = rebuiltId, resume = secondToken });

        var frame = await ReadFrameWithHtmlAsync(second);
        Assert.Contains(">5<", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_record_an_unknown_session_still_reloads()
    {
        var (host, sessionId, _) = await StartAndCapture(seed: 1);
        using var _2 = host;
        await host.Store.RemoveAsync(sessionId);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(frame);
        Assert.Contains("\"status\":\"unknown\"", frame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_garbage_record_is_refused_and_the_client_is_told_to_reload()
    {
        var (host, sessionId, _) = await StartAndCapture(seed: 1);
        using var _2 = host;
        await host.Store.RemoveAsync(sessionId);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId, resume = "not-a-real-record" });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(frame);
        Assert.Contains("\"status\":\"unknown\"", frame, StringComparison.Ordinal);
        Assert.Equal(0, host.Store.Count);
    }

    /// <summary>
    /// A resume storm follows every deploy — every connected client reconnects at once. Those rebuilds must
    /// shed against MaxSessions like any other new session rather than walking past the cap.
    /// </summary>
    [Fact]
    public async Task A_rebuild_is_refused_when_the_host_is_at_capacity()
    {
        var host = RaskTestHost.Create<CounterApp>();
        using var _ = host;

        var initial = await host.Http.GetAsync("/start");
        var sessionId = SessionIdFrom(await initial.Content.ReadAsStringAsync());
        var session = host.Store.Get(sessionId)!;
        session.Services.GetRequiredService<IPersistentState>().Persist(StateKey, 3);
        var token = SealFor(host, session);

        await host.Store.RemoveAsync(sessionId);

        // Full: the rebuild has nowhere to go.
        host.Store.MaxSessions = 1;
        host.Store.Create(_ => new Span());

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId, resume = token });

        var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(frame);
        Assert.Contains("\"status\":\"unknown\"", frame, StringComparison.Ordinal);
        Assert.Equal(1, host.Store.Count);
    }

    /// <summary>
    /// Turning resume off restores exactly the behaviour that shipped before it: no protector at all, so a
    /// record cannot even be built, and an unknown session reloads.
    /// </summary>
    [Fact]
    public async Task With_resume_disabled_no_record_can_be_built_and_an_unknown_session_reloads()
    {
        var host = RaskTestHost.Create<CounterApp>(configureServer: o => o.SessionResume = false);
        using var _ = host;

        Assert.False(host.Services.GetRequiredService<SessionResumeSupport>().Enabled);

        var initial = await host.Http.GetAsync("/start");
        var sessionId = SessionIdFrom(await initial.Content.ReadAsStringAsync());

        await host.Store.RemoveAsync(sessionId);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        var reply = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(reply);
        Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static string SessionIdFrom(string html)
    {
        const string marker = "data-rask-root=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "the GET shell must carry data-rask-root");
        start += marker.Length;
        return html[start..html.IndexOf('"', start)];
    }


    /// <summary>
    /// Reads frames until one carries rendered html (the rebuild), skipping resume/ack frames, and returns
    /// the html itself rather than the envelope — the envelope is JSON, so its markup is escaped and would
    /// not match anything a test looks for.
    /// </summary>
    private static async Task<string> ReadFrameWithHtmlAsync(WebSocket ws)
    {
        for (var i = 0; i < 8; i++)
        {
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
            if (frame is null)
            {
                break;
            }

            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("html", out var html) && html.ValueKind == JsonValueKind.String)
            {
                return html.GetString()!;
            }
        }

        Assert.Fail("expected a rendered frame on the socket");
        return string.Empty;
    }
}
