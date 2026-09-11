using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.DevTools.Probe;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     What the probe records, driven directly against real sessions from the test host. The probe under test is its own
///     instance, never the one in <see cref="RaskDevToolsHook" />; the hosts still install theirs, which is why this class
///     is in <see cref="DevToolsHookCollection" />.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed partial class DevToolsProbeTests
{
    [Fact]
    public async Task A_render_frame_is_recorded_inbound_with_the_diff_that_produced_it()
    {
        using var host = Host();
        var session = await SessionFromPage(host);
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);

        probe.DiffComputed(session, opCount: 4, usedDiff: true, startTimestamp: 0);
        probe.FrameSent(session, bytes: 321);

        Assert.True(feeds.TryGet(session, out var feed));
        var sent = Assert.Single(feed.WireSnapshot());
        Assert.Equal(DevToolsWireDirection.In, sent.Direction);
        Assert.Equal("frame", sent.Kind);
        Assert.Equal(321, sent.Bytes);
        Assert.Equal(4, sent.DiffOps);
    }

    [Fact]
    public async Task A_frame_from_the_page_is_recorded_outbound_by_its_type()
    {
        using var host = Host();
        var session = await SessionFromPage(host);
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);

        using (var withType = JsonDocument.Parse("""{"type":"event","id":"h1"}"""))
        {
            probe.FrameReceived(session, bytes: 27, withType.RootElement);
        }

        using (var withoutType = JsonDocument.Parse("""{"id":"h1"}"""))
        {
            probe.FrameReceived(session, bytes: 9, withoutType.RootElement);
        }

        using (var notAnObject = JsonDocument.Parse("[1,2]"))
        {
            probe.FrameReceived(session, bytes: 5, notAnObject.RootElement);
        }

        Assert.True(feeds.TryGet(session, out var feed));
        var events = feed.WireSnapshot();
        Assert.All(events, e => Assert.Equal(DevToolsWireDirection.Out, e.Direction));
        Assert.Equal(["event", "?", "?"], events.Select(e => e.Kind));
        Assert.Equal([27, 9, 5], events.Select(e => e.Bytes));
    }

    [Fact]
    public async Task The_devtools_own_sessions_are_never_recorded()
    {
        using var host = Host();
        var session = await SessionFromPage(host);
        session.Services.GetRequiredService<RouteState>().Path = DevToolsProbe.PanelPrefix + "/";
        var feeds = new DevToolsFeeds();
        var probe = new DevToolsProbe(feeds);

        probe.DiffComputed(session, opCount: 2, usedDiff: true, startTimestamp: 0);
        probe.FrameSent(session, bytes: 100);
        using (var frame = JsonDocument.Parse("""{"type":"event"}"""))
        {
            probe.FrameReceived(session, bytes: 20, frame.RootElement);
        }

        Assert.False(feeds.TryGet(session, out _));
    }

    [Fact]
    public async Task An_app_page_that_merely_starts_with_the_same_letters_is_recorded()
    {
        using var host = Host();
        var session = await SessionFromPage(host);
        session.Services.GetRequiredService<RouteState>().Path = DevToolsProbe.PanelPrefix + "x/";
        var feeds = new DevToolsFeeds();

        new DevToolsProbe(feeds).FrameSent(session, bytes: 100);

        Assert.True(feeds.TryGet(session, out var feed));
        Assert.Single(feed.WireSnapshot());
    }

    private static RaskTestHost Host() =>
        RaskTestHost.Create<DevToolsTestApp>(
            configureServices: s => RaskDevToolsLoader.Attach(s),
            environment: "Development");

    /// <summary>The live session the host minted for its page, found by the id the page carries.</summary>
    private static async Task<LiveSessionBase> SessionFromPage(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/");
        var match = SessionId().Match(html);
        Assert.True(match.Success, "the page carries no session id:" + Environment.NewLine + html);

        var session = host.Store.Get(match.Groups[1].Value);
        Assert.NotNull(session);
        return session;
    }

    [GeneratedRegex("data-rask-root=\"([^\"]+)\"")]
    private static partial Regex SessionId();
}
