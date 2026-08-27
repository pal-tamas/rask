using Rask.Core;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Configuration;

// The render ladder is automatic — a page climbs as far as it needs to. These switches are the
// ceiling, for an app that wants a rung it will never use turned off rather than merely unused.
//
// The contradictions matter more than the switches: a combination that cannot serve a working page is
// a configuration mistake, and a host that refuses to start is far cheaper to diagnose than a page
// that silently does nothing in production.
public class RenderModesTests
{
    [Fact]
    public void Defaults_AreTodaysBehaviour()
    {
        var modes = new RaskRenderModes();

        // Server-interactive, no static pages, no streaming, no browser runtime — exactly how Rask
        // has always worked. An app that configures nothing must notice nothing.
        Assert.True(modes.ServerInteractivity);
        Assert.False(modes.Static);
        Assert.False(modes.Streaming);
        Assert.False(modes.Wasm);
        Assert.Equal(TimeSpan.FromSeconds(5), modes.QuiescenceTimeout);
    }

    [Fact]
    public void NoInteractivityAtAll_Throws()
    {
        // A click would reach neither a server session nor a browser runtime.
        var modes = new RaskRenderModes { ServerInteractivity = false };

        var ex = Assert.Throws<InvalidOperationException>(modes.Validate);

        // The message has to say what to do, not just what is wrong: this is read by someone whose
        // app would not start.
        Assert.Contains("no", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Turn one of them on", ex.Message);
    }

    [Fact]
    public void AStageThatDoesNotExistYet_Throws()
    {
        // Announced rather than ignored. A switch that reads as supported and quietly does nothing
        // leaves the app looking configured for something it is not doing.
        //
        // Streaming is the only rung left unbuilt — Wasm now works, and turning it on is covered by
        // WasmBundleDeliveryTests instead.
        var modes = new RaskRenderModes { Streaming = true };

        var ex = Assert.Throws<InvalidOperationException>(modes.Validate);

        Assert.Contains("not implemented yet", ex.Message);
    }

    [Fact]
    public void TurningTheBrowserRungOn_NoLongerThrows()
    {
        // Guards the retirement itself. Left in place, the old "not implemented" throw would refuse to
        // start every app that opts into the rung this release added.
        var modes = new RaskRenderModes { Wasm = true };

        Assert.Null(Record.Exception(modes.Validate));
    }

    [Fact]
    public void AHostConfiguredWithAContradiction_DoesNotStart()
    {
        // The end-to-end half: validation runs when the host is built, not merely when someone calls
        // Validate by hand.
        var ex = Record.Exception(() =>
            RaskTestHost.Create<RenderModesApp>(
                configureServer: o => o.RenderModes.ServerInteractivity = false));

        Assert.IsType<InvalidOperationException>(ex);
    }

    [Fact]
    public async Task StaticIsTheSameSwitchItReplaced()
    {
        // RenderModes.Static is where the old StaticPages flag went; it must still do the one thing
        // that flag did.
        using var host = RaskTestHost.Create<RenderModesApp>(
            configureServer: o => o.RenderModes.Static = true);

        var body = await host.Http.GetStringAsync("/");

        Assert.DoesNotContain("data-rask-root", body);
        Assert.Equal(0, host.Store.Count);
    }
}

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>
public sealed partial class RenderModesApp : Component
{
    protected override Component? HeadAssets => Title["render-modes"];

    protected override Component? Render() => Div["content-only"];
}
#pragma warning restore RASK019
