using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Hibernation;
using Rask.Server.Tests.Infrastructure;
using static Rask.Server.Tests.Infrastructure.FrameFields;

namespace Rask.Server.Tests.Live;

/// <summary>
///     A tab out of sight long enough is asked to hibernate: its session is let go into the tab's resume record, and
///     the page is rebuilt from that record when the tab is looked at again (#1322).
/// </summary>
/// <remarks>
///     Each test drives one round by hand (<see cref="SessionHibernation.AskDueAsync" />); the host's own round is set
///     an hour apart so it never runs under a test. The tab's side — what it says yes to — is rask-hibernate.ts, proven
///     by <c>HibernationFixtureTests</c> and in a browser by the Server E2E journey.
/// </remarks>
public sealed class SessionHibernationTests
{
    private const string StateKey = "counter";

    private static readonly TimeSpan Briefly = TimeSpan.FromMilliseconds(300);

    /// <summary>A page that declares one value and renders it, with the path it is on.</summary>
    private sealed class CounterApp(IPersistentState state, RouteState route) : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render()
        {
            state.TryGet<int>(StateKey, out var count);
            return
            [
                Markup.Div[count.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                Markup.Div[route.Path]
            ];
        }
    }

    private static SessionHibernationOptions AtOnce(TimeSpan? askAgainAfter = null) => new()
    {
        HiddenFor = TimeSpan.Zero,
        Every = TimeSpan.FromHours(1),
        AskAgainAfter = askAgainAfter ?? TimeSpan.FromHours(1),
    };

    private static RaskTestHost Host(SessionHibernationOptions? options = null, Action<RaskServerOptions>? server = null) =>
        RaskTestHost.Create<CounterApp>(
            configureServices: s => s.AddSingleton(options ?? AtOnce()),
            configureServer: server);

    /// <summary>Serves a page, connects to it, and declares <paramref name="seed" /> as an event handler would.</summary>
    private static async Task<(LiveSession Session, ILiveTestConnection Tab)> OpenAsync(
        RaskTestHost host, LiveTransportKind transport, int seed = 41, string path = "/start")
    {
        var page = await host.Http.GetAsync(path);
        var sessionId = SessionIdFrom(await page.Content.ReadAsStringAsync());
        var tab = await LiveTestConnection.OpenAsync(host, transport, sessionId);
        var session = host.Store.Peek(sessionId)!;
        await WaitFor.True(() => session.HasOpenTransport, "the tab's connection attaches");

        session.Services.GetRequiredService<IPersistentState>().Persist(StateKey, seed);
        await session.View.StateHasChangedAsync();
        await tab.SettledAsync();
        return (session, tab);
    }

    private static async Task HideAsync(LiveSession session, ILiveTestConnection tab)
    {
        await tab.SendJsonAsync(new { type = "visibility", hidden = true });
        await WaitFor.True(() => session.Hibernation.Hidden, "the session hears its tab went out of sight");
        await WaitFor.True(() => session.Settled, "the session has nothing under way");
    }

    private static Task<string> OfferAsync(ILiveTestConnection tab) =>
        tab.ReceiveUntilAsync(f => StringField(f, "type") == "hibernate", "the offer to hibernate");

    private static SessionHibernation HibernationOf(RaskTestHost host) => host.Services.GetRequiredService<SessionHibernation>();

    [Theory]
    [MemberData(nameof(LiveTestConnection.Transports), MemberType = typeof(LiveTestConnection))]
    public async Task A_hidden_tab_that_agrees_lets_its_session_go_and_gets_its_page_back_when_shown(LiveTransportKind transport)
    {
        using var host = Host();
        var (session, tab) = await OpenAsync(host, transport, seed: 41, path: "/orders/7");
        await HideAsync(session, tab);
        var asked = await HibernationOf(host).AskDueAsync(TestContext.Current.CancellationToken);
        var record = StringField(await OfferAsync(tab), "resume")!;

        await tab.SendJsonAsync(new { type = "hibernate", ok = true });
        var closed = await tab.TryReceiveCloseReasonAsync(LiveFrames.HangCeiling);
        await WaitFor.True(() => host.Store.Peek(session.Id) is null, "the session is let go");
        await tab.DisposeAsync();
        await using var back = await LiveTestConnection.OpenAsync(host, transport, session.Id, record);
        var html = StringField(
            await back.ReceiveUntilAsync(f => StringField(f, "html") is not null, "the rebuilt page"), "html")!;
        await back.SendJsonAsync(new { type = "navigate", path = "/orders/9", query = "" });
        var moved = await back.ReceiveUntilAsync(f => f.Contains("/orders/9", StringComparison.Ordinal), "the next page");

        Assert.Equal(1, asked);
        Assert.Equal("hibernate", closed);
        Assert.Contains(">41<", html, StringComparison.Ordinal);
        Assert.Contains("/orders/7", html, StringComparison.Ordinal);
        Assert.NotEqual(session.Id, SessionIdFrom(html));
        Assert.Contains("/orders/9", moved, StringComparison.Ordinal);
        Assert.True(back.IsOpen);
    }

    [Fact]
    public async Task A_tab_that_says_not_now_keeps_its_session_and_is_asked_again_only_later()
    {
        var clock = new StoppedClock();
        using var host = Host(AtOnce(askAgainAfter: TimeSpan.FromMinutes(1)));
        host.Store.Clock = clock;
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var _ = tab;
        await HideAsync(session, tab);
        var hibernation = HibernationOf(host);
        await hibernation.AskDueAsync(TestContext.Current.CancellationToken);
        await OfferAsync(tab);

        await tab.SendJsonAsync(new { type = "hibernate", ok = false, why = "input" });
        await tab.SettledAsync();
        var soon = await hibernation.AskDueAsync(TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        await WaitFor.True(() => session.Settled, "the session has nothing under way");
        var later = await hibernation.AskDueAsync(TestContext.Current.CancellationToken);

        Assert.Same(session, host.Store.Peek(session.Id));
        Assert.True(tab.IsOpen);
        Assert.False(session.Hibernation.Agreed);
        Assert.Equal(0, soon);
        Assert.Equal(1, later);
    }

    [Fact]
    public async Task A_tab_in_sight_is_never_asked_and_one_shown_again_is_asked_no_more()
    {
        using var host = Host();
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var _ = tab;
        var hibernation = HibernationOf(host);

        var inSight = await hibernation.AskDueAsync(TestContext.Current.CancellationToken);
        await HideAsync(session, tab);
        await tab.SendJsonAsync(new { type = "visibility", hidden = false });
        await WaitFor.True(() => !session.Hibernation.Hidden, "the session hears its tab is back");
        var shownAgain = await hibernation.AskDueAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, inSight);
        Assert.Equal(0, shownAgain);
        Assert.Null(await tab.TryReceiveTextAsync(Briefly));
    }

    [Fact]
    public async Task A_session_with_a_handler_running_is_put_off_and_its_tab_is_not_asked()
    {
        using var host = Host();
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var _ = tab;
        await HideAsync(session, tab);
        await session.Lock.WaitAsync(TestContext.Current.CancellationToken);

        int asked;
        try
        {
            asked = await HibernationOf(host).AskDueAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            session.Lock.Release();
        }

        Assert.Equal(0, asked);
        Assert.NotEqual(0, session.Hibernation.AskedAt);
        Assert.False(session.Hibernation.Offered);
        Assert.Null(await tab.TryReceiveTextAsync(Briefly));
    }

    [Fact]
    public async Task A_yes_nobody_asked_for_is_not_taken()
    {
        using var host = Host();
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var _ = tab;
        await HideAsync(session, tab);

        await tab.SendJsonAsync(new { type = "hibernate", ok = true });
        await tab.SettledAsync();

        Assert.True(tab.IsOpen);
        Assert.False(session.Hibernation.Agreed);
        Assert.Same(session, host.Store.Peek(session.Id));
    }

    [Fact]
    public async Task Under_pressure_the_tabs_out_of_sight_longest_are_asked_first()
    {
        var clock = new StoppedClock();
        using var host = Host(new SessionHibernationOptions
        {
            HiddenFor = TimeSpan.FromHours(1), HiddenUnderPressureFor = TimeSpan.FromSeconds(30), Every = TimeSpan.FromHours(1),
        });
        host.Store.Clock = clock;
        var tabs = new List<(LiveSession Session, ILiveTestConnection Tab)>();
        for (var i = 0; i < 4; i++)
        {
            var opened = await OpenAsync(host, LiveTransportKind.WebSocket, seed: i);
            await HideAsync(opened.Session, opened.Tab);
            tabs.Add(opened);
            clock.Advance(TimeSpan.FromSeconds(20));
        }

        var hibernation = HibernationOf(host);
        var now = clock.GetTimestamp();

        var calm = hibernation.Due(host.Store.Snapshot(), now, default);
        var twoOver = hibernation.Due(host.Store.Snapshot(), now, new HibernationPressure(Memory: false, OverSessions: 2));
        var shortOfMemory = await new SessionHibernation(
                host.Store, hibernation.Options, host.Services.GetRequiredService<SessionResumeSupport>(),
                () => new MemoryLoad(InUseBytes: 90, AvailableBytes: 100))
            .AskDueAsync(TestContext.Current.CancellationToken);

        Assert.Empty(calm);
        // Hidden 80, 60, 40 and 20 seconds: the last is under the 30 s floor, and the two longest go first.
        Assert.Equal([tabs[0].Session, tabs[1].Session], twoOver.Select(d => d.Session));
        Assert.All(twoOver, d => Assert.Equal(HibernationTrigger.Pressure, d.Trigger));
        Assert.Equal(1, shortOfMemory);
        Assert.True(tabs[0].Session.Hibernation.Offered);
        Assert.False(tabs[1].Session.Hibernation.Offered);
        foreach (var (_, tab) in tabs)
        {
            await tab.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(95, 100, 0, 15)]
    [InlineData(85, 100, 0, 0)]
    [InlineData(90, 100, 0, 10)]
    [InlineData(10, 0, 0, 0)]
    public void Sessions_held_past_the_cap_ask_for_as_many_as_bring_the_host_back_to_its_target(
        int live, int max, int inUse, int over)
    {
        var options = new SessionHibernationOptions();

        var pressure = HibernationPressure.Read(new MemoryLoad(inUse, 100), live, max, options);

        Assert.Equal(over, pressure.OverSessions);
        Assert.Equal(over > 0, pressure.Any);
    }

    [Theory]
    [InlineData(86, 100, true)]
    [InlineData(84, 100, false)]
    [InlineData(1_000, 0, false)]
    public void Memory_in_use_past_its_limit_is_pressure_and_an_unknown_limit_is_none(
        long inUse, long available, bool pressed)
    {
        var options = new SessionHibernationOptions();

        var pressure = HibernationPressure.Read(new MemoryLoad(inUse, available), 0, 0, options);

        Assert.Equal(pressed, pressure.Memory);
    }

    [Fact]
    public async Task A_record_tampered_with_rebuilds_nothing()
    {
        using var host = Host();
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await HideAsync(session, tab);
        await HibernationOf(host).AskDueAsync(TestContext.Current.CancellationToken);
        var record = StringField(await OfferAsync(tab), "resume")!;
        await tab.SendJsonAsync(new { type = "hibernate", ok = true });
        await WaitFor.True(() => host.Store.Peek(session.Id) is null, "the session is let go");
        await tab.DisposeAsync();
        var bytes = Convert.FromBase64String(record);
        bytes[bytes.Length / 2] ^= 0x5A;

        await using var back = await LiveTestConnection.OpenAsync(
            host, LiveTransportKind.WebSocket, session.Id, Convert.ToBase64String(bytes));
        var reply = await back.ReceiveTextAsync();

        Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);
        Assert.Equal(0, host.Store.Count);
    }

    [Fact]
    public async Task A_record_past_its_lifetime_rebuilds_nothing()
    {
        using var host = Host(server: o => o.ResumeTokenLifetime = TimeSpan.FromMilliseconds(1));
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await HideAsync(session, tab);
        await HibernationOf(host).AskDueAsync(TestContext.Current.CancellationToken);
        var record = StringField(await OfferAsync(tab), "resume")!;
        await tab.SendJsonAsync(new { type = "hibernate", ok = true });
        await WaitFor.True(() => host.Store.Peek(session.Id) is null, "the session is let go");
        await tab.DisposeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        await using var back = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, session.Id, record);
        var reply = await back.ReceiveTextAsync();

        Assert.Contains("\"status\":\"unknown\"", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_seals_no_records_never_asks()
    {
        using var host = Host(server: o => o.SessionResume = false);
        var (session, tab) = await OpenAsync(host, LiveTransportKind.WebSocket);
        await using var _ = tab;
        await tab.SendJsonAsync(new { type = "visibility", hidden = true });
        await WaitFor.True(() => session.Hibernation.Hidden, "the session hears its tab went out of sight");

        var asked = await HibernationOf(host).AskDueAsync(TestContext.Current.CancellationToken);

        Assert.False(HibernationOf(host).Enabled);
        Assert.Equal(0, asked);
    }
}
