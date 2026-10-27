using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Transport;

namespace Rask.Server.Tests.Live;

/// <summary>
///     Which connection a session is attached to, when two of them overlap.
/// </summary>
/// <remarks>
///     <para>
///         A tab that reconnects quickly runs two connections at once for a moment: the new one attaches
///         from its own <c>hello</c> while the old one is still unwinding through its <c>finally</c>. The
///         old cleanup cleared the session's connection unconditionally, so it detached the LIVE one —
///         renders stopped reaching a client that was sitting there connected, and the session was armed
///         for removal underneath it (#1076).
///     </para>
///     <para>
///         The same window is why an attach reports whether it replaced a connection that was still attached:
///         only an attach to a session with none counts, and only a detach that wins uncounts, so a replaced
///         connection is never counted twice and a resumed one is never left uncounted (#1059).
///     </para>
/// </remarks>
public sealed class TransportAttachTests
{
    private sealed class Shell : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render() => Markup.H1["hi"];
    }

    /// <summary>A connection that records what it was asked to carry.</summary>
    private sealed class FakeTransport : ILiveTransport
    {
        public bool IsOpen { get; set; } = true;

        public List<string> Sent { get; } = [];

        public int Aborts { get; private set; }

        public string? ClosedWith { get; private set; }

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
        {
            if (!IsOpen)
            {
                // As a closed WebSocket does.
                return ValueTask.FromException(new System.Net.WebSockets.WebSocketException("closed"));
            }

            Sent.Add(System.Text.Encoding.UTF8.GetString(frame.Span));
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct)
        {
            ClosedWith = description;
            IsOpen = false;
            return Task.CompletedTask;
        }

        public void Abort()
        {
            Aborts++;
            IsOpen = false;
        }
    }

    private static LiveSessionStore NewStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RaskServerLimits());
        services.AddScoped<Rask.Core.Routing.RouteState>();
        services.AddScoped<Rask.Core.Routing.Navigator>();
        var sp = services.BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public async Task An_older_connections_cleanup_leaves_the_newer_one_attached()
    {
        var session = NewStore().Create(_ => new Shell());
        var first = new FakeTransport();
        var second = new FakeTransport();

        session.AttachTransport(first, CancellationToken.None);
        session.AttachTransport(second, CancellationToken.None);

        // The first connection's loop unwinds here, after the reconnect has attached the second one.
        Assert.False(session.DetachTransport(first), "the first connection was no longer the attached one");

        await session.SendOutOfBandAsync("still here"u8.ToArray());

        Assert.Equal(["still here"], second.Sent);
        Assert.Empty(first.Sent);
    }

    [Fact]
    public async Task Detaching_the_attached_connection_reports_it_and_stops_the_sends()
    {
        var session = NewStore().Create(_ => new Shell());
        var transport = new FakeTransport();

        session.AttachTransport(transport, CancellationToken.None);

        Assert.True(session.DetachTransport(transport), "the attached connection detaches");
        Assert.False(session.DetachTransport(transport), "and only the first detach owns it");

        await session.SendOutOfBandAsync("nobody is listening"u8.ToArray());

        Assert.Empty(transport.Sent);
    }

    [Fact]
    public async Task A_closed_connection_is_not_written_to()
    {
        var session = NewStore().Create(_ => new Shell());
        var transport = new FakeTransport();

        session.AttachTransport(transport, CancellationToken.None);
        await transport.CloseAsync(LiveTransportClose.GoingAway, "server-shutdown", CancellationToken.None);

        await session.SendOutOfBandAsync("too late"u8.ToArray());

        Assert.Empty(transport.Sent);
    }

    /// <summary>
    ///     The same race one step later: a stale connection's cleanup detaches just before the new connection
    ///     attaches, and arms a removal the new attach never sees. Nothing else would cancel it, so the session
    ///     was disposed under a connected tab when the grace period ran out.
    /// </summary>
    [Fact]
    public async Task A_removal_armed_under_a_connected_session_does_not_remove_it()
    {
        var store = NewStore();
        var session = store.Create(_ => new Shell());
        session.AttachTransport(new FakeTransport(), CancellationToken.None);

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(20));
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Same(session, store.Peek(session.Id));
    }

    /// <summary>A lookup that has not yet proved anything must not keep a detached session alive.</summary>
    [Fact]
    public async Task Peeking_at_a_session_leaves_its_pending_removal_armed()
    {
        var store = NewStore();
        var session = store.Create(_ => new Shell());

        store.ScheduleRemoval(session.Id, TimeSpan.FromMilliseconds(20));

        Assert.Same(session, store.Peek(session.Id));

        for (var i = 0; i < 50 && store.Peek(session.Id) is not null; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Null(store.Peek(session.Id));
    }

    /// <summary>A connection whose first send fails, the way a client that drops mid-attach makes it fail.</summary>
    private sealed class FailingTransport : ILiveTransport
    {
        public bool IsOpen => true;

        public ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct) =>
            ValueTask.FromException(new System.Net.WebSockets.WebSocketException("the client went away"));

        public Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct) => Task.CompletedTask;

        public void Abort()
        {
        }
    }

    /// <summary>
    ///     An attach whose render throws never tells its caller it attached, so the caller's cleanup cannot undo it.
    ///     The attach has to: otherwise the session keeps a dead connection with no removal armed, and the connected
    ///     count — what the health check reports from — never comes back down.
    /// </summary>
    [Fact]
    public async Task An_attach_whose_render_throws_undoes_itself()
    {
        using var host = Infrastructure.RaskTestHost.Create<Infrastructure.TestApp>(
            configureServer: o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(50));
        var sessionId = MarkupAssert.SessionId(await host.Http.GetStringAsync("/start", TestContext.Current.CancellationToken));
        var session = host.Store.Peek(sessionId)!;

        // A previous connection came and went, so the next attach owes the tab a catch-up frame — the render
        // that is going to fail.
        var earlier = new FakeTransport();
        session.AttachTransport(earlier, CancellationToken.None);
        session.DetachTransport(earlier);

        var services = host.Services;
        await Assert.ThrowsAsync<System.Net.WebSockets.WebSocketException>(() => RaskEndpointExtensions.AttachAsync(
            sessionId, resumeToken: null, sentBefore: 0, new FailingTransport(), host.Store,
            services.GetRequiredService<RaskServerLimits>(),
            new System.Security.Claims.ClaimsPrincipal(),
            services.GetRequiredService<SessionResumeSupport>(),
            // Only a resume rebuild consults it; this session is known.
            new RaskRootSelector(_ => new Shell(), []),
            metrics: null, resumeCulture: default, CancellationToken.None));

        Assert.Equal(0, host.Store.ConnectedCount);
        Assert.False(session.HasOpenTransport);

        // And the grace period was armed, so the session goes rather than lingering for good.
        for (var i = 0; i < 50 && host.Store.Peek(sessionId) is not null; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Null(host.Store.Peek(sessionId));
    }

    /// <summary>A page whose render runs <see cref="During" /> first: where a connection can go under a render.</summary>
    private sealed class Watched : Component
    {
        internal Action? During;

        internal int Count;

        internal int Ticks;

        protected override string? HtmlLang => null;

        protected override Component? Render()
        {
            During?.Invoke();
            // A page long enough that a change to it goes as a diff, not as the whole page again.
            return Markup.Div[
                Markup.H1[$"count {Count}"],
                Markup.P[$"ticks {Ticks}"],
                Enumerable.Range(0, 40).Select(static line => Markup.P.Key($"line-{line}")[$"line {line} of a page that stays as it is"])];
        }
    }

    /// <summary>
    ///     A connection that goes while a handler's render is being made: the render was checked against it before
    ///     the walk, and sent after. The send read it again, found none and threw a NullReferenceException into the
    ///     handler, and the frame was lost (#1258).
    /// </summary>
    [Fact]
    public async Task A_frame_whose_connection_goes_during_its_render_is_sent_to_the_next_one()
    {
        Watched? page = null;
        var session = NewStore().Create(_ => page = new Watched());
        var first = new FakeTransport();
        var second = new FakeTransport();
        session.AttachTransport(first, CancellationToken.None);
        page!.Count = 1;
        page.During = () =>
        {
            page.During = null;
            session.DetachTransport(first);
        };

        var sent = await session.RenderAndSendAsync(null, false);
        session.AttachTransport(second, CancellationToken.None);
        await session.RenderAndSendAsync(null, false);

        Assert.False(sent, "nobody was left to send it to");
        Assert.Empty(first.Sent);
        Assert.Contains(second.Sent, frame => frame.Contains("count 1", StringComparison.Ordinal));
    }

    /// <summary>The same, when the connection is still attached but closed by the time its frame is sent.</summary>
    [Fact]
    public async Task A_frame_whose_connection_closes_during_its_render_is_sent_to_the_next_one()
    {
        Watched? page = null;
        var session = NewStore().Create(_ => page = new Watched());
        var first = new FakeTransport();
        var second = new FakeTransport();
        session.AttachTransport(first, CancellationToken.None);
        page!.Count = 2;
        page.During = () =>
        {
            page.During = null;
            first.IsOpen = false;
        };

        var sent = await session.RenderAndSendAsync(null, false);
        session.AttachTransport(second, CancellationToken.None);
        await session.RenderAndSendAsync(null, false);

        Assert.False(sent, "nobody was left to send it to");
        Assert.Empty(first.Sent);
        Assert.Contains(second.Sent, frame => frame.Contains("count 2", StringComparison.Ordinal));
    }

    /// <summary>
    ///     What a frame that never left carried is shown by the next connection, however much changed after it. The
    ///     catch-up was a diff against the render that frame was made from, which the browser never had: the change
    ///     in it was left out, and a page that kept changing — a ticking source — never showed it (#1258).
    /// </summary>
    [Fact]
    public async Task The_next_connection_is_shown_what_a_lost_frame_carried_beside_what_changed_since()
    {
        Watched? page = null;
        var session = NewStore().Create(_ => page = new Watched());
        var first = new FakeTransport();
        var second = new FakeTransport();
        session.AttachTransport(first, CancellationToken.None);
        await session.RenderAndSendAsync(null, false);
        page!.Count = 1;
        page.During = () =>
        {
            page.During = null;
            first.IsOpen = false;
        };
        await session.RenderAndSendAsync(null, false);
        page.Ticks = 1;

        session.AttachTransport(second, CancellationToken.None);
        await session.RenderAndSendAsync(null, false);

        Assert.Single(first.Sent);
        Assert.Contains("count 1", second.Sent[^1], StringComparison.Ordinal);
        Assert.Contains("ticks 1", second.Sent[^1], StringComparison.Ordinal);
    }

    /// <summary>
    ///     An event stream that has ended says so when handed a frame, as a closed socket does. It used to take the frame
    ///     and drop it, and the session counted it as the browser's.
    /// </summary>
    [Fact]
    public async Task An_ended_event_stream_refuses_a_frame_rather_than_dropping_it()
    {
        var writer = new System.IO.Pipelines.Pipe().Writer;
        var stream = new SseTransport(writer, generation: 1);

        stream.Abort();

        await Assert.ThrowsAsync<IOException>(async () => await stream.SendAsync("frame"u8.ToArray(), CancellationToken.None));
    }

    /// <summary>
    ///     The shutdown announcement reaches the connection the session currently has, whichever it is.
    /// </summary>
    [Fact]
    public async Task The_shutdown_close_goes_to_the_attached_connection()
    {
        var session = NewStore().Create(_ => new Shell());
        var first = new FakeTransport();
        var second = new FakeTransport();

        session.AttachTransport(first, CancellationToken.None);
        session.AttachTransport(second, CancellationToken.None);

        await session.CloseForShutdownAsync(CancellationToken.None);

        Assert.Equal("server-shutdown", second.ClosedWith);
        Assert.Null(first.ClosedWith);
    }
}
