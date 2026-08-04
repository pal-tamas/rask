using System.Text;
using Microsoft.Extensions.Hosting;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.WebSockets;

/// <summary>
///     The dev-only "hot reload applied" channel: its exact wire text, and the two independent gates
///     that keep it out of production.
/// </summary>
public class HotReloadMessageTests
{
    [Fact]
    public void The_applied_frame_has_the_exact_shape_the_client_branches_on()
    {
        // Asserted as a whole literal, not by deserializing: rask.js switches on `data.type` and
        // `data.status` as literal strings, so a serializer-options change that renamed or recased a
        // property would leave both sides compiling and silently stop the indicator working. The
        // client fixture asserts against this same constant.
        Assert.Equal("""{"type":"hotReload","status":"applied"}""", LivePayload.HotReloadAppliedJson);
        Assert.Equal(
            LivePayload.HotReloadAppliedJson,
            Encoding.UTF8.GetString(LivePayload.HotReloadAppliedFrame));
    }

    [Fact]
    public void The_dev_gate_is_closed_in_production()
    {
        // Gate 1. Production must never even subscribe, so no frame can be produced regardless of
        // whether the process happens to support metadata updates.
        using var host = RaskTestHost.Create<TestApp>(environment: Environments.Production);

        Assert.False(RaskEndpointExtensions.IsDevHotReloadEnabled(host.Services));
    }

    [Fact]
    public async Task Production_html_does_not_carry_the_client_dev_flag()
    {
        // Gate 2, defence in depth: without data-rask-dev on <body>, rask.js will not act on a
        // hotReload frame even if one reached it.
        using var host = RaskTestHost.Create<TestApp>(environment: Environments.Production);

        var html = await host.Http.GetStringAsync("/start");

        Assert.DoesNotContain("data-rask-dev", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-root", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_the_gate_and_the_html_flag_agree()
    {
        // The two gates must never disagree — a subscribed server that stamped no flag would send
        // frames the client silently drops, and the reverse would advertise a channel nothing feeds.
        //
        // Both are ANDed with MetadataUpdater.IsSupported, a per-process feature switch that cannot be
        // flipped from inside a running test host. So rather than assert a fixed outcome, assert the
        // two agree with each other and with the switch — which holds whichever way the host is
        // configured.
        using var host = RaskTestHost.Create<TestApp>(environment: Environments.Development);

        var enabled = RaskEndpointExtensions.IsDevHotReloadEnabled(host.Services);
        var html = await host.Http.GetStringAsync("/start");

        Assert.Equal(System.Reflection.Metadata.MetadataUpdater.IsSupported, enabled);
        Assert.Equal(enabled, html.Contains("data-rask-dev", StringComparison.Ordinal));
    }

    [Fact]
    public void InjectRootAttr_EmitsTheDevFlagOnlyWhenAsked()
    {
        const string html = "<html><body><p>hi</p></body></html>";

        Assert.DoesNotContain("data-rask-dev", LivePayload.InjectRootAttr(html, "s1"), StringComparison.Ordinal);

        var dev = LivePayload.InjectRootAttr(html, "s1", dev: true);
        Assert.Contains("data-rask-root=\"s1\"", dev, StringComparison.Ordinal);
        Assert.Contains("data-rask-dev", dev, StringComparison.Ordinal);
    }
}
